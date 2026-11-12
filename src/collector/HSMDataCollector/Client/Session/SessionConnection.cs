using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Logging;

namespace HSMDataCollector.Client.Session
{
    /// <summary>
    /// How long the session waits for things. The defaults are the protocol's; tests shorten them.
    /// </summary>
    internal sealed class SessionTimings
    {
        /// <summary>One attempt to open a session: connect, TLS, upgrade and the hello frame.</summary>
        internal TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>The first pause before a new attempt; it doubles up to <see cref="MaxBackoff"/>.</summary>
        internal TimeSpan MinBackoff { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The ceiling of the pause. Well under the 5 minutes after which a silent collector is
        /// alerted (#1666): a session that can be reopened is reopened long before that.
        /// </summary>
        internal TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>A session that lasted this long starts the backoff over when it ends.</summary>
        internal TimeSpan StableSession { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>A frame that cannot be written in this time ends the session.</summary>
        internal TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }


    /// <summary>
    /// One collector session: a WebSocket to <c>GET /api/sensors/session</c> that is opened in
    /// the background, kept alive by a heartbeat and reopened with a jittered backoff when it
    /// ends. Requests are frames answered by acks.
    ///
    /// There is no other path: while no session is up a request fails at once, and the caller
    /// keeps its data and tries again, as it does when the server is unreachable over HTTP.
    /// Nothing here ever sends an HTTP request.
    /// </summary>
    internal sealed class SessionConnection : IDisposable
    {
        private const string NoSession = "no collector session";

        private readonly ConcurrentDictionary<long, TaskCompletionSource<SessionAck>> _pending =
            new ConcurrentDictionary<long, TaskCompletionSource<SessionAck>>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly object _lifecycle = new object();
        private readonly Random _jitter = new Random();

        private readonly Uri _url;
        private readonly IReadOnlyList<KeyValuePair<string, string>> _headers;
        private readonly bool _allowUntrustedCertificate;
        private readonly ICollectorLogger _logger;
        private readonly Action<string> _reportError;
        private readonly ServerDirectives _directives;
        private readonly SessionTimings _timings;

        // One run per Start. A loop that is slow to notice its stop (a connect that does not
        // honour cancellation) must neither keep the next Start from running nor write the state
        // of the run that replaced it.
        private sealed class Run
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal volatile bool Stopping;
            internal Task Loop;
        }

        private Run _current;

        // The session being served, and what its hello stated. Written by the loop, read by senders.
        private volatile ClientWebSocket _socket;
        private volatile TaskCompletionSource<string> _reset;
        private volatile TaskCompletionSource<bool> _upSignal = NewSignal();
        private volatile string _downReason = NoSession;
        private int _maxFrameBytes;
        private long _heartbeatTicks;
        private long _lastSentTicks;
        private long _lastReceivedTicks;
        private long _nextId;
        private long _sessionsOpened;

        internal SessionConnection(
            Uri url,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            bool allowUntrustedCertificate,
            ICollectorLogger logger,
            Action<string> reportError,
            ServerDirectives directives,
            SessionTimings timings)
        {
            _url = url;
            _headers = headers;
            _allowUntrustedCertificate = allowUntrustedCertificate;
            _logger = logger;
            _reportError = reportError;
            _directives = directives;
            _timings = timings ?? new SessionTimings();
        }

        /// <summary>True while a session is up: the hello arrived and nothing has ended it.</summary>
        internal bool IsUp => _socket != null;

        /// <summary>
        /// The largest body a request may carry on the current session: the server's frame limit
        /// less the envelope. 0 while no session is up.
        /// </summary>
        internal int MaxBodyBytes => IsUp ? Math.Max(0, Volatile.Read(ref _maxFrameBytes) - SessionProtocol.EnvelopeRoom) : 0;

        /// <summary>Why there is no session; for the text of a request that could not be sent.</summary>
        internal string DownReason => _downReason;

        /// <summary>How many sessions were opened since the collector started this connection.</summary>
        internal long SessionsOpened => Interlocked.Read(ref _sessionsOpened);


        /// <summary>Completes when a session is up; at once when one is up now.</summary>
        internal async Task WaitUntilUpAsync(CancellationToken token)
        {
            var up = _upSignal.Task;

            if (up.IsCompleted)
                return;

            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                await Task.WhenAny(up, Task.Delay(System.Threading.Timeout.Infinite, wait.Token)).ConfigureAwait(false);
                wait.Cancel();
            }

            token.ThrowIfCancellationRequested();
        }

        private static TaskCompletionSource<bool> NewSignal() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Starts connecting in the background and returns at once.</summary>
        internal void Start()
        {
            lock (_lifecycle)
            {
                var running = _current;

                if (running != null && !running.Stopping && !running.Loop.IsCompleted)
                    return;

                var run = new Run();

                _downReason = "connecting";
                _current = run;
                run.Loop = Task.Run(() => RunAsync(run));
            }
        }

        private bool IsCurrent(Run run) => ReferenceEquals(Volatile.Read(ref _current), run);

        /// <summary>
        /// Ends the session and the loop. With a session up and a positive
        /// <paramref name="linger"/>, the close frame is sent and the server's close is awaited
        /// for that long, so the server sees a normal closure and not a reset. Bounded and
        /// idempotent.
        /// </summary>
        internal async Task StopAsync(TimeSpan linger)
        {
            Run run;

            lock (_lifecycle)
            {
                run = _current;

                if (run == null)
                    return;

                run.Stopping = true;
            }

            var loop = run.Loop;

            var socket = _socket;

            if (socket != null && linger > TimeSpan.Zero)
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(linger))
                    {
                        await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);

                        try
                        {
                            // Only our half: the loop is reading, and it is the loop that sees the
                            // server's close and ends the session.
                            if (socket.State == WebSocketState.Open)
                                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "collector stopped", timeout.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
                    }

                    await Task.WhenAny(loop, Task.Delay(linger)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A close that cannot be said politely is said by the abort below.
                }
            }

            try { run.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }

            try { _socket?.Abort(); }
            catch (Exception) { }

            // The loop ends on the cancellation; a second is ample for it to notice.
            await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        }

        public void Dispose()
        {
            try { StopAsync(TimeSpan.Zero).GetAwaiter().GetResult(); }
            catch (Exception) { }
        }

        /// <summary>
        /// Makes every request that waits for an ack fail now. The session stays up and later
        /// requests go through: this is what a stop does to sends it must not wait for.
        /// </summary>
        internal void CancelPendingRequests() => FailAllPending("cancelled");


        /// <summary>
        /// Sends {"id":..,"op":op,"body":body} and waits up to <paramref name="timeout"/> for its
        /// ack. Thread-safe; several requests may wait at once. A wait that times out ends the
        /// session: a late ack then cannot be taken for another request's, and the session is
        /// opened again. Cancelling <paramref name="token"/> throws.
        /// </summary>
        internal async Task<SessionAck> RequestAsync(string op, string body, TimeSpan timeout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var socket = _socket;

            if (socket == null)
                return SessionAck.NoAck(_downReason);

            var id = Interlocked.Increment(ref _nextId);
            var waiter = new TaskCompletionSource<SessionAck>(TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[id] = waiter;

            try
            {
                // The session may have ended between the read above and the registration: the
                // loop fails only the requests it finds, so look again.
                if (!ReferenceEquals(_socket, socket))
                    return SessionAck.NoAck(_downReason);

                try
                {
                    await SendAsync(socket, SessionProtocol.BuildRequest(id, op, body), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RequestReset($"sending a frame failed: {ex.Message}");
                    return SessionAck.NoAck($"sending failed: {ex.Message}");
                }

                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var deadline = Task.Delay(timeout, wait.Token);

                    if (await Task.WhenAny(waiter.Task, deadline).ConfigureAwait(false) == waiter.Task)
                    {
                        wait.Cancel();
                        return await waiter.Task.ConfigureAwait(false);
                    }
                }

                token.ThrowIfCancellationRequested();

                // Worded without the op: the same cause for every queue is one deduplicated report.
                RequestReset($"a request got no ack within {timeout.TotalSeconds:0.###} s");

                return SessionAck.NoAck("no ack in time");
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }


        private async Task RunAsync(Run run)
        {
            var token = run.Cancellation.Token;
            var attempt = 0;

            while (!token.IsCancellationRequested)
            {
                var lived = Stopwatch.StartNew();
                var wasUp = false;
                string why;

                var socket = new ClientWebSocket();

                try
                {
                    Configure(socket);

                    using (var open = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        open.CancelAfter(_timings.OpenTimeout);

                        try
                        {
                            await socket.ConnectAsync(_url, open.Token).ConfigureAwait(false);

                            if (!string.Equals(socket.SubProtocol, SessionProtocol.Subprotocol, StringComparison.Ordinal))
                                throw new InvalidOperationException($"the server did not select the {SessionProtocol.Subprotocol} subprotocol");

                            var hello = await ReadHelloAsync(socket, open.Token).ConfigureAwait(false);

                            Volatile.Write(ref _maxFrameBytes, hello.MaxFrameBytes);
                            Volatile.Write(ref _heartbeatTicks, TimeSpan.FromSeconds(hello.HeartbeatSeconds).Ticks);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            throw new TimeoutException($"no session within {_timings.OpenTimeout.TotalSeconds:0.###} s");
                        }
                    }

                    wasUp = true;
                    lived.Restart();
                    MarkUp(socket);

                    why = await ServeAsync(socket, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    why = null;
                }
                catch (Exception ex)
                {
                    why = Describe(ex);
                }
                finally
                {
                    MarkDown(socket);
                }

                // A run that was replaced by a new Start only leaves: the state is the new run's.
                if (!IsCurrent(run))
                    break;

                if (token.IsCancellationRequested || run.Stopping)
                {
                    _downReason = "the collector is stopping";
                    FailAllPending(_downReason);
                    break;
                }

                _downReason = wasUp ? $"the session ended: {why}" : $"the session cannot be opened: {why}";
                FailAllPending(_downReason);

                if (wasUp)
                {
                    _logger?.Info($"The collector session ended after {lived.Elapsed.TotalSeconds:0.#} s: {why}. Reconnecting.");

                    if (lived.Elapsed >= _timings.StableSession)
                    {
                        attempt = 0;
                    }
                    else
                    {
                        // A session that opens and ends at once, again and again, delivers nothing
                        // and looks healthy at Info level (a server that takes the upgrade and
                        // never acknowledges, or closes right after its hello). Said as an error,
                        // deduplicated: the cause repeats with the same words.
                        _reportError?.Invoke($"The collector session to {_url} ended early: {why}. Values stay queued; reconnecting.");
                    }
                }
                else
                {
                    // Through the deduplicated channel: an upgrade the server refuses is refused
                    // again on every attempt, with the same words.
                    _reportError?.Invoke($"Cannot open a collector session to {_url}: {why}. Values stay queued; retrying.");
                }

                try
                {
                    await Task.Delay(NextPause(attempt++), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            run.Cancellation.Dispose();
        }

        private void Configure(ClientWebSocket socket)
        {
            socket.Options.AddSubProtocol(SessionProtocol.Subprotocol);

            foreach (var header in _headers)
                socket.Options.SetRequestHeader(header.Key, header.Value);

#if NET6_0_OR_GREATER
            if (_allowUntrustedCertificate)
                socket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
#endif
        }

        /// <summary>
        /// Opens a session, reads its hello and closes it again: the answer to "can this
        /// collector reach the server" for a collector whose transport is the session. It does
        /// not touch the session being served, if there is one.
        /// </summary>
        internal async Task<Core.ConnectionResult> ProbeAsync()
        {
            using (var socket = new ClientWebSocket())
            using (var timeout = new CancellationTokenSource(_timings.OpenTimeout))
            {
                try
                {
                    Configure(socket);

                    await socket.ConnectAsync(_url, timeout.Token).ConfigureAwait(false);

                    if (!string.Equals(socket.SubProtocol, SessionProtocol.Subprotocol, StringComparison.Ordinal))
                        return new Core.ConnectionResult(null, $"the server did not select the {SessionProtocol.Subprotocol} subprotocol");

                    await ReadHelloAsync(socket, timeout.Token).ConfigureAwait(false);

                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "connection test", timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // The question is answered; how the test session ends does not change it.
                    }

                    return Core.ConnectionResult.Ok;
                }
                catch (Exception ex)
                {
                    return new Core.ConnectionResult(null, Describe(ex));
                }
            }
        }

        // The first frame of a session: the limits the collector has to respect.
        private static async Task<SessionFrame> ReadHelloAsync(ClientWebSocket socket, CancellationToken token)
        {
            var text = await ReceiveAsync(socket, NewReceiveBuffer(), token).ConfigureAwait(false);

            if (text == null)
                throw new InvalidOperationException($"the server closed the session before its hello{DescribeClose(socket)}");

            if (!SessionFrame.TryParse(text, out var hello) || hello.Op != SessionProtocol.OpHello)
                throw new InvalidOperationException("the first frame is not a hello");

            if (hello.MaxFrameBytes < SessionProtocol.MinFrameBytes
                || hello.HeartbeatSeconds < SessionProtocol.MinHeartbeatSeconds
                || hello.HeartbeatSeconds > SessionProtocol.MaxHeartbeatSeconds)
                throw new InvalidOperationException($"the hello states limits this collector cannot work with (maxFrameBytes {hello.MaxFrameBytes}, heartbeatSeconds {hello.HeartbeatSeconds})");

            return hello;
        }

        private void MarkUp(ClientWebSocket socket)
        {
            var now = Stopwatch.GetTimestamp();

            Volatile.Write(ref _lastSentTicks, now);
            Volatile.Write(ref _lastReceivedTicks, now);

            _reset = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _socket = socket;

            Interlocked.Increment(ref _sessionsOpened);
            _upSignal.TrySetResult(true);

            _logger?.Info($"The collector session is up: {_url}, frame limit {Volatile.Read(ref _maxFrameBytes)} bytes, heartbeat {TimeSpan.FromTicks(Volatile.Read(ref _heartbeatTicks)).TotalSeconds:0} s.");
        }

        private void MarkDown(ClientWebSocket socket)
        {
            if (ReferenceEquals(_socket, socket))
            {
                _socket = null;

                // The next waiter waits for the next session.
                if (_upSignal.Task.IsCompleted)
                    _upSignal = NewSignal();
            }

            try { socket.Abort(); }
            catch (Exception) { }

            socket.Dispose();
        }

        // Serves one open session until it ends, and says why.
        private async Task<string> ServeAsync(ClientWebSocket socket, CancellationToken token)
        {
            using (var session = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var receive = ReceiveLoopAsync(socket, session.Token);
                var heartbeat = HeartbeatLoopAsync(socket, session.Token);
                var reset = _reset.Task;

                var first = await Task.WhenAny(receive, heartbeat, reset).ConfigureAwait(false);

                session.Cancel();

                // The other loop ends on the cancellation; what it throws on the way is not news.
                // Observed before anything below can throw: a stop leaves through the catch.
                Observe(receive);
                Observe(heartbeat);

                string why;

                try
                {
                    why = await first.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    why = Describe(ex);
                }

                token.ThrowIfCancellationRequested();

                return why;
            }
        }

        private async Task<string> ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = NewReceiveBuffer();

            while (true)
            {
                var text = await ReceiveAsync(socket, buffer, token).ConfigureAwait(false);

                if (text == null)
                {
                    var why = $"closed by the server{DescribeClose(socket)}";

                    await AnswerCloseAsync(socket).ConfigureAwait(false);

                    return why;
                }

                Volatile.Write(ref _lastReceivedTicks, Stopwatch.GetTimestamp());

                if (!SessionFrame.TryParse(text, out var frame))
                    return "the server sent a frame that is not a JSON object";

                if (frame.Ack.HasValue)
                {
                    // An ack nobody waits for any more (its request gave up) is dropped.
                    if (_pending.TryRemove(frame.Ack.Value, out var waiter))
                        waiter.TrySetResult(SessionAck.Answered(frame.Status, frame.Error, frame.Body));

                    continue;
                }

                switch (frame.Op)
                {
                    case SessionProtocol.OpPing:
                        await SendAsync(socket, SessionProtocol.Pong, token).ConfigureAwait(false);
                        break;

                    case SessionProtocol.OpDirective:
                        _directives?.Receive(frame.Body);
                        break;

                    // A pong only has to arrive: it was counted above as a sign of life. An op
                    // this version does not know is ignored, which is what lets version 1 grow.
                }
            }
        }

        // The server closed the session and waits for the close that answers it. ClientWebSocket
        // sends none by itself, and a session that is dropped instead reads on the server as a
        // connection that broke after its close. Best effort and short: the session is over
        // either way.
        private async Task AnswerCloseAsync(ClientWebSocket socket)
        {
            if (socket.State != WebSocketState.CloseReceived)
                return;

            if (!await _sendLock.WaitAsync(CloseAnswerTimeout).ConfigureAwait(false))
                return;

            try
            {
                using (var timeout = new CancellationTokenSource(CloseAnswerTimeout))
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private static readonly TimeSpan CloseAnswerTimeout = TimeSpan.FromSeconds(1);

        // Says something when nothing was sent for a whole interval, and gives the session up
        // when the server has been silent for two and a half.
        private async Task<string> HeartbeatLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var interval = TimeSpan.FromTicks(Volatile.Read(ref _heartbeatTicks));
            var silence = TimeSpan.FromTicks(interval.Ticks * 5 / 2);
            var tick = TimeSpan.FromMilliseconds(Math.Max(20, Math.Min(1000, interval.TotalMilliseconds / 4)));

            while (true)
            {
                await Task.Delay(tick, token).ConfigureAwait(false);

                if (Elapsed(ref _lastReceivedTicks) >= silence)
                    return $"the server sent nothing for two and a half heartbeats ({silence.TotalSeconds:0.#} s)";

                if (Elapsed(ref _lastSentTicks) >= interval)
                    await SendAsync(socket, SessionProtocol.Ping, token).ConfigureAwait(false);
            }
        }

        // The caller's token decides whether the frame is started, not whether a write that has
        // begun is finished: cancelling a WebSocket write aborts the whole connection, and a stop
        // that cancels a queue in the middle of a frame would take away the session its own drain
        // is about to use. A write that has begun is bounded by the send timeout alone.
        private async Task SendAsync(ClientWebSocket socket, byte[] frame, CancellationToken token)
        {
            await _sendLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                token.ThrowIfCancellationRequested();

                using (var timeout = new CancellationTokenSource(_timings.SendTimeout))
                {
                    try
                    {
                        await socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException($"a frame could not be written within {_timings.SendTimeout.TotalSeconds:0.###} s");
                    }
                }

                Volatile.Write(ref _lastSentTicks, Stopwatch.GetTimestamp());
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // One whole text message, or null when the server closed. A message is assembled from
        // its fragments by ClientWebSocket's own EndOfMessage; this only bounds its size.
        // `buffer` is the caller's, one per session: an ack or a pong is a few dozen bytes and
        // arrives in one read, so the common case allocates only its text.
        private static async Task<string> ReceiveAsync(ClientWebSocket socket, ArraySegment<byte> buffer, CancellationToken token)
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
                        throw new InvalidOperationException("the server sent a binary frame");

                    if (result.EndOfMessage && message == null)
                        return Encoding.UTF8.GetString(buffer.Array, buffer.Offset, result.Count);

                    if (message == null)
                        message = new MemoryStream();

                    message.Write(buffer.Array, buffer.Offset, result.Count);

                    if (message.Length > SessionProtocol.MaxInboundFrameBytes)
                        throw new InvalidOperationException($"the server sent a frame larger than {SessionProtocol.MaxInboundFrameBytes} bytes");

                    if (result.EndOfMessage)
                        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
            finally
            {
                message?.Dispose();
            }
        }

        private static ArraySegment<byte> NewReceiveBuffer() => new ArraySegment<byte>(new byte[16 * 1024]);

        private void RequestReset(string why) => _reset?.TrySetResult(why);

        private void FailAllPending(string why)
        {
            foreach (var id in _pending.Keys)
                if (_pending.TryRemove(id, out var waiter))
                    waiter.TrySetResult(SessionAck.NoAck(why));
        }

        // Exponential with jitter: between half of the step and the whole step, so a fleet that
        // lost its server together does not come back together.
        private TimeSpan NextPause(int attempt)
        {
            var step = _timings.MinBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 20));
            step = Math.Min(step, _timings.MaxBackoff.TotalMilliseconds);

            double share;
            lock (_jitter)
                share = 0.5 + _jitter.NextDouble() / 2;

            return TimeSpan.FromMilliseconds(Math.Max(1, step * share));
        }

        private static TimeSpan Elapsed(ref long sinceTicks) =>
            TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - Volatile.Read(ref sinceTicks)) / (double)Stopwatch.Frequency);

        private static string DescribeClose(ClientWebSocket socket)
        {
            try
            {
                var status = socket.CloseStatus;

                if (!status.HasValue)
                    return string.Empty;

                return string.IsNullOrEmpty(socket.CloseStatusDescription)
                    ? $" ({(int)status.Value})"
                    : $" ({(int)status.Value} {socket.CloseStatusDescription})";
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // The cause in one line. ClientWebSocket reports a refused upgrade as an exception whose
        // text, or whose inner exception's text, names the status the server answered with.
        private static string Describe(Exception ex)
        {
            var text = ex.Message;

            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                if (!string.IsNullOrEmpty(inner.Message) && text.IndexOf(inner.Message, StringComparison.Ordinal) < 0)
                    text += " " + inner.Message;

            return text.Replace('\r', ' ').Replace('\n', ' ').TrimEnd('.', ' ');
        }

        private static void Observe(Task task) =>
            task.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
