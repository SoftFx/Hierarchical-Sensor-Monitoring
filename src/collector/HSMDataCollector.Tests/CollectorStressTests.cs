using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorStressTests
    {
        private readonly ITestOutputHelper _output;

        public CollectorStressTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Collector_survives_transient_server_failures_under_parallel_load()
        {
            if (!HttpListener.IsSupported)
                return;

            var serverOptions = new FakeHsmServerOptions
            {
                AlwaysSucceedFirstRequests = 3,
                FailureEvery = 7,
                AbortEvery = 17,
                SlowEvery = 11,
                SlowDelay = TimeSpan.FromMilliseconds(200)
            };

            using (var server = FakeHsmServer.Start(serverOptions))
            using (var collector = CreateCollector(server.Port, TimeSpan.FromSeconds(2), maxQueueSize: 30000))
            {
                var sensors = CreateSensors(collector, 48);

                collector.Initialize(false);

                await ProduceConcurrentLoadAsync(sensors, workerCount: 24, valuesPerWorker: 800).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                await DisposeWithinAsync(collector, TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                WriteServerStats(server);

                Assert.True(server.TotalRequests > 0, "The fake HSM server should receive collector requests.");
                Assert.True(server.CommandRequests > 0, "The collector should register sensors via command requests.");
                Assert.True(server.DataRequests > 0, "The collector should send queued sensor values.");
                Assert.True(server.FailedResponses > 0, "The stress server should exercise HTTP 500 responses.");
                Assert.True(server.AbortedConnections > 0, "The stress server should exercise broken connections.");
                Assert.True(server.SlowResponses > 0, "The stress server should exercise slow responses.");
            }
        }

        [LongStressFact]
        public async Task Collector_runs_for_ten_minutes_against_flaky_server_under_sustained_load()
        {
            if (!HttpListener.IsSupported)
                return;

            var duration = GetLongStressDuration();
            var initialManagedMemory = GC.GetTotalMemory(forceFullCollection: true);

            var serverOptions = new FakeHsmServerOptions
            {
                AlwaysSucceedFirstRequests = 3,
                FailureEvery = 19,
                AbortEvery = 43,
                SlowEvery = 29,
                SlowDelay = TimeSpan.FromSeconds(1)
            };

            using (var server = FakeHsmServer.Start(serverOptions))
            using (var collector = CreateCollector(server.Port, TimeSpan.FromSeconds(3), maxQueueSize: 100000))
            {
                var sensors = CreateSensors(collector, 96);

                collector.Initialize(false);

                await ProduceSustainedLoadAsync(sensors, duration).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                await DisposeWithinAsync(collector, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                var finalManagedMemory = GC.GetTotalMemory(forceFullCollection: true);
                var managedMemoryGrowth = finalManagedMemory - initialManagedMemory;

                WriteServerStats(server);
                _output.WriteLine("Managed memory growth: {0:n0} bytes", managedMemoryGrowth);

                Assert.True(server.TotalRequests > 100, "Long stress should produce many HTTP requests.");
                Assert.True(server.DataRequests > 50, "Long stress should keep sending sensor data.");
                Assert.True(server.FailedResponses > 0, "Long stress should include HTTP failures.");
                Assert.True(server.AbortedConnections > 0, "Long stress should include broken connections.");
                Assert.True(managedMemoryGrowth < 256L * 1024 * 1024, "Managed memory should not grow without bounds during stress.");
            }
        }

        private static DataCollector CreateCollector(int port, TimeSpan requestTimeout, int maxQueueSize)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "stress-test-key",
                ClientName = "stress-test-client",
                ComputerName = "stress-test-host",
                Module = "stress-test-module",
                ServerAddress = "http://127.0.0.1",
                Port = port,
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = 250,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(100),
                RequestTimeout = requestTimeout,
                ExceptionDeduplicatorWindow = TimeSpan.FromSeconds(2),
                MaxDeduplicatedMessages = 1000
            });
        }

        private static IReadOnlyList<IInstantValueSensor<double>> CreateSensors(DataCollector collector, int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => collector.CreateDoubleSensor("stress/double/" + i.ToString(CultureInfo.InvariantCulture)))
                .ToArray();
        }

        private static Task ProduceConcurrentLoadAsync(IReadOnlyList<IInstantValueSensor<double>> sensors, int workerCount, int valuesPerWorker)
        {
            var tasks = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(() =>
                {
                    for (var i = 0; i < valuesPerWorker; i++)
                    {
                        var sensor = sensors[(worker + i) % sensors.Count];
                        sensor.AddValue(worker * valuesPerWorker + i, SensorStatus.Ok, "parallel-load");
                    }
                }))
                .ToArray();

            return Task.WhenAll(tasks);
        }

        private static async Task ProduceSustainedLoadAsync(IReadOnlyList<IInstantValueSensor<double>> sensors, TimeSpan duration)
        {
            var stopwatch = Stopwatch.StartNew();
            var workerCount = Math.Max(8, Environment.ProcessorCount * 2);
            var tasks = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(async () =>
                {
                    var value = 0L;

                    while (stopwatch.Elapsed < duration)
                    {
                        for (var batch = 0; batch < 500; batch++)
                        {
                            var sensor = sensors[(int)((value + worker) % sensors.Count)];
                            sensor.AddValue(value, SensorStatus.Ok, "sustained-load");
                            value++;
                        }

                        await Task.Delay(1).ConfigureAwait(false);
                    }
                }))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task DisposeWithinAsync(DataCollector collector, TimeSpan timeout)
        {
            var disposeTask = Task.Run(() => collector.Dispose());
            var completed = await Task.WhenAny(disposeTask, Task.Delay(timeout)).ConfigureAwait(false);

            Assert.True(completed == disposeTask, "Collector disposal should complete even when the server is flaky.");

            await disposeTask.ConfigureAwait(false);
        }

        private static TimeSpan GetLongStressDuration()
        {
            var rawMinutes = Environment.GetEnvironmentVariable("HSM_COLLECTOR_STRESS_MINUTES");

            if (double.TryParse(rawMinutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
                return TimeSpan.FromMinutes(minutes);

            return TimeSpan.FromMinutes(10);
        }

        private void WriteServerStats(FakeHsmServer server)
        {
            _output.WriteLine("Requests: {0}", server.TotalRequests);
            _output.WriteLine("Command requests: {0}", server.CommandRequests);
            _output.WriteLine("Data requests: {0}", server.DataRequests);
            _output.WriteLine("Failed responses: {0}", server.FailedResponses);
            _output.WriteLine("Aborted connections: {0}", server.AbortedConnections);
            _output.WriteLine("Slow responses: {0}", server.SlowResponses);
            _output.WriteLine("Request bytes: {0}", server.RequestBytes);
            _output.WriteLine("Max concurrent requests: {0}", server.MaxConcurrentRequests);
        }

        private sealed class LongStressFactAttribute : FactAttribute
        {
            public LongStressFactAttribute()
            {
                if (!string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_LONG_STRESS"), "1", StringComparison.Ordinal))
                    Skip = "Set HSM_COLLECTOR_RUN_LONG_STRESS=1 to run the 10-minute collector stress test.";
            }
        }

        private sealed class FakeHsmServerOptions
        {
            public int AlwaysSucceedFirstRequests { get; set; }

            public int FailureEvery { get; set; }

            public int AbortEvery { get; set; }

            public int SlowEvery { get; set; }

            public TimeSpan SlowDelay { get; set; }
        }

        private sealed class FakeHsmServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
            private readonly FakeHsmServerOptions _options;
            private readonly Task _listenTask;

            private int _currentConcurrentRequests;
            private int _maxConcurrentRequests;
            private long _totalRequests;
            private long _commandRequests;
            private long _dataRequests;
            private long _failedResponses;
            private long _abortedConnections;
            private long _slowResponses;
            private long _requestBytes;

            private FakeHsmServer(FakeHsmServerOptions options)
            {
                _options = options;
                Port = GetFreePort();

                _listener.Prefixes.Add("http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/");
                _listener.Start();
                _listenTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));
            }

            public int Port { get; }

            public long TotalRequests => Interlocked.Read(ref _totalRequests);

            public long CommandRequests => Interlocked.Read(ref _commandRequests);

            public long DataRequests => Interlocked.Read(ref _dataRequests);

            public long FailedResponses => Interlocked.Read(ref _failedResponses);

            public long AbortedConnections => Interlocked.Read(ref _abortedConnections);

            public long SlowResponses => Interlocked.Read(ref _slowResponses);

            public long RequestBytes => Interlocked.Read(ref _requestBytes);

            public int MaxConcurrentRequests => Volatile.Read(ref _maxConcurrentRequests);

            public static FakeHsmServer Start(FakeHsmServerOptions options)
            {
                return new FakeHsmServer(options ?? new FakeHsmServerOptions());
            }

            public void Dispose()
            {
                _cancellationTokenSource.Cancel();

                try
                {
                    _listener.Stop();
                }
                catch (ObjectDisposedException)
                {
                }

                try
                {
                    _listenTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                }

                _listener.Close();
                _cancellationTokenSource.Dispose();
            }

            private async Task ListenAsync(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    HttpListenerContext context;

                    try
                    {
                        context = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    _ = Task.Run(() => HandleAsync(context, token), token);
                }
            }

            private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
            {
                var activeRequests = Interlocked.Increment(ref _currentConcurrentRequests);
                UpdateMaxConcurrentRequests(activeRequests);

                var requestNumber = Interlocked.Increment(ref _totalRequests);

                try
                {
                    if (IsCommandRequest(context.Request.RawUrl))
                        Interlocked.Increment(ref _commandRequests);
                    else if (IsDataRequest(context.Request.RawUrl))
                        Interlocked.Increment(ref _dataRequests);

                    if (ShouldAbort(requestNumber))
                    {
                        Interlocked.Increment(ref _abortedConnections);
                        context.Response.Abort();
                        return;
                    }

                    if (ShouldDelay(requestNumber))
                    {
                        Interlocked.Increment(ref _slowResponses);
                        await Task.Delay(_options.SlowDelay, token).ConfigureAwait(false);
                    }

                    await DrainRequestBodyAsync(context.Request).ConfigureAwait(false);

                    if (ShouldFail(requestNumber))
                    {
                        Interlocked.Increment(ref _failedResponses);
                        await WriteResponseAsync(context.Response, HttpStatusCode.InternalServerError, "\"temporary failure\"").ConfigureAwait(false);
                        return;
                    }

                    var body = IsCommandRequest(context.Request.RawUrl) ? "{}" : "\"ok\"";
                    await WriteResponseAsync(context.Response, HttpStatusCode.OK, body).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (HttpListenerException)
                {
                }
                catch (IOException)
                {
                }
                finally
                {
                    Interlocked.Decrement(ref _currentConcurrentRequests);
                }
            }

            private bool ShouldAbort(long requestNumber)
            {
                return CanBreak(requestNumber) && _options.AbortEvery > 0 && requestNumber % _options.AbortEvery == 0;
            }

            private bool ShouldDelay(long requestNumber)
            {
                return CanBreak(requestNumber) && _options.SlowEvery > 0 && requestNumber % _options.SlowEvery == 0;
            }

            private bool ShouldFail(long requestNumber)
            {
                return CanBreak(requestNumber) && _options.FailureEvery > 0 && requestNumber % _options.FailureEvery == 0;
            }

            private bool CanBreak(long requestNumber)
            {
                return requestNumber > _options.AlwaysSucceedFirstRequests;
            }

            private async Task DrainRequestBodyAsync(HttpListenerRequest request)
            {
                using (var memory = new MemoryStream())
                {
                    await request.InputStream.CopyToAsync(memory).ConfigureAwait(false);
                    Interlocked.Add(ref _requestBytes, memory.Length);
                }
            }

            private static async Task WriteResponseAsync(HttpListenerResponse response, HttpStatusCode statusCode, string body)
            {
                var bytes = Encoding.UTF8.GetBytes(body);

                response.StatusCode = (int)statusCode;
                response.ContentType = "application/json";
                response.ContentLength64 = bytes.Length;

                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                response.Close();
            }

            private static bool IsCommandRequest(string rawUrl)
            {
                return rawUrl != null && (rawUrl.EndsWith("/commands", StringComparison.OrdinalIgnoreCase)
                    || rawUrl.EndsWith("/addOrUpdate", StringComparison.OrdinalIgnoreCase));
            }

            private static bool IsDataRequest(string rawUrl)
            {
                if (rawUrl == null)
                    return false;

                return rawUrl.IndexOf("/api/sensors/", StringComparison.OrdinalIgnoreCase) >= 0
                    && !IsCommandRequest(rawUrl)
                    && !rawUrl.EndsWith("/testConnection", StringComparison.OrdinalIgnoreCase);
            }

            private void UpdateMaxConcurrentRequests(int activeRequests)
            {
                int currentMax;
                while (activeRequests > (currentMax = Volatile.Read(ref _maxConcurrentRequests)))
                {
                    if (Interlocked.CompareExchange(ref _maxConcurrentRequests, activeRequests, currentMax) == currentMax)
                        break;
                }
            }

            private static int GetFreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();

                try
                {
                    return ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                finally
                {
                    listener.Stop();
                }
            }
        }
    }
}
