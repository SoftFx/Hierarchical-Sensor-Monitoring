using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Client
{
    internal sealed class HsmHttpsClient : IDataSender, IDisposable
    {
        private const string HeaderClientName = "ClientName";
        private const string HeaderAccessKey = "Key";

        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        private readonly CommandHandler _commandsHandler;
        private readonly DataHandlers _dataHandler;

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

            _commandsHandler = new CommandHandler(_endpoints, _logger);
            _dataHandler     = new DataHandlers( _endpoints, _logger);
        }


        public void Dispose()
        {
            _tokenSource.Cancel();

            _commandsHandler.SendRequestEvent -= _client.PostAsync;
            _dataHandler.SendRequestEvent -= _client.PostAsync;

            _commandsHandler.Dispose();
            _dataHandler.Dispose();
            _client.Dispose();
        }

        public Task<string> SendCommandAsync(CommandRequestBase command, CancellationToken token)
        {
            _commandsHandler.SendAsync(command, token);
        }

        public Task<Dictionary<string, string>> SendCommandAsync(IEnumerable<CommandRequestBase> command, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task SendDataAsync(SensorValueBase data, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task SendFileAsync(FileSensorValue file, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        internal async Task<ConnectionResult> TestConnection()
        {
            try
            {
                var connect = await _client.GetAsync(_endpoints.TestConnection, _tokenSource.Token);

                if (connect.IsSuccessStatusCode)
                    return ConnectionResult.Ok;

                var error = await connect.Content.ReadAsStringAsync();

                return new ConnectionResult(connect.StatusCode, $"{connect.ReasonPhrase} ({error})");
            }
            catch (Exception ex)
            {
                return new ConnectionResult(null, ex.Message);
            }
        }
    }
}