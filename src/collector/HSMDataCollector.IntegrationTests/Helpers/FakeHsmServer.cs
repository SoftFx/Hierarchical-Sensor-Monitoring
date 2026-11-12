using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.IntegrationTests.Helpers
{
    /// <summary>
    /// In-process fake HSM Sensor API over real plaintext HTTP (no Docker, no TLS): captures every
    /// request the collector makes (path, method, headers, body) and can inject transient failures.
    /// The collector talks to it through its normal HttpClient stack, so this exercises the real
    /// serializer, headers, batching and retry/re-enqueue path end-to-end — the smoke layer of the
    /// conformance strategy (epic #1093 / #1094), complementing the in-proc corpus and the
    /// Dockerized real-server integration tests.
    /// </summary>
    public sealed class FakeHsmServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<CapturedRequest> _requests = new ConcurrentQueue<CapturedRequest>();
        private readonly Task _loop;

        // Data POSTs to fail with 503 before the first success (decremented per data request).
        private int _failDataRequests;

        // Command POSTs (/commands, /addOrUpdate) to fail with 503 before the first success.
        private int _failCommandRequests;

        // Command POSTs whose connection is dropped without any response.
        private int _abortCommandRequests;

        // Statuses scripted for the next command POSTs, one per request, in order.
        private readonly ConcurrentQueue<HttpStatusCode> _scriptedCommandStatuses = new ConcurrentQueue<HttpStatusCode>();

        // X-Hsm-Directive values added to every data response.
        private string[] _directives = new string[0];

        // Answers scripted for the next data POSTs, one per request, in order.
        private readonly ConcurrentQueue<ScriptedAnswer> _scriptedDataAnswers = new ConcurrentQueue<ScriptedAnswer>();

        public FakeHsmServer()
        {
            Port = GetFreeTcpPort();
            // The collector posts to {address}:{port}/api/sensors/{endpoint}; a root prefix
            // captures every endpoint (data, addOrUpdate, commands, testConnection, file).
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public string ServerAddress => "http://localhost";

        public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

        /// <summary>The next <paramref name="count"/> data POSTs answer 503; the collector must
        /// re-enqueue and retry until they stop failing.</summary>
        public void FailNextDataRequests(int count) => Interlocked.Exchange(ref _failDataRequests, count);

        /// <summary>The next <paramref name="count"/> command POSTs (/commands or /addOrUpdate)
        /// answer 503.</summary>
        public void FailNextCommandRequests(int count) => Interlocked.Exchange(ref _failCommandRequests, count);

        /// <summary>The next command POST (/commands or /addOrUpdate) answers
        /// <paramref name="status"/>; it takes precedence over <see cref="FailNextCommandRequests"/>.</summary>
        public void AnswerNextCommandRequest(HttpStatusCode status) => _scriptedCommandStatuses.Enqueue(status);

        /// <summary>The next <paramref name="count"/> command POSTs get no answer at all: the
        /// connection is dropped, which the collector sees as a failed transport, not a status.
        /// Such a request is captured with <see cref="CapturedRequest.Answer"/> 0.</summary>
        public void AbortNextCommandRequests(int count) => Interlocked.Exchange(ref _abortCommandRequests, count);

        /// <summary>The next data POST answers <paramref name="status"/> with
        /// <paramref name="body"/> as its content; it takes precedence over
        /// <see cref="FailNextDataRequests"/>.</summary>
        public void AnswerNextDataRequest(HttpStatusCode status, string body = null, string contentType = "text/plain") =>
            _scriptedDataAnswers.Enqueue(new ScriptedAnswer(status, body, contentType));

        /// <summary>Directives the server piggybacks on every data response from now on, one
        /// <c>X-Hsm-Directive</c> header each, as the real server does for an agent.</summary>
        public void SendDirectivesOnDataResponses(params string[] directives) => Volatile.Write(ref _directives, directives);

        public IEnumerable<CapturedRequest> CommandRequests =>
            Requests.Where(r => r.Method == "POST"
                                && (r.Path.EndsWith("/addOrUpdate", StringComparison.Ordinal)
                                    || r.Path.EndsWith("/commands", StringComparison.Ordinal)));

        public IEnumerable<CapturedRequest> DataRequests =>
            Requests.Where(r => r.Method == "POST"
                                && !r.Path.EndsWith("/addOrUpdate", StringComparison.Ordinal)
                                && !r.Path.EndsWith("/commands", StringComparison.Ordinal)
                                && !r.Path.EndsWith("/testConnection", StringComparison.Ordinal));

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }

                _ = Task.Run(() => Handle(context));
            }
        }

        // Takes one from a scripted counter while it is above zero. One step, so a test that
        // sets the counter between a decrement and its undo cannot end up with one more than it
        // asked for.
        private static bool TryTakeOne(ref int counter)
        {
            while (true)
            {
                var current = Volatile.Read(ref counter);
                if (current <= 0)
                    return false;

                if (Interlocked.CompareExchange(ref counter, current - 1, current) == current)
                    return true;
            }
        }

        private void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = reader.ReadToEnd();

            var path = request.Url?.AbsolutePath ?? string.Empty;
            var isCommand = request.HttpMethod == "POST"
                            && (path.EndsWith("/addOrUpdate", StringComparison.Ordinal)
                                || path.EndsWith("/commands", StringComparison.Ordinal));
            var isData = request.HttpMethod == "POST"
                         && !isCommand
                         && !path.EndsWith("/testConnection", StringComparison.Ordinal);

            if (isCommand)
            {
                if (TryTakeOne(ref _abortCommandRequests))
                {
                    _requests.Enqueue(new CapturedRequest(request.HttpMethod, path, request.Headers["Key"], request.Headers["ClientName"], body, 0, CopyHeaders(request)));
                    context.Response.Abort();
                    return;
                }
            }

            var statusCode = HttpStatusCode.OK;
            ScriptedAnswer scriptedData = null;
            if (isCommand && _scriptedCommandStatuses.TryDequeue(out var scripted))
                statusCode = scripted;
            else if (isData && _scriptedDataAnswers.TryDequeue(out scriptedData))
                statusCode = scriptedData.Status;
            else if (isData && TryTakeOne(ref _failDataRequests))
                statusCode = HttpStatusCode.ServiceUnavailable;
            else if (isCommand && TryTakeOne(ref _failCommandRequests))
                statusCode = HttpStatusCode.ServiceUnavailable;

            _requests.Enqueue(new CapturedRequest(
                request.HttpMethod,
                path,
                request.Headers["Key"],
                request.Headers["ClientName"],
                body,
                statusCode,
                CopyHeaders(request)));

            try
            {
                context.Response.StatusCode = (int)statusCode;

                if (isData)
                    foreach (var directive in Volatile.Read(ref _directives))
                        context.Response.AppendHeader("X-Hsm-Directive", directive);

                // The real /commands route answers the dictionary of per-path errors, empty when
                // every command was accepted, and the collector reads it: an empty body is a
                // failed send to the command handler.
                if (isCommand && statusCode == HttpStatusCode.OK)
                {
                    var accepted = System.Text.Encoding.UTF8.GetBytes("{}");
                    context.Response.ContentType = "application/json";
                    context.Response.Close(accepted, willBlock: true);
                }
                else if (scriptedData?.Body != null)
                {
                    context.Response.ContentType = scriptedData.ContentType;
                    context.Response.Close(System.Text.Encoding.UTF8.GetBytes(scriptedData.Body), willBlock: true);
                }
                else
                    context.Response.Close();
            }
            catch (HttpListenerException)
            {
                // client went away mid-response — irrelevant to the assertions
            }
        }

        private static Dictionary<string, string> CopyHeaders(HttpListenerRequest request)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in request.Headers.AllKeys)
                headers[name] = request.Headers[name];

            return headers;
        }

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
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* already stopped */ }
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* loop already unwound */ }
            _listener.Close();
            _cts.Dispose();
        }

        private sealed class ScriptedAnswer
        {
            public ScriptedAnswer(HttpStatusCode status, string body, string contentType)
            {
                Status = status;
                Body = body;
                ContentType = contentType;
            }

            public HttpStatusCode Status { get; }
            public string Body { get; }
            public string ContentType { get; }
        }

        public sealed class CapturedRequest
        {
            public CapturedRequest(string method, string path, string key, string clientName, string body, HttpStatusCode answer, Dictionary<string, string> headers)
            {
                Answer = answer;
                Headers = headers;
                Method = method;
                Path = path;
                Key = key;
                ClientName = clientName;
                Body = body;
            }

            public string Method { get; }
            public string Path { get; }
            public string Key { get; }
            public string ClientName { get; }
            public string Body { get; }

            /// <summary>The status this request was answered with.</summary>
            public HttpStatusCode Answer { get; }

            /// <summary>The request headers, by case-insensitive name.</summary>
            public Dictionary<string, string> Headers { get; }
        }
    }
}
