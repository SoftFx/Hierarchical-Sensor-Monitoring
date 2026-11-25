using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Logging;
using HSMDataCollector.Tables;
using HSMTableBlocks;

namespace HSMDataCollector.Client.Session
{
    /// <summary>
    /// The table connection of the collector session (#1846 part 2; wire contract, "Table connection"): a second
    /// WebSocket to the session route with the subprotocol <c>hsm.tables.v1</c>, named after the live session by its
    /// <c>sessionId</c>, that carries table blocks as binary frames, one at a time, each answered by a text ack.
    /// <para>
    /// It is opened only while the session is up and once a table registration was accepted, by a loop of its own: at
    /// most one attempt at a time, none while the session is down, with the session's backoff and jitter between
    /// attempts. A block is sent by <see cref="PutAsync"/> (the pipeline's delivery loop, never the scheduler) and waits
    /// for its ack up to the request timeout; a connection that is lost meanwhile (no ack, a close, a new session) is
    /// replaced and the same frame, with the same producer id and sequence, is sent again on the next one.
    /// </para>
    /// <para>
    /// Nothing here throws into the host: failures are answers ("not delivered") or reports through the collector's
    /// error channel, each said once until it changes.
    /// </para>
    /// </summary>
    internal sealed class TableConnection : IDisposable
    {
        internal const int FrameHeaderSize = 40;

        /// <summary>The frame limit the server announces by default; the pipeline uses it until a hello says otherwise.</summary>
        internal const int DefaultFrameBytes = 4 * 1024 * 1024;

        private const byte FrameVersion = 1;
        private const byte KindBlock = 1;
        private const byte KindProbe = 2;

        // A put follows a block across this many lost connections before it answers "not delivered" and leaves the
        // next attempt to the pipeline's next delivery.
        private const int ConnectionsPerPut = 3;

        private static readonly TimeSpan CloseAnswerTimeout = TimeSpan.FromSeconds(1);

        // How long writing the close frame of a stop may take; the frame is a few bytes.
        private static readonly TimeSpan CloseFrameBound = TimeSpan.FromMilliseconds(250);
        private static readonly Regex StatusInMessage = new Regex(@"status code '(\d{3})'|\((\d{3})\)", RegexOptions.CultureInvariant);

        private readonly SessionConnection _session;
        private readonly ICollectorLogger _logger;
        private readonly Action<string> _reportError;
        private readonly SessionTimings _timings;
        private readonly TimeSpan _requestTimeout;
        private readonly object _lifecycle = new object();
        private readonly object _reportLock = new object();

        // Guards the pair (_link, _upSignal): a waiter that sees no link always finds a signal that is not set yet.
        private readonly object _linkGate = new object();
        private readonly SemaphoreSlim _putLock = new SemaphoreSlim(1, 1);

        private sealed class Run
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal volatile bool Stopping;
            internal Task Loop;
        }

        private Run _current;
        private volatile Link _link;
        private volatile TaskCompletionSource<bool> _upSignal = NewSignal();
        private volatile TaskCompletionSource<bool> _wantedSignal = NewSignal();
        private volatile string _downReason = "the table connection is not open";
        private string _lastReport;
        private int _maxFrameBytes = DefaultFrameBytes;
        private TableBlockLimits _limits = TableBlockLimits.Default;
        private long _nextId;
        private long _opened;
        private long _attempts;

        internal TableConnection(SessionConnection session, ICollectorLogger logger, Action<string> reportError, SessionTimings timings, TimeSpan requestTimeout)
        {
            _session = session;
            _logger = logger;
            _reportError = reportError;
            _timings = timings ?? new SessionTimings();
            _requestTimeout = requestTimeout;
        }


        /// <summary>The largest block (frame header not included) the last table hello allows; the default before one.</summary>
        internal int MaxBlockBytes => Volatile.Read(ref _maxFrameBytes) - FrameHeaderSize;

        /// <summary>The block limits of the last table hello; the defaults before one.</summary>
        internal TableBlockLimits BlockLimits => Volatile.Read(ref _limits);

        /// <summary>True while a table connection is open.</summary>
        internal bool IsUp => _link != null;

        /// <summary>
        /// For tests: a table connection is open and not over, so a put sends on it without waiting. With
        /// <see cref="ConnectionsOpened"/> it says which connection that is: a connection's number is counted before it
        /// is published, and the one before it is over (and cleared) before the next one is opened.
        /// </summary>
        internal bool IsServing => _link is Link link && !link.Ended.IsCompleted;

        /// <summary>Why no table connection is open now.</summary>
        internal string DownReason => _downReason;

        /// <summary>For tests: table connections opened so far.</summary>
        internal long ConnectionsOpened => Interlocked.Read(ref _opened);

        /// <summary>For tests: upgrades attempted so far (opened or not).</summary>
        internal long OpenAttempts => Interlocked.Read(ref _attempts);

        /// <summary>For tests: completes when the link that is open now ends; null while none is open.</summary>
        internal Task EndOfCurrentLink => _link?.Ended;

        /// <summary>
        /// For tests: the wait for the ack of a block ends when the task this returns for the block's request id
        /// completes, in place of the request timeout, so a test decides when "no ack in time" happens and no clock does.
        /// </summary>
        internal Func<long, Task> AckDeadlineForTests { get; set; }

        private static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);


        // ---- Lifecycle --------------------------------------------------------------------------------------------

        /// <summary>
        /// Starts the loop that keeps the table connection; it opens one only after <see cref="Want"/>. Returns at once.
        /// A Start after a Stop starts afresh: nothing is wanted until a registration of the new run is accepted.
        /// </summary>
        internal void Start()
        {
            lock (_lifecycle)
            {
                var running = _current;

                if (running != null && !running.Stopping && !running.Loop.IsCompleted)
                    return;

                var run = new Run();

                _wantedSignal = NewSignal();
                _downReason = "the table connection is not open yet";
                _current = run;
                run.Loop = Task.Run(() => RunAsync(run));
            }
        }

        /// <summary>A table registration was accepted: the table connection is opened once the session is up.</summary>
        internal void Want() => _wantedSignal.TrySetResult(true);

        /// <summary>
        /// Closes the table connection and ends its loop: a close handshake (1000) of at most <paramref name="linger"/>,
        /// cut short by <paramref name="budget"/>, then the socket is aborted. Bounded, idempotent, never throws.
        /// </summary>
        internal async Task StopAsync(TimeSpan linger, CancellationToken budget = default)
        {
            Run run;

            lock (_lifecycle)
            {
                run = _current;

                if (run == null || run.Stopping)
                    return;

                run.Stopping = true;
            }

            var link = _link;

            if (link != null && linger > TimeSpan.Zero)
            {
                try
                {
                    // The close frame is written even when the budget is spent (a drain that waited for an ack to its
                    // end): it is a few bytes, bounded on its own, and lets the server see a normal closure. Only the
                    // wait for the server's answer is the budget's.
                    using (var write = new CancellationTokenSource(CloseFrameBound))
                        await CloseOutputAsync(link, WebSocketCloseStatus.NormalClosure, "collector stopped", write.Token).ConfigureAwait(false);

                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget))
                    {
                        timeout.CancelAfter(linger);
                        await Task.WhenAny(link.Ended, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // A close that cannot be said politely is said by the abort below.
                }
            }

            try { run.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }

            link?.Abort("the collector is stopping");

            try
            {
                await Task.WhenAny(run.Loop, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Ends the loop and drops the socket without waiting for anything. Never throws.</summary>
        public void Dispose()
        {
            Run run;

            lock (_lifecycle)
            {
                run = _current;

                if (run != null)
                    run.Stopping = true;
            }

            try { run?.Cancellation.Cancel(); }
            catch (Exception) { }

            try { _link?.Abort("the collector is disposed"); }
            catch (Exception) { }
        }


        // ---- The loop that keeps the connection ----------------------------------------------------------------------

        private async Task RunAsync(Run run)
        {
            var token = run.Cancellation.Token;
            var attempt = 0;
            string refusedSession = null;

            while (!token.IsCancellationRequested && !run.Stopping)
            {
                try
                {
                    await WaitAsync(_wantedSignal.Task, token).ConfigureAwait(false);
                    await _session.WaitUntilUpAsync(token).ConfigureAwait(false);

                    if (!_session.TryGetSession(out var sessionId, out var sessionEnded))
                    {
                        // The session went down between the two reads: wait for the next one.
                        await Task.Delay(10, token).ConfigureAwait(false);
                        continue;
                    }

                    if (sessionId == null)
                    {
                        _downReason = "the server's session states no sessionId: the server has no table connections";
                        Report($"Cannot open the table connection to {_session.Url}: {_downReason}. Table blocks are kept (CollectorOptions.MaxTableBacklogBytes); asking again with every new session and every {Minutes(_timings.TableRefusedRetry)}.");
                        await WaitAsync(sessionEnded, token, _timings.TableRefusedRetry).ConfigureAwait(false);
                        continue;
                    }

                    if (sessionId == refusedSession)
                    {
                        // A 409 for this session: the next session is the next chance.
                        await WaitAsync(sessionEnded, token).ConfigureAwait(false);
                        continue;
                    }

                    // A stop that has begun opens nothing more: its close of the last connection must stay the last.
                    if (run.Stopping)
                        break;

                    var opened = await OpenAsync(sessionId, token).ConfigureAwait(false);

                    if (opened.Link == null)
                    {
                        _downReason = opened.Why;
                        await PauseAfterRefusalAsync(opened, sessionId, sessionEnded, attempt, token).ConfigureAwait(false);

                        if (opened.Status == 409)
                            refusedSession = sessionId;
                        else if (opened.Status != 404 && opened.Status != 400)
                            attempt++;

                        continue;
                    }

                    refusedSession = null;

                    var lived = Stopwatch.StartNew();
                    var end = await ServeAsync(opened.Link, sessionEnded, token).ConfigureAwait(false);

                    if (token.IsCancellationRequested)
                        break;

                    if (lived.Elapsed >= _timings.StableSession)
                        attempt = 0;

                    _downReason = $"the table connection ended: {end.Why}";
                    _logger?.Info($"The table connection ended after {lived.Elapsed.TotalSeconds:0.#} s: {end.Why}.");

                    if (end.ProtocolError)
                        Report($"The table connection to {_session.Url} was closed: {end.Why}. Table blocks are kept; reconnecting.");

                    // Its session ended: the next one is waited for at the top of the loop.
                    if (end.SessionEnded)
                        continue;

                    // 1000 from the server: replaced, or its session ended (which this collector may not have noticed
                    // yet). The next attempt waits for the end of the session or a backoff, whichever comes first.
                    if (end.ServerClose == (int)WebSocketCloseStatus.NormalClosure)
                    {
                        await WaitAsync(sessionEnded, token, _session.NextPause(attempt++)).ConfigureAwait(false);
                        continue;
                    }

                    await Task.Delay(_session.NextPause(attempt++), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Nothing above should throw; whatever does must not end the loop for good.
                    Report($"The table connection to {_session.Url} failed: {SessionConnection.Describe(ex)}. Table blocks are kept; retrying.");

                    try { await Task.Delay(_session.NextPause(attempt++), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }

            _downReason = "the table connection is stopped";

            try { run.Cancellation.Dispose(); }
            catch (Exception) { }
        }

        // What an upgrade the server refused means for the next attempt (wire contract, "Table connection"; the server's
        // CollectorSessionHandler is the reference of the statuses).
        private async Task PauseAfterRefusalAsync(OpenResult opened, string sessionId, Task sessionEnded, int attempt, CancellationToken token)
        {
            switch (opened.Status)
            {
                case 404:
                    // Table connections (or sessions) are off on the server: nothing changes until an operator does.
                    // A new session is the first sign that someone did (a server restarted with another setting), so
                    // it is asked again with the next session as well as on the long period.
                    Report($"The server refused the table connection with 404: table connections are switched off on {_session.Url} (CollectorSessions.MaxTableFrameBytes = 0). Table blocks are kept (CollectorOptions.MaxTableBacklogBytes, the oldest dropped and counted); asking again every {Minutes(_timings.TableRefusedRetry)} and with the next session.");
                    await WaitAsync(sessionEnded, token, _timings.TableRefusedRetry).ConfigureAwait(false);
                    return;

                case 400:
                    // A server older than table sensors knows only hsm.sensors.v1; an upgraded one comes with a new
                    // session.
                    Report($"The server refused the table connection with 400: {_session.Url} does not know the subprotocol {SessionProtocol.TablesSubprotocol} (a server older than table sensors). Table blocks are kept (CollectorOptions.MaxTableBacklogBytes, the oldest dropped and counted); asking again every {Minutes(_timings.TableRefusedRetry)} and with the next session.");
                    await WaitAsync(sessionEnded, token, _timings.TableRefusedRetry).ConfigureAwait(false);
                    return;

                case 409:
                    // The server knows no live session of this key under that id: the session may have ended there
                    // already. Waits for the next session.
                    Report($"The server refused the table connection with 409: it knows no live session {sessionId} of this key. Table blocks are kept; waiting for the next session.");
                    return;

                case 406:
                    Report($"The server refused the table connection with 406: the access key cannot send. Table blocks are kept; retrying.");
                    break;

                case 503:
                    Report($"The server refused the table connection with 503: it is shutting down. Table blocks are kept; retrying.");
                    break;

                default:
                    Report($"Cannot open the table connection to {_session.Url}: {opened.Why}. Table blocks are kept; retrying.");
                    break;
            }

            await WaitAsync(sessionEnded, token, _session.NextPause(attempt)).ConfigureAwait(false);
        }

        private static string Minutes(TimeSpan span) => span >= TimeSpan.FromMinutes(1) ? $"{span.TotalMinutes:0.#} minutes" : $"{span.TotalSeconds:0.###} s";

        // Said once until the words change: a refusal repeated on every attempt is one report.
        private void Report(string text)
        {
            lock (_reportLock)
            {
                if (text == _lastReport)
                    return;

                _lastReport = text;
            }

            try
            {
                if (_reportError != null)
                    _reportError(text);
                else
                    _logger?.Error(text);
            }
            catch (Exception)
            {
            }
        }


        // ---- Opening ---------------------------------------------------------------------------------------------------

        private readonly struct OpenResult
        {
            internal OpenResult(Link link, int status, string why)
            {
                Link = link;
                Status = status;
                Why = why;
            }

            internal Link Link { get; }

            /// <summary>The HTTP status of a refused upgrade; 0 for any other failure.</summary>
            internal int Status { get; }

            internal string Why { get; }
        }

        // The upgrade, the table hello and the probe. A refused upgrade comes back with its status.
        private async Task<OpenResult> OpenAsync(string sessionId, CancellationToken token)
        {
            Interlocked.Increment(ref _attempts);

            var socket = new ClientWebSocket();
            var handedOver = false;

            try
            {
                _session.Configure(socket, SessionProtocol.TablesSubprotocol);
                socket.Options.SetRequestHeader(SessionProtocol.SessionHeader, sessionId);

                using (var open = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    open.CancelAfter(_timings.OpenTimeout);

                    try
                    {
                        await socket.ConnectAsync(_session.Url, open.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        return new OpenResult(null, 0, $"no table connection within {_timings.OpenTimeout.TotalSeconds:0.###} s");
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested && !(ex is OperationCanceledException))
                    {
                        return new OpenResult(null, UpgradeStatus(ex), $"the upgrade failed: {SessionConnection.Describe(ex)}");
                    }

                    if (!string.Equals(socket.SubProtocol, SessionProtocol.TablesSubprotocol, StringComparison.Ordinal))
                        return new OpenResult(null, 0, $"the server did not select the {SessionProtocol.TablesSubprotocol} subprotocol");

                    Link link;

                    try
                    {
                        link = await ReadHelloAsync(socket, sessionId, open.Token).ConfigureAwait(false);
                        await ProbeAsync(link, open.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        return new OpenResult(null, 0, $"no table hello and probe answer within {_timings.OpenTimeout.TotalSeconds:0.###} s");
                    }
                    catch (TableProtocolException ex)
                    {
                        await CloseQuietlyAsync(socket, ex.Code, ex.Message).ConfigureAwait(false);
                        return new OpenResult(null, 0, ex.Message);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        return new OpenResult(null, 0, SessionConnection.Describe(ex));
                    }

                    handedOver = true;
                    return new OpenResult(link, 0, null);
                }
            }
            finally
            {
                if (!handedOver)
                {
                    try { socket.Abort(); } catch (Exception) { }
                    socket.Dispose();
                }
            }
        }

        // The status of a refused upgrade. .NET 6 says it in the text of the WebSocketException ("The server returned
        // status code '404' when status code '101' was expected"); .NET Framework wraps a WebException whose response
        // carries it. 0 when neither says.
        internal static int UpgradeStatus(Exception ex)
        {
            for (var inner = ex; inner != null; inner = inner.InnerException)
            {
                if (inner is WebException web && web.Response is HttpWebResponse response)
                {
                    try { return (int)response.StatusCode; }
                    catch (Exception) { }
                }
            }

            for (var inner = ex; inner != null; inner = inner.InnerException)
            {
                var match = StatusInMessage.Match(inner.Message ?? string.Empty);

                if (match.Success)
                    return int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            return 0;
        }

        // The first frame of a table connection: the frame limit, the heartbeat and the block limits the server checks.
        private async Task<Link> ReadHelloAsync(ClientWebSocket socket, string sessionId, CancellationToken token)
        {
            var text = await ReceiveTextAsync(socket, NewReceiveBuffer(), token).ConfigureAwait(false);

            if (text == null)
                throw new InvalidOperationException($"the server closed the table connection before its hello{SessionConnection.DescribeClose(socket)}");

            if (!SessionFrame.TryParse(text, out var hello) || hello.Op != SessionProtocol.OpHello)
                throw new TableProtocolException(WebSocketCloseStatus.ProtocolError, "the first frame of the table connection is not a hello");

            // A frame must take the header and something of a block; the heartbeat rule is the session's.
            if (hello.MaxFrameBytes <= FrameHeaderSize + 64
                || hello.HeartbeatSeconds < SessionProtocol.MinHeartbeatSeconds
                || hello.HeartbeatSeconds > SessionProtocol.MaxHeartbeatSeconds)
                throw new TableProtocolException(WebSocketCloseStatus.ProtocolError, $"the table hello states limits this collector cannot work with (maxFrameBytes {hello.MaxFrameBytes}, heartbeatSeconds {hello.HeartbeatSeconds})");

            TableBlockLimits limits;

            try
            {
                var stated = hello.BlockLimits;

                limits = stated == null
                    ? TableBlockLimits.Default
                    : new TableBlockLimits(stated.MaxDecompressedBytes, stated.MaxTables, stated.MaxRows, stated.MaxColumnsPerTable, stated.MaxTableIdLength);
            }
            catch (ArgumentException ex)
            {
                throw new TableProtocolException(WebSocketCloseStatus.ProtocolError, $"the table hello states block limits this collector cannot work with ({ex.Message})");
            }

            // Kept for the pipeline: the next pack splits by the server's numbers.
            Volatile.Write(ref _maxFrameBytes, hello.MaxFrameBytes);
            Volatile.Write(ref _limits, limits);

            return new Link(socket, sessionId, hello.MaxFrameBytes, TimeSpan.FromSeconds(hello.HeartbeatSeconds));
        }

        // A header without a block (wire contract, "Probe"): binary frames reach the server through whatever is in
        // between, before any block waits on them. Its ack is read here, before the receive loop starts.
        private async Task ProbeAsync(Link link, CancellationToken token)
        {
            var id = Interlocked.Increment(ref _nextId);

            link.NoteSent(id);
            await link.Socket.SendAsync(new ArraySegment<byte>(BuildHeader(KindProbe, id, Guid.Empty, 0)), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);

            var buffer = NewReceiveBuffer();

            while (true)
            {
                var text = await ReceiveTextAsync(link.Socket, buffer, token).ConfigureAwait(false);

                if (text == null)
                    throw new InvalidOperationException($"the server closed the table connection before it answered the probe{SessionConnection.DescribeClose(link.Socket)}");

                if (!SessionFrame.TryParse(text, out var frame))
                    throw new TableProtocolException(WebSocketCloseStatus.InvalidPayloadData, "the server sent a text frame that is not a JSON object");

                if (frame.Op == SessionProtocol.OpPong || frame.Op == SessionProtocol.OpPing)
                    continue;

                if (frame.Ack != id)
                    throw new TableProtocolException(WebSocketCloseStatus.ProtocolError, "the server's first answer on the table connection is not the ack of its probe");

                if (frame.Status != 200)
                    throw new TableProtocolException(WebSocketCloseStatus.ProtocolError, $"the server answered the probe with status {frame.Status}");

                return;
            }
        }

        internal static byte[] BuildHeader(byte kind, long id, Guid producer, long sequence)
        {
            var header = new byte[FrameHeaderSize];

            header[0] = FrameVersion;
            header[1] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(header, 4, 4), FrameHeaderSize);
            BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(header, 8, 8), id);
            producer.ToByteArray().CopyTo(header, 16);
            BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(header, 32, 8), sequence);

            return header;
        }


        // ---- Serving an open connection -------------------------------------------------------------------------------

        private readonly struct LinkEnd
        {
            internal LinkEnd(string why, int? serverClose, bool protocolError, bool sessionEnded)
            {
                Why = why;
                ServerClose = serverClose;
                ProtocolError = protocolError;
                SessionEnded = sessionEnded;
            }

            internal string Why { get; }

            internal int? ServerClose { get; }

            internal bool ProtocolError { get; }

            internal bool SessionEnded { get; }
        }

        private async Task<LinkEnd> ServeAsync(Link link, Task sessionEnded, CancellationToken token)
        {
            Interlocked.Increment(ref _opened);
            _downReason = null;

            lock (_reportLock)
                _lastReport = null;

            lock (_linkGate)
            {
                _link = link;
                _upSignal.TrySetResult(true);
            }

            _logger?.Info($"The table connection is up: frame limit {link.MaxFrameBytes} bytes, heartbeat {link.Heartbeat.TotalSeconds:0} s.");

            string why;
            var sessionWentAway = false;

            using (var serving = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var receive = ReceiveLoopAsync(link, serving.Token);
                var heartbeat = HeartbeatLoopAsync(link, serving.Token);

                var first = await Task.WhenAny(receive, heartbeat, link.Ended, sessionEnded).ConfigureAwait(false);

                if (first == sessionEnded)
                {
                    sessionWentAway = true;
                    why = "its session ended";
                }
                else if (first == link.Ended)
                {
                    why = link.Ended.Result;
                }
                else
                {
                    try { why = await ((Task<string>)first).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { why = "the collector is stopping"; }
                    catch (Exception ex) { why = SessionConnection.Describe(ex); }
                }

                // The link is over for every waiter before anything else happens: a block waiting for its ack learns it
                // now and goes to the next connection.
                link.End(why);
                MarkDown(link);

                // The close is said before the loops are cancelled: cancelling a receive that is under way aborts a
                // ClientWebSocket, and the server would then see a reset where the collector meant a closure (a block
                // without an ack in time, the end of the session).
                await CloseQuietlyAsync(link.Socket, link.CloseCode, link.CloseReason, link.SendLock).ConfigureAwait(false);

                serving.Cancel();
                Observe(receive);
                Observe(heartbeat);
            }

            link.Socket.Dispose();

            return new LinkEnd(why, link.ServerClose, link.ProtocolError, sessionWentAway);
        }

        // Called by the loop when a link ends, and by a put that found its link over (lost ack, failed write) before
        // the loop got to it: the put must not send its block again on the connection it has just given up.
        private void MarkDown(Link link)
        {
            lock (_linkGate)
            {
                if (!ReferenceEquals(_link, link))
                    return;

                // The next waiter waits for the next connection; the signal is renewed before the link is cleared, so
                // a put that sees no link never finds the old, completed signal.
                if (_upSignal.Task.IsCompleted)
                    _upSignal = NewSignal();

                _link = null;
            }
        }

        // Text acks, pings and pongs only. A binary frame, a text that is no frame of the protocol, an ack for a request
        // never sent and a frame larger than an ack can be close the connection as a protocol error (said once, the
        // connection is opened again after a backoff).
        private async Task<string> ReceiveLoopAsync(Link link, CancellationToken token)
        {
            var buffer = NewReceiveBuffer();

            while (true)
            {
                string text;

                try
                {
                    text = await ReceiveTextAsync(link.Socket, buffer, token).ConfigureAwait(false);
                }
                catch (TableProtocolException ex)
                {
                    return link.ProtocolViolation(ex.Code, ex.Message);
                }

                if (text == null)
                {
                    link.ServerClose = (int?)link.Socket.CloseStatus;

                    var why = $"closed by the server{SessionConnection.DescribeClose(link.Socket)}";

                    // The answer to the server's close, echoing its code.
                    link.CloseCode = link.Socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure;
                    link.CloseReason = string.Empty;

                    return why;
                }

                link.NoteReceived();

                if (!SessionFrame.TryParse(text, out var frame))
                    return link.ProtocolViolation(WebSocketCloseStatus.InvalidPayloadData, "the server sent a text frame that is not a JSON object");

                if (frame.Ack.HasValue)
                {
                    if (!link.TryCompleteAck(frame))
                        return link.ProtocolViolation(WebSocketCloseStatus.ProtocolError, $"the server acknowledged request {frame.Ack.Value}, which this collector did not send");

                    continue;
                }

                switch (frame.Op)
                {
                    case SessionProtocol.OpPing:
                        await link.SendTextAsync(SessionProtocol.Pong, _timings.SendTimeout, token).ConfigureAwait(false);
                        break;

                    case SessionProtocol.OpPong:
                        break;

                    case null:
                        return link.ProtocolViolation(WebSocketCloseStatus.ProtocolError, "the server sent a frame that is neither an ack nor a push");

                    // A push this version does not know is ignored, as on the session: that is how version 1 grows.
                }
            }
        }

        // A ping when nothing was sent for a heartbeat; the connection is given up when the server was silent for two and
        // a half, except while a block is being written (a large frame may wait unread on the server for minutes).
        private async Task<string> HeartbeatLoopAsync(Link link, CancellationToken token)
        {
            var interval = link.Heartbeat;
            var silence = TimeSpan.FromTicks(interval.Ticks * 5 / 2);
            var tick = TimeSpan.FromMilliseconds(Math.Max(20, Math.Min(1000, interval.TotalMilliseconds / 4)));

            while (true)
            {
                await Task.Delay(tick, token).ConfigureAwait(false);

                if (!link.IsWritingBlock && link.SinceReceived >= silence)
                    return $"the server sent nothing for two and a half heartbeats ({silence.TotalSeconds:0.#} s)";

                if (link.SinceSent >= interval && await link.SendLock.WaitAsync(0).ConfigureAwait(false))
                {
                    try
                    {
                        await link.SendUnlockedAsync(SessionProtocol.Ping, WebSocketMessageType.Text, _timings.SendTimeout, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        link.SendLock.Release();
                    }
                }
            }
        }


        // ---- Blocks -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Sends one block and returns the server's answer. Waits up to <see cref="SessionTimings.TableConnectionWait"/>
        /// for a table connection; a connection lost while the block waits for its ack is replaced and the same frame
        /// goes again on the next one (a block that was stored is then answered <c>duplicate</c>). Does not throw for a
        /// failure of the transport; throws <see cref="OperationCanceledException"/> when <paramref name="token"/> fires.
        /// </summary>
        internal async Task<TableBlockAck> PutAsync(Guid producer, long sequence, byte[] block, CancellationToken token)
        {
            await _putLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                Want();

                // Set before the first write: a frame written on a connection that was then lost may be stored, and a
                // TooLarge from a later connection must say so, or the pipeline would give this sequence to other rows.
                var written = false;

                for (var connection = 0; connection < ConnectionsPerPut; connection++)
                {
                    token.ThrowIfCancellationRequested();

                    var link = await WaitForLinkAsync(token).ConfigureAwait(false);

                    if (link == null)
                        return TableBlockAck.NotDelivered(_downReason ?? "no table connection");

                    if (block.Length + FrameHeaderSize > link.MaxFrameBytes)
                        return TableBlockAck.TooLarge($"a block of {block.Length + FrameHeaderSize} bytes with its frame header is larger than the table connection takes ({link.MaxFrameBytes} bytes)", written);

                    // The block limits of the hello, from the block's header (nothing is decompressed): a block packed
                    // for larger limits would be refused for good (400), so it is packed again instead.
                    if (ExceedsLimits(block, BlockLimits, out var exceeded))
                        return TableBlockAck.TooLarge($"a block exceeds the block limits of the table connection ({exceeded})", written);

                    written = true;

                    var answer = await SendBlockAsync(link, producer, sequence, block, token).ConfigureAwait(false);

                    if (answer != null)
                        return answer;
                }

                return TableBlockAck.NotDelivered($"the table connection was lost {ConnectionsPerPut} times while the block waited for its ack ({_downReason})");
            }
            finally
            {
                _putLock.Release();
            }
        }

        private static bool ExceedsLimits(byte[] block, TableBlockLimits limits, out string why)
        {
            try
            {
                var header = TableBlockReader.ReadHeader(block, ReadBackLimits);

                why = header.RowCount > limits.MaxRows ? $"{header.RowCount} rows, at most {limits.MaxRows}"
                    : header.TableCount > limits.MaxTables ? $"{header.TableCount} tables, at most {limits.MaxTables}"
                    : header.DecompressedLength > limits.MaxDecompressedBytes ? $"{header.DecompressedLength} bytes decompressed, at most {limits.MaxDecompressedBytes}"
                    : null;

                return why != null;
            }
            catch (TableBlockFormatException)
            {
                // Not this collector's to judge: the server answers it.
                why = null;
                return false;
            }
        }

        private static readonly TableBlockLimits ReadBackLimits = new TableBlockLimits(TableBlockLimits.FormatMaxDecompressedBytes, int.MaxValue, int.MaxValue,
                                                                                         TableBlockLimits.FormatMaxColumnsPerTable, TableBlockLimits.FormatMaxTableIdLength);

        private async Task<Link> WaitForLinkAsync(CancellationToken token)
        {
            var link = _link;

            if (link != null && !link.Ended.IsCompleted)
                return link;

            // Over, and the loop has not cleared it yet: the next connection is the one to wait for.
            if (link != null)
                MarkDown(link);

            var run = _current;

            if (run == null || run.Stopping)
                return null;

            await WaitAsync(_upSignal.Task, token, _timings.TableConnectionWait).ConfigureAwait(false);

            link = _link;

            return link != null && !link.Ended.IsCompleted ? link : null;
        }

        // One block on one connection. Null when the connection was lost before an answer: the caller sends the same
        // frame on the next one.
        private async Task<TableBlockAck> SendBlockAsync(Link link, Guid producer, long sequence, byte[] block, CancellationToken token)
        {
            var id = Interlocked.Increment(ref _nextId);
            var waiter = link.Expect(id);
            var header = BuildHeader(KindBlock, id, producer, sequence);

            try
            {
                await link.SendLock.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    link.BeginBlock();

                    // The header and the block as two fragments of one message: the block is not copied. A large frame may
                    // wait on the server for its memory budget (it closes with 1013 past its bound), so the write is
                    // bounded by ten heartbeats, not by the send timeout of a small frame.
                    var bound = TimeSpan.FromTicks(Math.Max(_timings.SendTimeout.Ticks, link.Heartbeat.Ticks * 10));

                    await link.SendUnlockedAsync(header, WebSocketMessageType.Binary, bound, token, endOfMessage: false).ConfigureAwait(false);
                    await link.SendUnlockedAsync(block, WebSocketMessageType.Binary, bound, token, endOfMessage: true).ConfigureAwait(false);
                }
                finally
                {
                    link.EndBlock();
                    link.SendLock.Release();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A write cancelled half way breaks the frame: the connection cannot be used any more.
                link.Abort("a block write was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                link.End($"writing a block failed: {SessionConnection.Describe(ex)}");
                return null;
            }

            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var deadline = AckDeadlineForTests?.Invoke(id) ?? Task.Delay(_requestTimeout, wait.Token);
                var cancelled = Task.Delay(Timeout.Infinite, wait.Token);
                var first = await Task.WhenAny(waiter, link.Ended, deadline, cancelled).ConfigureAwait(false);

                wait.Cancel();

                // An answer that is there is taken, whatever else ended at the same moment (the link, the wait, the
                // caller's token): a block the server answered is never sent again for nothing.
                if (first == waiter || waiter.Status == TaskStatus.RanToCompletion)
                    return Answer(await waiter.ConfigureAwait(false), link);
            }

            token.ThrowIfCancellationRequested();

            if (link.Ended.IsCompleted)
                return null;

            // No ack in time: the connection is closed, so a late ack cannot be taken for the next block's, and the same
            // frame goes on the next connection.
            link.End($"a block got no ack within {_requestTimeout.TotalSeconds:0.###} s");
            return null;
        }

        // The ack of a block as the pipeline's answer. For each status: can a repeat cure it? A refusal that a repeat
        // cannot cure is final; a fault is never final.
        private TableBlockAck Answer(SessionFrame ack, Link link)
        {
            var status = ack.Status;

            if (status >= 200 && status < 300)
                return ReadOutcome(ack);

            var retryAfter = ack.RetryAfter.HasValue && ack.RetryAfter.Value > 0 ? TimeSpan.FromSeconds(ack.RetryAfter.Value) : (TimeSpan?)null;
            var words = string.IsNullOrEmpty(ack.Error) ? $"status {status}" : $"status {status}: {ack.Error}";

            // A request that waited too long, or a rate limit: a wait cures it.
            if (status == 408 || status == 429)
                return TableBlockAck.NotDelivered($"the server answered a block with {words}", retryAfter);

            // 400 and every other refusal of the request: the same frame would be refused again.
            if (status >= 400 && status < 500)
                return TableBlockAck.Invalid(words);

            // 500, 503 and every other fault of the server: nothing was stored, the same frame goes again.
            if (status >= 500 && status < 600)
                return TableBlockAck.NotDelivered($"the server answered a block with {words}", retryAfter);

            // A status no version of the protocol sends: the connection is not trusted any more.
            link.ProtocolViolation(WebSocketCloseStatus.ProtocolError, $"the server answered a block with status {status}");
            return TableBlockAck.NotDelivered($"the server answered a block with status {status}");
        }

        private TableBlockAck ReadOutcome(SessionFrame ack)
        {
            string outcome = null;
            var refused = new List<TableRefusal>();
            var refusedCount = 0;

            try
            {
                if (!string.IsNullOrEmpty(ack.Body))
                    using (var document = JsonDocument.Parse(ack.Body))
                    {
                        var body = document.RootElement;

                        if (body.ValueKind == JsonValueKind.Object)
                        {
                            if (body.TryGetProperty("outcome", out var element) && element.ValueKind == JsonValueKind.String)
                                outcome = element.GetString();

                            if (body.TryGetProperty("refusedCount", out element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var count))
                                refusedCount = count;

                            if (body.TryGetProperty("refused", out element) && element.ValueKind == JsonValueKind.Array)
                                foreach (var entry in element.EnumerateArray())
                                {
                                    if (entry.ValueKind != JsonValueKind.Object)
                                        continue;

                                    var index = entry.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var parsed) ? parsed : -1;
                                    var table = entry.TryGetProperty("table", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                                    var reason = entry.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;

                                    refused.Add(new TableRefusal(index, table, reason ?? "unknown"));
                                }
                        }
                    }
            }
            catch (JsonException)
            {
                // A 200 settles the block whatever its body says; the refusals it lists are then unknown.
                _logger?.Error("The body of a table block ack is not JSON; the block is taken as stored.");
            }

            switch (outcome)
            {
                case "stored":
                    return TableBlockAck.Stored(refused, refusedCount);

                case "duplicate":
                    // "refusedKnown": false carries no list: the rows were settled by the first put either way.
                    return TableBlockAck.Duplicate(refused, refusedCount);

                case "allRefused":
                    return TableBlockAck.AllRefused(refused, refusedCount);

                default:
                    // A 200 settles the block (wire contract); an outcome this version does not know is said and taken
                    // as stored, so the block does not hold the ones behind it for ever.
                    Report($"The server answered a table block with an outcome this collector does not know ({outcome ?? "none"}); the block is taken as stored.");
                    return TableBlockAck.Stored(refused, refusedCount);
            }
        }


        // ---- Frames -------------------------------------------------------------------------------------------------

        private static ArraySegment<byte> NewReceiveBuffer() => new ArraySegment<byte>(new byte[16 * 1024]);

        // One whole text message, or null when the server closed. A binary frame, or a text larger than an ack can be,
        // is a protocol error: the bound is what a broken or hostile server can make the collector allocate.
        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, ArraySegment<byte> buffer, CancellationToken token)
        {
            MemoryStream message = null;

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return null;

                    if (result.MessageType != WebSocketMessageType.Text)
                        throw new TableProtocolException(WebSocketCloseStatus.InvalidMessageType, "the server sent a binary frame on the table connection");

                    if (result.EndOfMessage && message == null)
                        return Encoding.UTF8.GetString(buffer.Array, buffer.Offset, result.Count);

                    if (message == null)
                        message = new MemoryStream();

                    if (message.Length + result.Count > SessionProtocol.MaxTableInboundFrameBytes)
                        throw new TableProtocolException(WebSocketCloseStatus.MessageTooBig, $"the server sent a text frame larger than {SessionProtocol.MaxTableInboundFrameBytes} bytes on the table connection");

                    message.Write(buffer.Array, buffer.Offset, result.Count);

                    if (result.EndOfMessage)
                        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
            finally
            {
                message?.Dispose();
            }
        }

        private static async Task CloseOutputAsync(Link link, WebSocketCloseStatus code, string reason, CancellationToken token)
        {
            await link.SendLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                if (link.Socket.State == WebSocketState.Open || link.Socket.State == WebSocketState.CloseReceived)
                    await link.Socket.CloseOutputAsync(code, reason, token).ConfigureAwait(false);
            }
            finally
            {
                link.SendLock.Release();
            }
        }

        // Says the close (ours, or the answer to the server's) for at most a second, then lets the socket go.
        private static async Task CloseQuietlyAsync(ClientWebSocket socket, WebSocketCloseStatus code, string reason, SemaphoreSlim sendLock = null)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    using (var timeout = new CancellationTokenSource(CloseAnswerTimeout))
                    {
                        if (sendLock != null && !await sendLock.WaitAsync(CloseAnswerTimeout).ConfigureAwait(false))
                            sendLock = null;
                        else
                        {
                            try
                            {
                                await socket.CloseOutputAsync(code, Truncate(reason), timeout.Token).ConfigureAwait(false);
                            }
                            finally
                            {
                                sendLock?.Release();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            try { socket.Abort(); }
            catch (Exception) { }
        }

        // A close reason is at most 123 bytes.
        private static string Truncate(string reason) => string.IsNullOrEmpty(reason) ? string.Empty : reason.Length <= 100 ? reason : reason.Substring(0, 100);

        private static async Task WaitAsync(Task task, CancellationToken token, TimeSpan? timeout = null)
        {
            if (task.IsCompleted)
                return;

            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(timeout ?? Timeout.InfiniteTimeSpan, wait.Token);

                await Task.WhenAny(task, delay).ConfigureAwait(false);
                wait.Cancel();
            }

            token.ThrowIfCancellationRequested();
        }

        private static void Observe(Task task) =>
            task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);


        /// <summary>A frame of the server that this collector cannot take: the connection is closed with <see cref="Code"/>.</summary>
        private sealed class TableProtocolException : Exception
        {
            internal TableProtocolException(WebSocketCloseStatus code, string message) : base(message)
            {
                Code = code;
            }

            internal WebSocketCloseStatus Code { get; }
        }


        /// <summary>One open table connection: its socket, its limits, the block waiting for its ack, and its end.</summary>
        private sealed class Link
        {
            private readonly TaskCompletionSource<string> _ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _ackLock = new object();
            private long _pendingId;
            private TaskCompletionSource<SessionFrame> _pendingAck;
            private long _highestSent;
            private long _lastSent;
            private long _lastReceived;
            private int _writingBlock;

            internal Link(ClientWebSocket socket, string sessionId, int maxFrameBytes, TimeSpan heartbeat)
            {
                Socket = socket;
                SessionId = sessionId;
                MaxFrameBytes = maxFrameBytes;
                Heartbeat = heartbeat;
                NoteSentNow();
                NoteReceived();
            }

            internal ClientWebSocket Socket { get; }

            internal string SessionId { get; }

            internal int MaxFrameBytes { get; }

            internal TimeSpan Heartbeat { get; }

            internal SemaphoreSlim SendLock { get; } = new SemaphoreSlim(1, 1);

            /// <summary>Completes, with the reason, when the connection is over for whatever cause.</summary>
            internal Task<string> Ended => _ended.Task;

            /// <summary>The close code the server sent; null when it sent none.</summary>
            internal int? ServerClose { get; set; }

            internal bool ProtocolError { get; private set; }

            /// <summary>The close the collector says when the link ends (its own, or the answer to the server's).</summary>
            internal WebSocketCloseStatus CloseCode { get; set; } = WebSocketCloseStatus.NormalClosure;

            internal string CloseReason { get; set; } = string.Empty;

            internal bool IsWritingBlock => Volatile.Read(ref _writingBlock) == 1;

            internal TimeSpan SinceSent => Since(ref _lastSent);

            internal TimeSpan SinceReceived => Since(ref _lastReceived);

            internal void End(string why) => _ended.TrySetResult(why);

            internal void Abort(string why)
            {
                End(why);

                try { Socket.Abort(); }
                catch (Exception) { }
            }

            internal string ProtocolViolation(WebSocketCloseStatus code, string why)
            {
                ProtocolError = true;
                CloseCode = code;
                CloseReason = why;
                End(why);
                return why;
            }

            internal void NoteSent(long id)
            {
                lock (_ackLock)
                    if (id > _highestSent)
                        _highestSent = id;
            }

            /// <summary>The ack of request <paramref name="id"/> is waited for from now on.</summary>
            internal Task<SessionFrame> Expect(long id)
            {
                var waiter = new TaskCompletionSource<SessionFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

                lock (_ackLock)
                {
                    _pendingId = id;
                    _pendingAck = waiter;

                    if (id > _highestSent)
                        _highestSent = id;
                }

                return waiter.Task;
            }

            /// <summary>
            /// Hands an ack to the block that waits for it. An ack of a request this connection sent and no longer waits
            /// for (its wait was cancelled) is dropped; an ack of a request never sent is false: a protocol error.
            /// </summary>
            internal bool TryCompleteAck(SessionFrame frame)
            {
                var id = frame.Ack.Value;

                lock (_ackLock)
                {
                    if (id == _pendingId && _pendingAck != null)
                    {
                        _pendingAck.TrySetResult(frame);
                        _pendingAck = null;
                        return true;
                    }

                    return id > 0 && id <= _highestSent;
                }
            }

            internal void BeginBlock() => Volatile.Write(ref _writingBlock, 1);

            // The silence clock starts again when a block is written: the time it waited unread on the server is not
            // the server's silence.
            internal void EndBlock()
            {
                Volatile.Write(ref _writingBlock, 0);
                NoteSentNow();
                NoteReceived();
            }

            internal void NoteReceived() => Volatile.Write(ref _lastReceived, Stopwatch.GetTimestamp());

            private void NoteSentNow() => Volatile.Write(ref _lastSent, Stopwatch.GetTimestamp());

            internal async Task SendTextAsync(byte[] frame, TimeSpan bound, CancellationToken token)
            {
                await SendLock.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    await SendUnlockedAsync(frame, WebSocketMessageType.Text, bound, token).ConfigureAwait(false);
                }
                finally
                {
                    SendLock.Release();
                }
            }

            // Under SendLock. A write that cannot finish within `bound` breaks the connection.
            internal async Task SendUnlockedAsync(byte[] data, WebSocketMessageType type, TimeSpan bound, CancellationToken token, bool endOfMessage = true)
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(bound);

                    try
                    {
                        await Socket.SendAsync(new ArraySegment<byte>(data), type, endOfMessage, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new TimeoutException($"a frame could not be written within {bound.TotalSeconds:0.###} s");
                    }
                }

                NoteSentNow();
            }

            private static TimeSpan Since(ref long ticks) =>
                TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - Volatile.Read(ref ticks)) / (double)Stopwatch.Frequency);
        }
    }
}
