using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Client
{
    internal sealed class HsmHttpsClient : IDataSender, IDisposable
    {
        private const string HeaderClientName = "ClientName";
        private const string HeaderAccessKey = "Key";

        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        private readonly CommandHandler _commandsHandler;
        private readonly DataHandlers _dataHandler;
        private readonly DataHandlers _priorityDataHandler;
        private readonly DataHandlers _fileHandler;

        private readonly ICollectorLogger _logger;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;

        internal HsmHttpsClient(CollectorOptions options, ICollectorLogger logger)
        {
            _endpoints = new Endpoints(options);
            _logger = logger;

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(HeaderClientName, options.ClientName);
            _client.DefaultRequestHeaders.Add(HeaderAccessKey, options.AccessKey);

            _commandsHandler = new CommandHandler(this, _endpoints, _logger);
            _dataHandler     = new DataHandlers(this, _endpoints, _logger);
            _priorityDataHandler = new DataHandlers(this, _endpoints, _logger);
            _fileHandler = new DataHandlers(this, _endpoints, _logger);
        }


        public void Dispose()
        {
            _tokenSource.Cancel();

            _commandsHandler.Dispose();
            _dataHandler.Dispose();
            _client.Dispose();
        }

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
        {
            return _commandsHandler.SendAsync(commands, token);
        }

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            return _dataHandler.SendAsync(items, token);
        }

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            return _priorityDataHandler.SendAsync(items, token);
        }

        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
        {
            return _fileHandler.SendAsync(file, token);
        }

        internal ValueTask<HttpResponseMessage> SendRequestAsync(string uri, HttpContent stringContent, CancellationToken token) => new ValueTask<HttpResponseMessage>(_client.PostAsync(uri, stringContent, token));


        public async ValueTask<ConnectionResult> TestConnectionAsync()
        {
            try
            {
                var connect = await _client.GetAsync(_endpoints.TestConnection, _tokenSource.Token).ConfigureAwait(false);

                if (connect.IsSuccessStatusCode)
                    return ConnectionResult.Ok;

                var error = await connect.Content.ReadAsStringAsync().ConfigureAwait(false);

                return new ConnectionResult(connect.StatusCode, $"{connect.ReasonPhrase} ({error})");
            }
            catch (Exception ex)
            {
                return new ConnectionResult(null, ex.Message);
            }
        }

    }
}