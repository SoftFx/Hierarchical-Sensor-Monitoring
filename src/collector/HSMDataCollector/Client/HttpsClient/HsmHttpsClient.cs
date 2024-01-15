using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.HttpsClient.Polly;

namespace HSMDataCollector.Client
{
    internal sealed class HsmHttpsClient : IDisposable
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private readonly IQueueManager _queueManager;
        private readonly ILoggerManager _logger;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;
        private readonly PollyStrategy _polly;

        internal CommandHandler Commands { get; }

        internal DataHandlers Data { get; }


        internal HsmHttpsClient(CollectorOptions options, IQueueManager queue, ILoggerManager logger)
        {
            _endpoints = new Endpoints(options);
            _queueManager = queue;
            _logger = logger;

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);
            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.ClientName), options.ClientName);

            Commands = new CommandHandler(queue.Commands, _endpoints, _logger);
            Commands.InvokeRequest += RequestToServer;

            Data = new DataHandlers(queue.Data, _endpoints, _logger);
            Data.InvokeRequest += RequestToServer;

            _polly = new PollyStrategy();
            _polly.Log += LogErrorRetry;
        }


        public void Dispose()
        {
            _tokenSource.Cancel();

            Commands.InvokeRequest -= RequestToServer;
            Data.InvokeRequest -= RequestToServer;

            _client.Dispose();
        }


        internal async Task<ConnectionResult> TestConnection()
        {
            try
            {
                var connect = await _client.GetAsync(_endpoints.TestConnection, _tokenSource.Token);

                return connect.IsSuccessStatusCode
                       ? ConnectionResult.Ok
                       : new ConnectionResult(connect.StatusCode, $"{connect.ReasonPhrase} ({await connect.Content.ReadAsStringAsync()})");
            }
            catch (Exception ex)
            {
                return new ConnectionResult(null, ex.Message);
            }
        }


        private async Task<HttpResponseMessage> RequestToServer(object value, string uri)
        {
            var json = JsonConvert.SerializeObject(value);

            _logger.Debug($"{nameof(RequestToServer)}: {json}");

            var data = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            response = await _polly.Pipeline.ExecuteAsync<HttpResponseMessage>(async token =>
            {
               response = await _client.PostAsync(uri, data, token);
               _queueManager.ThrowPackageSendingInfo(new PackageSendingInfo(json.Length, response));

               if (!response.IsSuccessStatusCode)
                   _logger.Error($"Failed to send data. StatusCode={response.StatusCode}. Data={json}.");

               return response;
            }, _tokenSource.Token);

            return response;
        }

        private void LogErrorRetry(string message)
        {
            _logger.Error($"Failed to send data. Error={message}.");
        }
    }
}