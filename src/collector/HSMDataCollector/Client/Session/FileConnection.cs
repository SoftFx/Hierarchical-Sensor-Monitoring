using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.LargeFiles;
using HSMDataCollector.Logging;

namespace HSMDataCollector.Client.Session
{
    /// <summary>The <c>fileLimits</c> and the rest of a file connection's hello.</summary>
    internal sealed class FileHello
    {
        internal FileHello(int maxFrameBytes, int heartbeatSeconds, long maxFileBytes, int pieceBytes)
        {
            MaxFrameBytes = maxFrameBytes;
            HeartbeatSeconds = heartbeatSeconds;
            MaxFileBytes = maxFileBytes;
            PieceBytes = pieceBytes;
        }

        internal int MaxFrameBytes { get; }
        internal int HeartbeatSeconds { get; }
        internal long MaxFileBytes { get; }
        internal int PieceBytes { get; }
    }


    /// <summary>The outcome of opening a file connection.</summary>
    internal readonly struct FileOpen
    {
        internal FileOpen(FileHello hello, int status, string why)
        {
            Hello = hello;
            Status = status;
            Why = why;
        }

        /// <summary>The hello of the connection that is open now; null when it did not open.</summary>
        internal FileHello Hello { get; }

        /// <summary>The HTTP status of a refused upgrade; 0 for any other failure.</summary>
        internal int Status { get; }

        internal string Why { get; }

        internal bool IsOpen => Hello != null;
    }


    /// <summary>The server's answer to one request on the file connection, or why there is none.</summary>
    internal readonly struct FileReply
    {
        private FileReply(bool answered, int status, string body, string error, int? retryAfter, string why, int? serverClose)
        {
            Answered = answered;
            Status = status;
            Body = body;
            Error = error;
            RetryAfter = retryAfter;
            Why = why;
            ServerClose = serverClose;
        }

        /// <summary>An ack arrived. False when the connection was lost, closed or silent before one did.</summary>
        internal bool Answered { get; }

        internal int Status { get; }

        /// <summary>The raw JSON of the ack's <c>body</c>; null when it has none.</summary>
        internal string Body { get; }

        internal string Error { get; }

        internal int? RetryAfter { get; }

        /// <summary>Why there is no answer.</summary>
        internal string Why { get; }

        /// <summary>The close code the server sent when it closed the connection instead of answering; null otherwise.</summary>
        internal int? ServerClose { get; }

        internal static FileReply Ack(SessionFrame frame) => new FileReply(true, frame.Status, frame.Body, frame.Error, frame.RetryAfter, null, null);

        internal static FileReply Lost(string why, int? serverClose = null) => new FileReply(false, 0, null, null, null, why, serverClose);

        /// <summary>A field of the body: a string, or the text of a number; null when absent.</summary>
        internal string Field(string name)
        {
            if (string.IsNullOrEmpty(Body))
                return null;

            try
            {
                using (var document = JsonDocument.Parse(Body))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(name, out var element))
                        return null;

                    return element.ValueKind == JsonValueKind.String ? element.GetString()
                         : element.ValueKind == JsonValueKind.Number ? element.GetRawText()
                         : null;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal long? LongField(string name) =>
            long.TryParse(Field(name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : (long?)null;

        public override string ToString() =>
            !Answered ? $"no answer ({Why})" : string.IsNullOrEmpty(Error) ? $"status {Status}{(Body == null ? "" : " " + Body)}" : $"status {Status}: {Error}";
    }


    /// <summary>The file connection as the uploader sees it; a seam so the upload logic is tested without sockets.</summary>
    internal interface IFileChannel : IDisposable
    {
        /// <summary>True while a connection is open and not over.</summary>
        bool IsOpen { get; }

        /// <summary>The longest a piece frame took to write on this connection, for the report of a slow link.</summary>
        TimeSpan LastPieceWrite { get; }

        /// <summary>Opens a connection (waits for the session first). Closes the one that is open, if any.</summary>
        Task<FileOpen> OpenAsync(CancellationToken token);

        /// <summary>A text request (<c>begin</c>, <c>end</c>, <c>discard</c>) and its answer.</summary>
        Task<FileReply> RequestAsync(string op, string body, CancellationToken token);

        /// <summary>A piece frame (its request id is written by the channel) and its answer.</summary>
        Task<FileReply> SendPieceAsync(byte[] frame, int length, CancellationToken token);

        /// <summary>Closes the connection politely (1000), within a second; never throws.</summary>
        void Close(string reason);

        /// <summary>Drops the connection at once; never throws.</summary>
        void Abort(string reason);
    }


    /// <summary>
    /// The file connection of the collector session (#1888; wire contract, "File connection"): a third WebSocket to the
    /// session route with the subprotocol <c>hsm.files.v1</c>, named after the live session by its <c>sessionId</c>, opened
    /// only while a file is uploaded. Text requests (<c>begin</c>, <c>end</c>, <c>discard</c>) and binary piece frames, one
    /// in flight, each answered by a text ack. Unlike the table connection it has no loop of its own: the uploader opens it,
    /// uses it and closes it. Its receive and heartbeat loops run while it is open. Nothing here throws for a failure of the
    /// transport: a lost connection is an answer (<see cref="FileReply.Lost"/>).
    /// </summary>
    internal sealed class FileConnection : IFileChannel
    {
        private const int MaxInboundTextBytes = 1024 * 1024;
        private static readonly TimeSpan CloseAnswerTimeout = TimeSpan.FromSeconds(1);

        private readonly SessionConnection _session;
        private readonly ICollectorLogger _logger;
        private readonly SessionTimings _timings;
        private readonly TimeSpan _requestTimeout;
        private readonly object _gate = new object();

        private Link _link;
        private long _nextId;
        private long _lastPieceWriteTicks;

        internal FileConnection(SessionConnection session, ICollectorLogger logger, SessionTimings timings, TimeSpan requestTimeout)
        {
            _session = session;
            _logger = logger;
            _timings = timings ?? new SessionTimings();
            _requestTimeout = requestTimeout;
        }

        /// <summary>For tests: the wait for the ack of request <c>id</c> ends when the returned task completes, in place of the request timeout.</summary>
        internal Func<long, Task> AckDeadlineForTests { get; set; }

        public bool IsOpen => Volatile.Read(ref _link) is Link link && !link.Ended.IsCompleted;

        public TimeSpan LastPieceWrite => TimeSpan.FromTicks(Interlocked.Read(ref _lastPieceWriteTicks));


        // ---- Opening ------------------------------------------------------------------------------------------------

        public async Task<FileOpen> OpenAsync(CancellationToken token)
        {
            Abort("a new file connection is opened");

            await _session.WaitUntilUpAsync(token).ConfigureAwait(false);

            if (!_session.TryGetSession(out var sessionId, out var sessionEnded))
                return new FileOpen(null, 0, "the session went down");

            if (sessionId == null)
                return new FileOpen(null, 404, "the server's session states no sessionId: the server has no file connections");

            var socket = new ClientWebSocket();
            var handedOver = false;

            try
            {
                _session.Configure(socket, SessionProtocol.FilesSubprotocol);
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
                        return new FileOpen(null, 0, $"no file connection within {_timings.OpenTimeout.TotalSeconds:0.###} s");
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested && !(ex is OperationCanceledException))
                    {
                        return new FileOpen(null, TableConnection.UpgradeStatus(ex), $"the upgrade failed: {SessionConnection.Describe(ex)}");
                    }

                    if (!string.Equals(socket.SubProtocol, SessionProtocol.FilesSubprotocol, StringComparison.Ordinal))
                        return new FileOpen(null, 0, $"the server did not select the {SessionProtocol.FilesSubprotocol} subprotocol");

                    FileHello hello;

                    try
                    {
                        hello = await ReadHelloAsync(socket, open.Token).ConfigureAwait(false);

                        var probeId = Interlocked.Increment(ref _nextId);

                        await socket.SendAsync(new ArraySegment<byte>(PieceFrame.Probe(probeId)), WebSocketMessageType.Binary, true, open.Token).ConfigureAwait(false);

                        var answer = await ReceiveAnswerAsync(socket, open.Token).ConfigureAwait(false);

                        if (answer.Ack != probeId || answer.Status != 200)
                            throw new FileProtocolException(WebSocketCloseStatus.ProtocolError, $"the server's first answer on the file connection is not a 200 ack of its probe ({answer.Status})");
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        return new FileOpen(null, 0, $"no file hello and probe answer within {_timings.OpenTimeout.TotalSeconds:0.###} s");
                    }
                    catch (FileProtocolException ex)
                    {
                        await CloseSocketAsync(socket, ex.Code, ex.Message).ConfigureAwait(false);
                        return new FileOpen(null, 0, ex.Message);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        return new FileOpen(null, 0, SessionConnection.Describe(ex));
                    }

                    var link = new Link(socket, hello);

                    lock (_gate)
                        _link = link;

                    handedOver = true;
                    Serve(link, sessionEnded);

                    _logger?.Info($"The file connection is up: frame limit {hello.MaxFrameBytes} bytes, piece {hello.PieceBytes} bytes, file limit {hello.MaxFileBytes} bytes.");

                    return new FileOpen(hello, 0, null);
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

        private static async Task<FileHello> ReadHelloAsync(ClientWebSocket socket, CancellationToken token)
        {
            var text = await ReceiveTextAsync(socket, new byte[16 * 1024], token).ConfigureAwait(false);

            if (text == null)
                throw new InvalidOperationException($"the server closed the file connection before its hello{SessionConnection.DescribeClose(socket)}");

            if (!SessionFrame.TryParse(text, out var frame) || frame.Op != SessionProtocol.OpHello)
                throw new FileProtocolException(WebSocketCloseStatus.ProtocolError, "the first frame of the file connection is not a hello");

            long maxFileBytes = 0;
            var pieceBytes = 0;

            try
            {
                using (var document = JsonDocument.Parse(frame.Body ?? "{}"))
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("fileLimits", out var limits) && limits.ValueKind == JsonValueKind.Object)
                    {
                        if (limits.TryGetProperty("maxFileBytes", out var max) && max.ValueKind == JsonValueKind.Number)
                            max.TryGetInt64(out maxFileBytes);

                        if (limits.TryGetProperty("pieceBytes", out var piece) && piece.ValueKind == JsonValueKind.Number)
                            piece.TryGetInt32(out pieceBytes);
                    }
            }
            catch (JsonException)
            {
            }

            // A piece of the stated size must fit a frame whatever it compresses to; a hello that breaks it is a server this
            // collector cannot work with.
            if (frame.HeartbeatSeconds < SessionProtocol.MinHeartbeatSeconds || frame.HeartbeatSeconds > SessionProtocol.MaxHeartbeatSeconds
                || maxFileBytes < 0 || pieceBytes < 65536 || PieceFrame.MaxFrameBytes(pieceBytes) > frame.MaxFrameBytes)
                throw new FileProtocolException(WebSocketCloseStatus.ProtocolError,
                    $"the file hello states limits this collector cannot work with (maxFrameBytes {frame.MaxFrameBytes}, heartbeatSeconds {frame.HeartbeatSeconds}, pieceBytes {pieceBytes}, maxFileBytes {maxFileBytes})");

            return new FileHello(frame.MaxFrameBytes, frame.HeartbeatSeconds, maxFileBytes, pieceBytes);
        }

        private static async Task<SessionFrame> ReceiveAnswerAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];

            while (true)
            {
                var text = await ReceiveTextAsync(socket, buffer, token).ConfigureAwait(false);

                if (text == null)
                    throw new InvalidOperationException($"the server closed the file connection before it answered the probe{SessionConnection.DescribeClose(socket)}");

                if (!SessionFrame.TryParse(text, out var frame))
                    throw new FileProtocolException(WebSocketCloseStatus.InvalidPayloadData, "the server sent a text frame that is not a JSON object");

                if (frame.Op == SessionProtocol.OpPing || frame.Op == SessionProtocol.OpPong)
                    continue;

                return frame;
            }
        }


        // ---- Serving ------------------------------------------------------------------------------------------------

        private void Serve(Link link, Task sessionEnded)
        {
            var receive = Task.Run(() => ReceiveLoopAsync(link));
            var heartbeat = Task.Run(() => HeartbeatLoopAsync(link));

            _ = Task.Run(async () =>
            {
                var first = await Task.WhenAny(receive, heartbeat, link.Ended, sessionEnded).ConfigureAwait(false);
                string why;

                if (first == sessionEnded)
                    why = "its session ended";
                else if (first == link.Ended)
                    why = link.Ended.Result;
                else
                {
                    try { why = await ((Task<string>)first).ConfigureAwait(false); }
                    catch (Exception ex) { why = SessionConnection.Describe(ex); }
                }

                link.End(why);

                await CloseSocketAsync(link.Socket, link.CloseCode, link.CloseReason, link.SendLock).ConfigureAwait(false);

                link.Stop.Cancel();
                Observe(receive);
                Observe(heartbeat);

                lock (_gate)
                    if (ReferenceEquals(_link, link))
                        _link = null;

                try { link.Socket.Dispose(); } catch (Exception) { }
                try { link.Stop.Dispose(); } catch (Exception) { }

                link.MarkClosed();
                _logger?.Info($"The file connection ended: {why}.");
            });
        }

        private async Task<string> ReceiveLoopAsync(Link link)
        {
            var buffer = new byte[16 * 1024];

            while (true)
            {
                string text;

                try
                {
                    text = await ReceiveTextAsync(link.Socket, buffer, link.Stop.Token).ConfigureAwait(false);
                }
                catch (FileProtocolException ex)
                {
                    return link.ProtocolViolation(ex.Code, ex.Message);
                }

                if (text == null)
                {
                    var code = link.Socket.CloseStatus;

                    link.ServerClose = code.HasValue ? (int)code.Value : (int?)null;
                    link.CloseCode = code ?? WebSocketCloseStatus.NormalClosure;
                    link.CloseReason = string.Empty;

                    return $"closed by the server{SessionConnection.DescribeClose(link.Socket)}";
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
                        await link.SendAsync(SessionProtocol.Pong, SessionProtocol.Pong.Length, WebSocketMessageType.Text, _timings.SendTimeout, link.Stop.Token).ConfigureAwait(false);
                        break;

                    case SessionProtocol.OpPong:
                        break;

                    case null:
                        return link.ProtocolViolation(WebSocketCloseStatus.ProtocolError, "the server sent a frame that is neither an ack nor a push");
                }
            }
        }

        // A ping when nothing was sent for a heartbeat; given up when the server was silent for two and a half, except while
        // a piece is being written (a large frame on a slow link).
        private async Task<string> HeartbeatLoopAsync(Link link)
        {
            var interval = TimeSpan.FromSeconds(link.Hello.HeartbeatSeconds);
            var silence = TimeSpan.FromTicks(interval.Ticks * 5 / 2);
            var tick = TimeSpan.FromMilliseconds(Math.Max(20, Math.Min(1000, interval.TotalMilliseconds / 4)));

            while (true)
            {
                await Task.Delay(tick, link.Stop.Token).ConfigureAwait(false);

                if (!link.IsWritingPiece && link.SinceReceived >= silence)
                    return $"the server sent nothing for two and a half heartbeats ({silence.TotalSeconds:0.#} s)";

                if (link.SinceSent >= interval && await link.SendLock.WaitAsync(0).ConfigureAwait(false))
                {
                    try
                    {
                        await link.SendUnlockedAsync(SessionProtocol.Ping, SessionProtocol.Ping.Length, WebSocketMessageType.Text, _timings.SendTimeout, link.Stop.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        link.SendLock.Release();
                    }
                }
            }
        }


        // ---- Requests -------------------------------------------------------------------------------------------------

        public async Task<FileReply> RequestAsync(string op, string body, CancellationToken token)
        {
            var link = Volatile.Read(ref _link);

            if (link == null || link.Ended.IsCompleted)
                return FileReply.Lost("no file connection is open");

            var id = Interlocked.Increment(ref _nextId);
            var frame = SessionProtocol.BuildRequest(id, op, body);
            var waiter = link.Expect(id);

            try
            {
                await link.SendAsync(frame, frame.Length, WebSocketMessageType.Text, _timings.SendTimeout, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                link.Abort("a request write was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                link.End($"writing a {op} request failed: {SessionConnection.Describe(ex)}");
                return FileReply.Lost(link.Ended.Result, link.ServerClose);
            }

            return await AwaitAckAsync(link, id, waiter, token).ConfigureAwait(false);
        }

        public async Task<FileReply> SendPieceAsync(byte[] frame, int length, CancellationToken token)
        {
            var link = Volatile.Read(ref _link);

            if (link == null || link.Ended.IsCompleted)
                return FileReply.Lost("no file connection is open");

            var id = Interlocked.Increment(ref _nextId);

            BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(frame, 8, 8), id);

            var waiter = link.Expect(id);

            // A large frame on a slow link takes long to write: its write is bounded by ten heartbeats, and the ack deadline
            // starts only when it has been written.
            var bound = TimeSpan.FromTicks(Math.Max(_timings.SendTimeout.Ticks, TimeSpan.FromSeconds(link.Hello.HeartbeatSeconds).Ticks * 10));
            var writing = Stopwatch.StartNew();

            try
            {
                link.BeginPiece();

                try
                {
                    await link.SendAsync(frame, length, WebSocketMessageType.Binary, bound, token).ConfigureAwait(false);
                }
                finally
                {
                    link.EndPiece();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                link.Abort("a piece write was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                link.End($"writing a piece failed: {SessionConnection.Describe(ex)}");
                return FileReply.Lost(link.Ended.Result, link.ServerClose);
            }
            finally
            {
                Interlocked.Exchange(ref _lastPieceWriteTicks, writing.Elapsed.Ticks);
            }

            return await AwaitAckAsync(link, id, waiter, token).ConfigureAwait(false);
        }

        private async Task<FileReply> AwaitAckAsync(Link link, long id, Task<SessionFrame> waiter, CancellationToken token)
        {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var deadline = AckDeadlineForTests?.Invoke(id) ?? Task.Delay(_requestTimeout, wait.Token);
                var cancelled = Task.Delay(Timeout.Infinite, wait.Token);
                var first = await Task.WhenAny(waiter, link.Ended, deadline, cancelled).ConfigureAwait(false);

                wait.Cancel();

                // An answer that is there is taken, whatever else ended at the same moment.
                if (first == waiter || waiter.Status == TaskStatus.RanToCompletion)
                    return FileReply.Ack(await waiter.ConfigureAwait(false));
            }

            token.ThrowIfCancellationRequested();

            if (link.Ended.IsCompleted)
            {
                // The receive loop records the server's close code before the link ends.
                return FileReply.Lost(link.Ended.Result, link.ServerClose);
            }

            // No ack in time: the connection is not trusted any more (a late ack must not be taken for the next request's).
            link.End($"a request got no ack within {_requestTimeout.TotalSeconds:0.###} s");
            return FileReply.Lost(link.Ended.Result);
        }


        // ---- Closing ------------------------------------------------------------------------------------------------

        public void Close(string reason)
        {
            var link = Volatile.Read(ref _link);

            if (link == null)
                return;

            link.CloseCode = WebSocketCloseStatus.NormalClosure;
            link.CloseReason = reason;
            link.End(reason);

            // The serving task says the close (bounded) and drops the socket; waited for at most a second.
            try { link.Closed.Wait(TimeSpan.FromSeconds(1.5)); }
            catch (Exception) { }
        }

        public void Abort(string reason)
        {
            var link = Volatile.Read(ref _link);

            if (link == null)
                return;

            link.Abort(reason);
        }

        public void Dispose() => Abort("the collector is disposed");


        // ---- Frames -------------------------------------------------------------------------------------------------

        // One whole text message, or null when the server closed. A binary frame, or a text larger than an ack can be, is a
        // protocol error.
        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, byte[] buffer, CancellationToken token)
        {
            MemoryStream message = null;

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return null;

                    if (result.MessageType != WebSocketMessageType.Text)
                        throw new FileProtocolException(WebSocketCloseStatus.InvalidMessageType, "the server sent a binary frame on the file connection");

                    if (result.EndOfMessage && message == null)
                        return Encoding.UTF8.GetString(buffer, 0, result.Count);

                    if (message == null)
                        message = new MemoryStream();

                    if (message.Length + result.Count > MaxInboundTextBytes)
                        throw new FileProtocolException(WebSocketCloseStatus.MessageTooBig, $"the server sent a text frame larger than {MaxInboundTextBytes} bytes on the file connection");

                    message.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
            finally
            {
                message?.Dispose();
            }
        }

        // Says the close (ours, or the answer to the server's) for at most a second, then lets the socket go.
        private static async Task CloseSocketAsync(ClientWebSocket socket, WebSocketCloseStatus code, string reason, SemaphoreSlim sendLock = null)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    if (sendLock == null || await sendLock.WaitAsync(CloseAnswerTimeout).ConfigureAwait(false))
                    {
                        try
                        {
                            using (var timeout = new CancellationTokenSource(CloseAnswerTimeout))
                                await socket.CloseOutputAsync(code, Truncate(reason), timeout.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            sendLock?.Release();
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

        private static string Truncate(string reason) => string.IsNullOrEmpty(reason) ? string.Empty : reason.Length <= 100 ? reason : reason.Substring(0, 100);

        private static void Observe(Task task) =>
            task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);


        private sealed class FileProtocolException : Exception
        {
            internal FileProtocolException(WebSocketCloseStatus code, string message) : base(message) => Code = code;

            internal WebSocketCloseStatus Code { get; }
        }


        /// <summary>One open file connection.</summary>
        private sealed class Link
        {
            private readonly TaskCompletionSource<string> _ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _ackLock = new object();
            private long _pendingId;
            private TaskCompletionSource<SessionFrame> _pendingAck;
            private long _highestSent;
            private long _lastSent;
            private long _lastReceived;
            private int _writingPiece;

            internal Link(ClientWebSocket socket, FileHello hello)
            {
                Socket = socket;
                Hello = hello;
                NoteSentNow();
                NoteReceived();
            }

            internal ClientWebSocket Socket { get; }

            internal FileHello Hello { get; }

            internal SemaphoreSlim SendLock { get; } = new SemaphoreSlim(1, 1);

            internal CancellationTokenSource Stop { get; } = new CancellationTokenSource();

            internal Task<string> Ended => _ended.Task;

            /// <summary>Completes when the serving task has said the close and dropped the socket.</summary>
            internal Task Closed => _closed.Task;

            internal void MarkClosed() => _closed.TrySetResult(true);

            internal int? ServerClose { get; set; }

            internal WebSocketCloseStatus CloseCode { get; set; } = WebSocketCloseStatus.NormalClosure;

            internal string CloseReason { get; set; } = string.Empty;

            internal bool IsWritingPiece => Volatile.Read(ref _writingPiece) == 1;

            internal TimeSpan SinceSent => Since(ref _lastSent);

            internal TimeSpan SinceReceived => Since(ref _lastReceived);

            internal void End(string why) => _ended.TrySetResult(why);

            internal void Abort(string why)
            {
                End(why);

                try { Stop.Cancel(); } catch (Exception) { }
                try { Socket.Abort(); } catch (Exception) { }
            }

            internal string ProtocolViolation(WebSocketCloseStatus code, string why)
            {
                CloseCode = code;
                CloseReason = why;
                End(why);
                return why;
            }

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

                    // An ack of a request this connection sent and no longer waits for is dropped; of one never sent, an error.
                    return id > 0 && id <= _highestSent;
                }
            }

            internal void BeginPiece() => Volatile.Write(ref _writingPiece, 1);

            // The silence clock starts again when a piece is written: the time it took is not the server's silence.
            internal void EndPiece()
            {
                Volatile.Write(ref _writingPiece, 0);
                NoteSentNow();
                NoteReceived();
            }

            internal void NoteReceived() => Volatile.Write(ref _lastReceived, Stopwatch.GetTimestamp());

            private void NoteSentNow() => Volatile.Write(ref _lastSent, Stopwatch.GetTimestamp());

            internal async Task SendAsync(byte[] data, int length, WebSocketMessageType type, TimeSpan bound, CancellationToken token)
            {
                await SendLock.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    await SendUnlockedAsync(data, length, type, bound, token).ConfigureAwait(false);
                }
                finally
                {
                    SendLock.Release();
                }
            }

            internal async Task SendUnlockedAsync(byte[] data, int length, WebSocketMessageType type, TimeSpan bound, CancellationToken token)
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, Stop.Token))
                {
                    timeout.CancelAfter(bound);

                    try
                    {
                        await Socket.SendAsync(new ArraySegment<byte>(data, 0, length), type, true, timeout.Token).ConfigureAwait(false);
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
