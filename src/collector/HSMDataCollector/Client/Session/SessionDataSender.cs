using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Client.Session
{
    /// <summary>
    /// The collector session as the transport of the managed collector (epic #1660 step 3,
    /// ADR-0017): value batches and registrations travel as frames on one WebSocket and are
    /// confirmed by acks. Chosen by <see cref="CollectorOptions.Transport"/>.
    ///
    /// A collector on a session sends nothing over the HTTP routes, and there is no fallback
    /// to them: while no session is up a send fails, and the queue keeps the package, as it
    /// does when the server is unreachable.
    ///
    /// The body of a frame is the JSON the HTTP route of the same name takes, byte for byte:
    /// every element is written by the serializer of the HTTP client.
    /// </summary>
    internal sealed class SessionDataSender : IDataSender, ICancelableDataSender, IErrorReportingSender, ICollectorTransport
    {
        // What the close handshake of a graceful stop may take, so that the server sees a
        // normal closure. A terminal stop does not wait.
        private static readonly TimeSpan CloseLinger = TimeSpan.FromSeconds(1);

        private readonly SessionConnection _connection;
        private readonly ICollectorLogger _logger;
        private readonly TimeSpan _requestTimeout;
        private readonly string _unavailable;

        private Action<string> _errorSink;
        private int _unavailableReported;
        private int _disposed;

        internal SessionDataSender(CollectorOptions options, ICollectorLogger logger)
        {
            _logger = logger;
            _requestTimeout = options.RequestTimeout;

            Directives = new ServerDirectives(logger);
            Url = BuildUrl(options);

            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Key", options.AccessKey),
                new KeyValuePair<string, string>(SessionProtocol.CollectorKindHeader, SessionProtocol.CollectorKind),
                new KeyValuePair<string, string>(SessionProtocol.CollectorVersionHeader, DataCollectorExtensions.Version.ToString(3)),
            };

            // As over HTTP, where a null client name sends no header value.
            if (!string.IsNullOrEmpty(options.ClientName))
                headers.Add(new KeyValuePair<string, string>("ClientName", options.ClientName));

            headers.AddRange(options.ExtraRequestHeaders);

            _unavailable = FindWhySessionCannotExist(options);
            _connection = new SessionConnection(Url, headers, options.AllowUntrustedServerCertificate, logger, ReportError, Directives, options.SessionTimings);
        }

        /// <summary>The session endpoint: ws:// or wss://host[:port]/api/sensors/session.</summary>
        internal Uri Url { get; }

        internal SessionConnection Connection => _connection;

        /// <summary>The directives the server pushes on the session (#1660 step 5) arrive here.</summary>
        internal ServerDirectives Directives { get; }

        /// <summary>The session has no op for a file value yet (#1660 step 2b).</summary>
        public bool CanCarryFiles => false;


        // The HTTP client's address rule (https unless plaintext is allowed and asked for),
        // with the WebSocket scheme of the same security.
        private static Uri BuildUrl(CollectorOptions options)
        {
            var address = new Endpoints(options).ConnectionAddress.TrimEnd('/') + SessionProtocol.EndpointSuffix;
            var builder = new UriBuilder(address);

            builder.Scheme = string.Equals(builder.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ? "ws" : "wss";

            return builder.Uri;
        }

        // A setting the session cannot honour, or a host it cannot exist on. Null when it can.
        private static string FindWhySessionCannotExist(CollectorOptions options)
        {
#if !NET6_0_OR_GREATER
            // ClientWebSocket has no per-connection certificate callback on .NET Framework, and
            // the process-wide one is not the collector's to change.
            if (options.AllowUntrustedServerCertificate)
                return "AllowUntrustedServerCertificate cannot be honoured for a collector session on .NET Framework";
#endif
            try
            {
                using (new ClientWebSocket()) { }
            }
            catch (PlatformNotSupportedException ex)
            {
                return $"this operating system has no WebSocket client ({ex.Message})";
            }

            return null;
        }


        /// <summary>
        /// Starts connecting in the background; never waits for the session and never throws.
        /// When a session cannot exist on this host, one error says why and nothing is started:
        /// the collector does not switch to HTTP by itself.
        /// </summary>
        public void Start()
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;

            if (_unavailable != null)
            {
                if (Interlocked.Exchange(ref _unavailableReported, 1) == 0)
                    ReportError($"The collector session transport is unavailable: {_unavailable}. Nothing is delivered until the transport setting or that setting is changed.");

                return;
            }

            _connection.Start();
        }

        public async Task StopAsync(bool terminal)
        {
            try
            {
                await _connection.StopAsync(terminal ? TimeSpan.Zero : CloseLinger).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Closing the collector session failed: {ex.Message}");
            }
        }

        public Task WaitForConnectionAsync(CancellationToken token) => _connection.WaitUntilUpAsync(token);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _connection.Dispose();
        }

        public void CancelPendingRequests() => _connection.CancelPendingRequests();

        public void RouteErrorsTo(Action<string> sink) => Volatile.Write(ref _errorSink, sink);

        private void ReportError(string message)
        {
            var sink = Volatile.Read(ref _errorSink);

            if (sink != null)
                sink(message);
            else
                _logger?.Error(message);
        }


        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
            SendAsync(SessionProtocol.OpList, items, isRegistration: false, token);

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
            SendAsync(SessionProtocol.OpList, items, isRegistration: false, token);

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
            SendAsync(SessionProtocol.OpCommands, commands, isRegistration: true, token);

        /// <summary>
        /// Never reached through the collector: a file value is refused before it is queued while
        /// the transport is a session (<see cref="CanCarryFiles"/>), because the file queue would
        /// send, fail and re-queue it for ever. A direct caller gets the refusal as a status.
        /// </summary>
        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
            new ValueTask<PackageSendingInfo>(new PackageSendingInfo(0, 400, "the collector session transport cannot carry file values yet"));

        /// <summary>
        /// Whether a session can be opened. With one up the answer is yes; otherwise a session is
        /// opened and closed just for the question. It does not go to the HTTP test route.
        /// </summary>
        public async ValueTask<ConnectionResult> TestConnectionAsync()
        {
            if (_unavailable != null)
                return new ConnectionResult(null, _unavailable);

            if (_connection.IsUp)
                return ConnectionResult.Ok;

            return await _connection.ProbeAsync().ConfigureAwait(false);
        }


        // One package as frames. A package that does not fit one frame is split, and every frame
        // is acknowledged on its own. The rule for the result is the HTTP rule: 2xx is delivered;
        // a registration answered 4xx is final; anything else, or no ack, sends what is not yet
        // settled back to the queue.
        private async ValueTask<PackageSendingInfo> SendAsync<T>(string op, IEnumerable<T> items, bool isRegistration, CancellationToken token)
        {
            var room = _connection.MaxBodyBytes;

            if (room == 0)
                return PackageSendingInfo.NotSent(_connection.DownReason);

            var elements = new List<string>();
            var paths = new List<string>();

            foreach (var item in items)
            {
                elements.Add(HttpRequest<T>.SerializeElement(item));
                paths.Add(PathOf(item));
            }

            // What is done with, by position in the package: acknowledged, refused for good, or
            // dropped and counted. On a failure the queue takes back only the rest.
            var settled = new bool[elements.Count];
            double contentSize = 0;
            var dropped = 0;
            var unsendable = 0;
            var index = 0;

            while (index < elements.Count)
            {
                var frameStart = index;
                var body = new StringBuilder("[");
                var bodyBytes = 2; // the brackets
                var count = 0;

                while (index < elements.Count)
                {
                    var element = elements[index];
                    var bytes = Encoding.UTF8.GetByteCount(element);

                    // An element that cannot fit a frame on its own has no way to the server.
                    // Sent, it would close the session, come back with the package and block
                    // everything behind it, so it leaves here, loudly.
                    if (bytes + 2 > room)
                    {
                        if (isRegistration)
                        {
                            ReportError($"The registration of {paths[index]} was not sent: its {bytes} bytes are larger than a collector session frame ({room} bytes).");
                        }
                        else
                        {
                            ReportError($"Dropped a value of {paths[index]}: its {bytes} bytes are larger than a collector session frame ({room} bytes).");
                            dropped++;
                        }

                        settled[index] = true;
                        unsendable++;
                        index++;
                        continue;
                    }

                    if (bodyBytes + bytes + (count == 0 ? 0 : 1) > room)
                        break;

                    if (count != 0)
                    {
                        body.Append(',');
                        bodyBytes++;
                    }

                    body.Append(element);
                    bodyBytes += bytes;
                    count++;
                    index++;
                }

                if (count == 0)
                    continue;

                body.Append(']');
                contentSize += body.Length;

                SessionAck ack;

                try
                {
                    ack = await _connection.RequestAsync(op, body.ToString(), _requestTimeout, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && (frameStart > 0 || unsendable > 0))
                {
                    // Cancelled part of the way through a package (a stop). The exception would
                    // send the whole package back, and what was delivered before this frame would
                    // go twice; said as a failed send, the queue takes back only the rest.
                    return PackageSendingInfo.NotSent("cancelled", settled, dropped);
                }

                if (ack.IsSuccess)
                {
                    if (isRegistration)
                        LogRegistrationErrors(ack.Body);

                    MarkSettled(settled, frameStart, index);
                    continue;
                }

                var error = ack.Acked
                    ? $"Code: {ack.Status}. {ack.Error}".TrimEnd()
                    : ack.Error;

                // A registration answered 4xx is refused for good (#1659 A2).
                if (isRegistration && ack.Acked && ack.Status >= 400 && ack.Status < 500)
                {
                    // The whole package was this one frame: its status is the package's, and the
                    // queue reports the refusal and drops the package, as it does over HTTP.
                    if (frameStart == 0 && index >= elements.Count && unsendable == 0)
                        return PackageSendingInfo.Answered(contentSize, ack.Status, error, dropped);

                    // One frame of several: said here, because the queue only hears about the
                    // package as a whole, and the frames after this one still go.
                    ReportError($"The server rejected {count} command(s) with status {ack.Status}; not sent again. {ack.Error}".TrimEnd());
                    MarkSettled(settled, frameStart, index);
                    continue;
                }

                // This frame and what follows it go back to the queue; what is settled does not.
                return ack.Acked
                    ? PackageSendingInfo.Failed(contentSize, ack.Status, error, settled, dropped)
                    : PackageSendingInfo.NotSent(error, settled, dropped);
            }

            return PackageSendingInfo.Answered(contentSize, 200, null, dropped);
        }

        private static void MarkSettled(bool[] settled, int from, int to)
        {
            for (var position = from; position < to; position++)
                settled[position] = true;
        }

        private static string PathOf<T>(T item)
        {
            switch (item)
            {
                case SensorValueBase value:
                    return value.Path;
                case CommandRequestBase command:
                    return command.Path;
                default:
                    return "?";
            }
        }

        // A success ack of a commands frame carries the per-path errors of the route: a
        // registration the server took but could not apply as asked.
        private void LogRegistrationErrors(string body)
        {
            if (string.IsNullOrEmpty(body))
                return;

            try
            {
                var errors = JsonSerializer.Deserialize<Dictionary<string, string>>(body);

                if (errors == null)
                    return;

                foreach (var error in errors)
                    _logger?.Error($"Command request for {error.Key} has been faulted. {error.Value}.");
            }
            catch (JsonException)
            {
                _logger?.Debug("The body of a commands ack is not a path-to-error dictionary.");
            }
        }
    }
}
