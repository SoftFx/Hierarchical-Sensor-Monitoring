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

        private void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = reader.ReadToEnd();

            var path = request.Url?.AbsolutePath ?? string.Empty;
            var isData = request.HttpMethod == "POST"
                         && !path.EndsWith("/addOrUpdate", StringComparison.Ordinal)
                         && !path.EndsWith("/commands", StringComparison.Ordinal)
                         && !path.EndsWith("/testConnection", StringComparison.Ordinal);

            _requests.Enqueue(new CapturedRequest(
                request.HttpMethod,
                path,
                request.Headers["Key"],
                request.Headers["ClientName"],
                body));

            var statusCode = HttpStatusCode.OK;
            if (isData && Interlocked.Decrement(ref _failDataRequests) >= 0)
                statusCode = HttpStatusCode.ServiceUnavailable;
            else if (isData)
                Interlocked.Increment(ref _failDataRequests); // floor the counter at 0

            try
            {
                context.Response.StatusCode = (int)statusCode;
                context.Response.Close();
            }
            catch (HttpListenerException)
            {
                // client went away mid-response — irrelevant to the assertions
            }
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

        public sealed class CapturedRequest
        {
            public CapturedRequest(string method, string path, string key, string clientName, string body)
            {
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
        }
    }
}
