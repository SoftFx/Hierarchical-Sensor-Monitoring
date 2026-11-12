#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// The server side of a collector session, scripted by a test: it accepts or refuses the
    /// upgrade, says hello, acknowledges requests with a chosen status or not at all, drops
    /// sessions, and records everything the collector sent, including any plain HTTP request,
    /// which a collector on a session must never make.
    ///
    /// The WebSocket protocol is <see cref="HttpListener"/>'s; this class speaks only the JSON
    /// envelope of the session (aicontext/features/api/wire-contract/feature.md).
    ///
    /// Used by the session tests of both collector test projects and by the managed
    /// conformance driver.
    /// </summary>
    public sealed class ScriptedSessionServer : IDisposable
    {
        public const string Subprotocol = "hsm.sensors.v1";

        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<SessionRequest> _requests = new ConcurrentQueue<SessionRequest>();
        private readonly ConcurrentQueue<string> _httpRequests = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<int> _closes = new ConcurrentQueue<int>();
        private readonly ConcurrentDictionary<int, WebSocket> _open = new ConcurrentDictionary<int, WebSocket>();
        private readonly ConcurrentDictionary<string, int> _ackStatus = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly Task _loop;

        private Dictionary<string, string> _upgradeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int _refuseStatus;
        private int _listFramesBeforeFailure = -1;
        private int _listFailStatus;
        private int _silentRequests;
        private int _answerPings = 1;
        private int _sessions;
        private int _refused;
        private int _pings;

        public ScriptedSessionServer()
        {
            Port = GetFreeTcpPort();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        /// <summary>For <c>CollectorOptions.ServerAddress</c>; plaintext, so the collector needs AllowPlaintextTransport.</summary>
        public string ServerAddress => "http://localhost";

        // --- the script ---

        /// <summary>What the hello of the sessions accepted from now on states.</summary>
        public int MaxFrameBytes { get; set; } = 4 * 1024 * 1024;

        public int HeartbeatSeconds { get; set; } = 30;

        /// <summary>Every upgrade from now on is refused with this HTTP status.</summary>
        public void RefuseUpgrades(int status) => Volatile.Write(ref _refuseStatus, status);

        public void AcceptUpgrades() => Volatile.Write(ref _refuseStatus, 0);

        /// <summary>Requests of <paramref name="op"/> are acknowledged with this status from now on (200 by default).</summary>
        public void AckWith(string op, int status) => _ackStatus[op] = status;

        /// <summary>
        /// Accepts this many more <c>list</c> frames, then answers every later one with
        /// <paramref name="status"/>, or with nothing at all when the status is 0. A negative
        /// number lifts the limit.
        /// </summary>
        public void FailListFramesAfter(int accepted, int status)
        {
            Volatile.Write(ref _listFailStatus, status);
            Volatile.Write(ref _listFramesBeforeFailure, accepted);
        }

        /// <summary>The next <paramref name="requests"/> requests are recorded and get no ack.</summary>
        public void StaySilentFor(int requests) => Interlocked.Exchange(ref _silentRequests, requests);

        /// <summary>Heartbeats are recorded and not answered: to the collector the server has gone quiet.</summary>
        public void StopAnsweringPings() => Volatile.Write(ref _answerPings, 0);

        public void AnswerPings() => Volatile.Write(ref _answerPings, 1);

        /// <summary>Ends every open session without a close handshake, as a lost connection does.</summary>
        public void DropSessions()
        {
            foreach (var session in _open.ToArray())
            {
                try { session.Value.Abort(); }
                catch (Exception) { }
            }
        }

        // --- what the collector did ---

        /// <summary>Upgrades accepted so far.</summary>
        public int Sessions => Volatile.Read(ref _sessions);

        public int RefusedUpgrades => Volatile.Read(ref _refused);

        public int Pings => Volatile.Read(ref _pings);

        /// <summary>Every request frame received, in order of arrival.</summary>
        public IReadOnlyList<SessionRequest> Requests => _requests.ToArray();

        /// <summary>The requests that were acknowledged with a 2xx status.</summary>
        public IEnumerable<SessionRequest> Accepted => Requests.Where(r => r.AckStatus >= 200 && r.AckStatus < 300);

        /// <summary>Every request that was not a WebSocket upgrade, as "METHOD path".</summary>
        public IReadOnlyList<string> HttpRequests => _httpRequests.ToArray();

        /// <summary>The close codes the collector sent, in order.</summary>
        public IReadOnlyList<int> Closes => _closes.ToArray();

        /// <summary>The request headers of the last upgrade, by case-insensitive name.</summary>
        public Dictionary<string, string> UpgradeHeaders => Volatile.Read(ref _upgradeHeaders);

        /// <summary>The elements of the accepted frames of <paramref name="op"/>, each as the JSON the collector sent.</summary>
        public List<string> AcceptedElements(string op)
        {
            var elements = new List<string>();

            foreach (var request in Accepted.Where(r => r.Op == op))
                using (var document = JsonDocument.Parse(request.Body))
                    elements.AddRange(document.RootElement.EnumerateArray().Select(e => e.GetRawText()));

            return elements;
        }


        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (!context.Request.IsWebSocketRequest)
                {
                    _httpRequests.Enqueue($"{context.Request.HttpMethod} {context.Request.Url?.AbsolutePath}");
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                var refuse = Volatile.Read(ref _refuseStatus);

                if (refuse != 0)
                {
                    Interlocked.Increment(ref _refused);
                    context.Response.StatusCode = refuse;
                    context.Response.Close();
                    return;
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var name in context.Request.Headers.AllKeys)
                    headers[name] = context.Request.Headers[name];

                Volatile.Write(ref _upgradeHeaders, headers);

                var upgraded = await context.AcceptWebSocketAsync(Subprotocol).ConfigureAwait(false);

                await ServeAsync(upgraded.WebSocket).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A session that ends badly is part of what the tests script.
            }
        }

        private async Task ServeAsync(WebSocket socket)
        {
            var index = Interlocked.Increment(ref _sessions);
            _open[index] = socket;

            try
            {
                await SendAsync(socket, $"{{\"op\":\"hello\",\"body\":{{\"maxFrameBytes\":{MaxFrameBytes},\"heartbeatSeconds\":{HeartbeatSeconds}}}}}").ConfigureAwait(false);

                var buffer = new ArraySegment<byte>(new byte[64 * 1024]);

                while (socket.State == WebSocketState.Open)
                {
                    string text;

                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;

                        do
                        {
                            result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                _closes.Enqueue((int)(result.CloseStatus ?? 0));
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                                return;
                            }

                            message.Write(buffer.Array, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        text = Encoding.UTF8.GetString(message.ToArray());
                    }

                    await AnswerAsync(socket, index, text).ConfigureAwait(false);
                }
            }
            finally
            {
                _open.TryRemove(index, out _);
                socket.Dispose();
            }
        }

        private async Task AnswerAsync(WebSocket socket, int session, string text)
        {
            string op;
            long? id = null;
            string body = null;

            using (var document = JsonDocument.Parse(text))
            {
                var root = document.RootElement;

                op = root.TryGetProperty("op", out var opElement) ? opElement.GetString() : null;

                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var parsed))
                    id = parsed;

                if (root.TryGetProperty("body", out var bodyElement))
                    body = bodyElement.GetRawText();
            }

            if (op == "ping")
            {
                Interlocked.Increment(ref _pings);

                if (Volatile.Read(ref _answerPings) == 1)
                    await SendAsync(socket, "{\"op\":\"pong\"}").ConfigureAwait(false);

                return;
            }

            if (!id.HasValue)
                return;

            if (Interlocked.Decrement(ref _silentRequests) >= 0)
            {
                _requests.Enqueue(new SessionRequest(session, id.Value, op, body, text, 0));
                return;
            }

            Interlocked.Increment(ref _silentRequests); // floor the counter at 0

            var status = _ackStatus.TryGetValue(op ?? string.Empty, out var scripted) ? scripted : 200;

            if (op == "list" && Volatile.Read(ref _listFramesBeforeFailure) >= 0 && Interlocked.Decrement(ref _listFramesBeforeFailure) < 0)
            {
                Volatile.Write(ref _listFramesBeforeFailure, 0);
                status = Volatile.Read(ref _listFailStatus);
            }

            _requests.Enqueue(new SessionRequest(session, id.Value, op, body, text, status));

            if (status == 0)
                return;

            await SendAsync(socket, status >= 200 && status < 300
                ? $"{{\"ack\":{id.Value},\"status\":{status},\"body\":{{}}}}"
                : $"{{\"ack\":{id.Value},\"status\":{status},\"error\":\"scripted refusal\"}}").ConfigureAwait(false);
        }

        private Task SendAsync(WebSocket socket, string text) =>
            socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, _stop.Token);

        private static int GetFreeTcpPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            DropSessions();

            try { _listener.Stop(); } catch (Exception) { }
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }

            _listener.Close();
            _stop.Dispose();
        }


        public sealed class SessionRequest
        {
            public SessionRequest(int session, long id, string op, string body, string frame, int ackStatus)
            {
                Session = session;
                Id = id;
                Op = op;
                Body = body;
                Frame = frame;
                AckStatus = ackStatus;
            }

            /// <summary>Which session carried it: 1 for the first accepted upgrade.</summary>
            public int Session { get; }

            public long Id { get; }

            public string Op { get; }

            /// <summary>The JSON of <c>body</c>, exactly as the collector wrote it.</summary>
            public string Body { get; }

            /// <summary>The whole frame text.</summary>
            public string Frame { get; }

            /// <summary>The status it was acknowledged with; 0 when it got no ack.</summary>
            public int AckStatus { get; }
        }
    }
}
