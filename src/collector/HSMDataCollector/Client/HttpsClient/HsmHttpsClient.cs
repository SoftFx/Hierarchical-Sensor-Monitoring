using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Client.HttpsClient.Polly;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using Newtonsoft.Json;
using Polly;
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

        private readonly CommandHandler _commandsHandler;
        private readonly DataHandlers _dataHandler;
        private readonly PollyStrategy _polly;

        private readonly IQueueManager _queueManager;
        private readonly ILoggerManager _logger;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;


        internal HsmHttpsClient(CollectorOptions options, IQueueManager queue, ILoggerManager logger)
        {
            _endpoints = new Endpoints(options);
            _queueManager = queue;
            _logger = logger;

            _polly = new PollyStrategy(_logger);

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);
            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.ClientName), options.ClientName);

            _commandsHandler = new CommandHandler(queue.Commands, _endpoints, _logger);
            _commandsHandler.InvokeRequest += RequestToServer;

            _dataHandler = new DataHandlers(queue.Data, _endpoints, _logger);
            _dataHandler.InvokeRequest += RequestToServer;
        }


        public void Dispose()
        {
            _tokenSource.Cancel();

            _commandsHandler.InvokeRequest -= RequestToServer;
            _dataHandler.InvokeRequest -= RequestToServer;

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
            var pipeline = _endpoints.IsCommandRequest(uri) ? _polly.CommandsPipeline : _polly.DataPipeline;

            return pipeline.ExecuteAsync(async context => await PostAsync(context, uri, value, _tokenSource.Token), PollyHelper.GetContext(_tokenSource)).AsTask();
        }


        private async Task<HttpResponseMessage> PostAsync(ResilienceContext context, string uri, object data, CancellationToken token)
        {
            if (context.Properties.TryGetValue(PollyHelper.AttemptKey, out _))
                await _client.GetAsync(_endpoints.TestConnection, _tokenSource.Token);

            var json = JsonConvert.SerializeObject(data);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            context.Properties.Set(PollyHelper.DataKey, json);
            var response = await _client.PostAsync(uri, content, token);

            _queueManager.ThrowPackageSendingInfo(new PackageSendingInfo(json.Length, response));

            if (!response.IsSuccessStatusCode)
                _logger.Error($"Failed to send data. StatusCode={response.StatusCode}. Data={json}."); //should be added?

            return response;
        }
    }
}