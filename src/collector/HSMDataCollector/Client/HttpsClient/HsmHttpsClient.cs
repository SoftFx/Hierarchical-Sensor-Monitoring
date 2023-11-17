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

namespace HSMDataCollector.Client
{
    internal sealed class HsmHttpsClient : IDisposable
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private readonly ILoggerManager _logger;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;


        internal CommandHandler Commands { get; }

        internal DataHandlers Data { get; }


        internal HsmHttpsClient(CollectorOptions options, IQueueManager queue, ILoggerManager logger)
        {
            _endpoints = new Endpoints(options);
            _logger = logger;

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);

            Commands = new CommandHandler(queue.Commands, _endpoints, _logger);
            Commands.InvokeRequest += RequestToServer;

            Data = new DataHandlers(queue.Data, _endpoints, _logger);
            Data.InvokeRequest += RequestToServer;
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
            string json = JsonConvert.SerializeObject(value);

            _logger.Debug($"{nameof(RequestToServer)}: {json}");

            var data = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await _client.PostAsync(uri, data, _tokenSource.Token);

            if (!res.IsSuccessStatusCode)
                _logger.Error($"Failed to send data. StatusCode={res.StatusCode}. Data={json}.");

            return res;
        }
    }
}