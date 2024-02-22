using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Client.HttpsClient.Polly;
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
        private readonly PollyStrategy _polly = new PollyStrategy();

        private readonly IQueueManager _queueManager;
        private readonly ILoggerManager _logger;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;

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


        private Task<HttpResponseMessage> RequestToServer(object value, string uri)
        {
            // var json = JsonConvert.SerializeObject(value);
            //
            // _logger.Debug($"{nameof(RequestToServer)}: {json}");

            var pipeline = _endpoints.IsCommandRequest(uri) ? _polly.CommandsPipeline : _polly.DataPipeline;
            // var data = new StringContent(json, Encoding.UTF8, "application/json");

            return pipeline.ExecuteAsync(async token => await (PostAsync(uri, value, token)), _tokenSource.Token).AsTask();
        }

        
        private async Task<HttpResponseMessage> PostAsync(string uri, object data, CancellationToken token)
        { 
            Console.WriteLine("trying to connnect" + DateTime.Now);
            var testConnection = await TestConnection();

            if (!testConnection.Result)
                return null;
            Console.WriteLine("trying to send" + DateTime.Now);
            
            var json = JsonConvert.SerializeObject(data);

            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _client.PostAsync(uri, content, token);

                _queueManager.ThrowPackageSendingInfo(new PackageSendingInfo(json.Length, response));

                if (!response.IsSuccessStatusCode)
                    _logger.Error($"Failed to send data. StatusCode={response.StatusCode}. Data={json}.");
                
                if (response.IsSuccessStatusCode)
                    Console.WriteLine("send"  + DateTime.Now);
                return response;
            }
            catch (Exception exception)
            {
                _logger.Error($"Failed to send data. Error={exception.Message}. Data={json}.");
                throw;
            }
        }
    }
}