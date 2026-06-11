using System;
using System.Collections.Generic;
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
        private int _disposed;

        // Bounded connection lifetime forces periodic DNS re-resolution (#1102-E4): without it a
        // busy keep-alive connection sticks to a stale IP indefinitely (e.g. after an LB move).
        private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

        internal HsmHttpsClient(CollectorOptions options, ICollectorLogger logger)
        {
            _endpoints = new Endpoints(options);
            _logger = logger;

#if NET6_0_OR_GREATER
            var httpHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = ConnectionLifetime,
            };

            if (options.AllowUntrustedServerCertificate)
                httpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
#else
            var httpHandler = new HttpClientHandler();

            if (options.AllowUntrustedServerCertificate)
                httpHandler.ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true;

            try
            {
                // .NET Framework has no PooledConnectionLifetime; the ServicePoint lease is the
                // equivalent knob for the collector endpoint.
                System.Net.ServicePointManager.FindServicePoint(new Uri(_endpoints.ConnectionAddress)).ConnectionLeaseTimeout =
                    (int)ConnectionLifetime.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to bound the connection lease for '{_endpoints.ConnectionAddress}': {ex}");
            }
#endif

            _client = new HttpClient(httpHandler);
            _client.Timeout = options.RequestTimeout;

            _client.DefaultRequestHeaders.Add(HeaderClientName, options.ClientName);
            _client.DefaultRequestHeaders.Add(HeaderAccessKey, options.AccessKey);

            _commandsHandler = new CommandHandler(this, _endpoints, _logger);
            _dataHandler     = new DataHandlers(this, _endpoints, _logger);
            _priorityDataHandler = new DataHandlers(this, _endpoints, _logger);
            _fileHandler = new DataHandlers(this, _endpoints, _logger);
        }


        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _tokenSource.Cancel();

            _commandsHandler.Dispose();
            _dataHandler.Dispose();
            _priorityDataHandler.Dispose();
            _fileHandler.Dispose();

            _tokenSource.Dispose();
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
                using (var connect = await _client.GetAsync(_endpoints.TestConnection, _tokenSource.Token).ConfigureAwait(false))
                {

                    if (connect.IsSuccessStatusCode)
                        return ConnectionResult.Ok;

                    var error = await connect.Content.ReadAsStringAsync().ConfigureAwait(false);

                    return new ConnectionResult(connect.StatusCode, $"{connect.ReasonPhrase} ({error})");
                }
            }
            catch (Exception ex)
            {
                return new ConnectionResult(null, ex.Message);
            }
        }

    }
}
