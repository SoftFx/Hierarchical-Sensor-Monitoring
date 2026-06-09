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
    internal sealed class HsmHttpsClient : IDataSender, ICancelableDataSender, IDisposable
    {
        private const string HeaderClientName = "ClientName";
        private const string HeaderAccessKey = "Key";

        private readonly object _tokenSourceLock = new object();
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        private readonly CommandHandler _commandsHandler;
        private readonly DataHandlers _dataHandler;
        private readonly DataHandlers _priorityDataHandler;
        private readonly DataHandlers _fileHandler;

        private readonly ICollectorLogger _logger;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;
        private int _disposed;

        internal HsmHttpsClient(CollectorOptions options, ICollectorLogger logger)
        {
            _endpoints = new Endpoints(options);
            _logger = logger;

            var httpHandler = new HttpClientHandler();

            if (options.AllowUntrustedServerCertificate)
                httpHandler.ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true;

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

            CancellationTokenSource tokenSourceToDispose;

            lock (_tokenSourceLock)
                tokenSourceToDispose = _tokenSource;

            tokenSourceToDispose.Cancel();

            _commandsHandler.Dispose();
            _dataHandler.Dispose();
            _priorityDataHandler.Dispose();
            _fileHandler.Dispose();

            tokenSourceToDispose.Dispose();
            _client.Dispose();
        }

        /// <summary>
        /// Abort all in-flight HTTP requests by cancelling the shared client-wide token and
        /// installing a fresh source so subsequent <see cref="SendRequestAsync"/> calls (e.g. a
        /// graceful Stop's bounded flush) can still proceed against the same <see cref="HttpClient"/>.
        ///
        /// Note: the previous implementation disposed <see cref="_client"/> here, which converted a
        /// `Dispose()` racing a concurrent graceful `Stop()` into silent data loss — every
        /// remaining send in the flush threw <see cref="ObjectDisposedException"/>, got re-enqueued
        /// then discarded by `ClearQueue`. Client disposal is owned by <see cref="Dispose"/>.
        /// </summary>
        public void CancelPendingRequests()
        {
            CancellationTokenSource tokenSourceToCancel;

            lock (_tokenSourceLock)
            {
                if (Volatile.Read(ref _disposed) == 1)
                    return;

                tokenSourceToCancel = _tokenSource;
                _tokenSource = new CancellationTokenSource();
            }

            // Do NOT dispose tokenSourceToCancel here: in-flight SendRequestAsync calls captured
            // its Token into linked CTSes outside the lock, and disposing the upstream while those
            // linked sources are alive can throw ObjectDisposedException from cancellation
            // callbacks. The CTS is unreferenced after this method returns and the GC will reclaim
            // it once the in-flight requests fall away. Total leak across the collector lifetime
            // is bounded by the number of CancelPendingRequests calls (typically one, at dispose).
            tokenSourceToCancel.Cancel();
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

        internal async ValueTask<HttpResponseMessage> SendRequestAsync(string uri, HttpContent stringContent, CancellationToken token)
        {
            CancellationToken pendingRequestsToken;

            lock (_tokenSourceLock)
            {
                // Surface dispose as a clean OperationCanceledException to callers instead of
                // letting them observe a disposed _tokenSource and hit ObjectDisposedException
                // when reading .Token. Dispose is terminal so a send racing it cannot succeed
                // either way, but the cancellation shape is what the BaseHandlers retry pipeline
                // and the queue's OCE branches already understand.
                if (Volatile.Read(ref _disposed) == 1)
                    throw new OperationCanceledException();

                // Capture the CancellationToken (not just the source ref) under the lock so
                // CancelPendingRequests cannot swap _tokenSource out between us reading the
                // reference and us reading .Token. A request that observed the source moments
                // before a Cancel() call still ends up linked to the cancelled token — that is
                // intentional and matches the contract of "cancel all in-flight requests": we
                // cannot promise that a request which started while the cancel was racing us
                // survives the cancel. The narrower capture just keeps the race window to the
                // single field read.
                pendingRequestsToken = _tokenSource.Token;
            }

            using (var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, pendingRequestsToken))
                return await _client.PostAsync(uri, stringContent, linkedTokenSource.Token).ConfigureAwait(false);
        }


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
