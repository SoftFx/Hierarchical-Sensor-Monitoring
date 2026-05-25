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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorResourceLeakTests
    {
        private readonly ITestOutputHelper _output;

        public CollectorResourceLeakTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Collector_releases_resources_after_flaky_http_cycles()
        {
            if (!HttpListener.IsSupported)
                return;

            var results = await RunResourceLeakScenarioAsync(
                cycles: 5,
                sensorCount: 24,
                workerCount: 8,
                valuesPerWorker: 600,
                valuesPerPackage: 100,
                maxQueueSize: 20000).ConfigureAwait(false);

            AssertResourceTrends(results, maxManagedGrowthBytes: 64L * 1024 * 1024);
        }

        [ResourceLeakStressFact]
        public async Task Collector_releases_resources_after_long_flaky_http_cycles()
        {
            if (!HttpListener.IsSupported)
                return;

            var cycles = GetPositiveIntEnvironment("HSM_COLLECTOR_RESOURCE_LEAK_CYCLES", 30);

            var results = await RunResourceLeakScenarioAsync(
                cycles: cycles,
                sensorCount: 48,
                workerCount: Math.Max(8, Environment.ProcessorCount * 2),
                valuesPerWorker: 1000,
                valuesPerPackage: 150,
                maxQueueSize: 50000).ConfigureAwait(false);

            AssertResourceTrends(results, maxManagedGrowthBytes: 128L * 1024 * 1024);
        }

        [SuiteSoakFact]
        public async Task Resource_leak_suite_repeated_for_duration_stays_bounded()
        {
            if (!HttpListener.IsSupported)
                return;

            var duration = GetSuiteSoakDuration();
            var maxDuration = GetSuiteSoakMaxDuration();
            var before = SuiteSoakResourceSnapshot.Capture();
            var stopwatch = Stopwatch.StartNew();
            var cycles = 0;
            var resourceCycles = 0;
            long addValueCalls = 0;
            long totalRequests = 0;
            long dataRequests = 0;
            long commandRequests = 0;
            long abortedConnections = 0;
            long failedResponses = 0;
            long slowResponses = 0;
            long requestBytes = 0;
            var observedPorts = new HashSet<int>();

            while (stopwatch.Elapsed < duration)
            {
                cycles++;

                var results = await RunResourceLeakScenarioAsync(
                    cycles: 5,
                    sensorCount: 24,
                    workerCount: 8,
                    valuesPerWorker: 600,
                    valuesPerPackage: 100,
                    maxQueueSize: 20000).ConfigureAwait(false);

                AssertResourceTrends(results, maxManagedGrowthBytes: 64L * 1024 * 1024);
                resourceCycles += results.Count;
                addValueCalls += results.Count * 8L * 600L;
                totalRequests += results.Sum(r => r.Server.TotalRequests);
                dataRequests += results.Sum(r => r.Server.DataRequests);
                commandRequests += results.Sum(r => r.Server.CommandRequests);
                abortedConnections += results.Sum(r => r.Server.AbortedConnections);
                failedResponses += results.Sum(r => r.Server.FailedResponses);
                slowResponses += results.Sum(r => r.Server.SlowResponses);
                requestBytes += results.Sum(r => r.Server.RequestBytes);
                foreach (var port in results.Select(r => r.Port).Where(p => p > 0))
                    observedPorts.Add(port);
                AssertWithinSuiteSoakMax(stopwatch, maxDuration);
            }

            var after = SuiteSoakResourceSnapshot.Capture(observedPorts);
            SuiteSoakResourceSnapshot.WriteDelta(_output, "resourceLeakSuiteSoak", before, after);
            SuiteSoakResourceSnapshot.AssertNoCriticalGrowth(before, after);
            SuiteSoakResourceSnapshot.AssertNoEstablishedConnections(after);

            _output.WriteLine(
                "resourceLeakSuiteSoak; durationSeconds={0}; maxSeconds={1}; elapsedSeconds={2}; suiteCycles={3}; resourceCycles={4}; addValues={5}; requests={6}; commands={7}; data={8}; aborts={9}; failures={10}; slow={11}; bytes={12}",
                duration.TotalSeconds,
                maxDuration.TotalSeconds,
                stopwatch.Elapsed.TotalSeconds,
                cycles,
                resourceCycles,
                addValueCalls,
                totalRequests,
                commandRequests,
                dataRequests,
                abortedConnections,
                failedResponses,
                slowResponses,
                requestBytes);

            Assert.True(cycles > 0, "The resource leak suite soak should complete at least one suite cycle.");
            Assert.True(resourceCycles >= 5, "The resource leak suite soak should execute at least one full resource cycle set.");
        }

        private async Task<IReadOnlyList<ResourceCycleResult>> RunResourceLeakScenarioAsync(
            int cycles,
            int sensorCount,
            int workerCount,
            int valuesPerWorker,
            int valuesPerPackage,
            int maxQueueSize)
        {
            var observedPorts = new HashSet<int>();
            var results = new List<ResourceCycleResult>();

            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                var before = ResourceSnapshot.Capture(observedPorts);
                ResourceSnapshot after;
                ResourceHsmServer server = null;
                var port = 0;

                try
                {
                    server = ResourceHsmServer.Start(new ResourceHsmServerOptions
                    {
                        AlwaysSucceedFirstRequests = 3,
                        FailureEvery = 5,
                        AbortEvery = 9,
                        SlowEvery = 7,
                        SlowDelay = TimeSpan.FromMilliseconds(150)
                    });

                    observedPorts.Add(server.Port);
                    port = server.Port;

                    using (var collector = CreateCollector(server.Port, valuesPerPackage, maxQueueSize))
                    {
                        var sensors = CreateSensors(collector, sensorCount, cycle);

                        collector.Initialize(false);

                        await ProduceConcurrentLoadAsync(sensors, workerCount, valuesPerWorker).ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);

                        await DisposeWithinAsync(collector, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    server?.Dispose();
                }

                await WaitForNoEstablishedConnectionsAsync(observedPorts, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                after = ResourceSnapshot.Capture(observedPorts);

                var result = new ResourceCycleResult(cycle, port, before, after, server?.Stats ?? ResourceHsmServerStats.Empty);
                results.Add(result);

                WriteCycle(result);
            }

            WriteTotals(results);

            return results;
        }

        private static DataCollector CreateCollector(int port, int valuesPerPackage, int maxQueueSize)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "resource-leak-test-key",
                ClientName = "resource-leak-test-client",
                ComputerName = "resource-leak-test-host",
                Module = "resource-leak-test-module",
                ServerAddress = "http://127.0.0.1",
                Port = port,
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = valuesPerPackage,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(75),
                RequestTimeout = TimeSpan.FromSeconds(2),
                ExceptionDeduplicatorWindow = TimeSpan.FromSeconds(1),
                MaxDeduplicatedMessages = 1000
            });
        }

        private static IReadOnlyList<IInstantValueSensor<double>> CreateSensors(DataCollector collector, int count, int cycle)
        {
            return Enumerable.Range(0, count)
                .Select(i => collector.CreateDoubleSensor("resource-leak/cycle-" + cycle.ToString(CultureInfo.InvariantCulture) + "/double/" + i.ToString(CultureInfo.InvariantCulture)))
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
                        sensor.AddValue(worker * valuesPerWorker + i, SensorStatus.Ok, "resource-leak-load");
                    }
                }))
                .ToArray();

            return Task.WhenAll(tasks);
        }

        private static async Task DisposeWithinAsync(DataCollector collector, TimeSpan timeout)
        {
            var disposeTask = Task.Run(() => collector.Dispose());
            var completed = await Task.WhenAny(disposeTask, Task.Delay(timeout)).ConfigureAwait(false);

            Assert.True(completed == disposeTask, "Collector disposal should complete during resource leak checks.");

            await disposeTask.ConfigureAwait(false);
        }

        private static async Task WaitForNoEstablishedConnectionsAsync(IReadOnlyCollection<int> ports, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                var snapshot = ResourceSnapshot.Capture(ports);

                if (snapshot.TcpEstablished == 0)
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }

        private static void AssertResourceTrends(IReadOnlyList<ResourceCycleResult> results, long maxManagedGrowthBytes)
        {
            Assert.NotEmpty(results);

            var first = results[0].After;
            var last = results[results.Count - 1].After;

            Assert.All(results, result =>
            {
                Assert.True(result.After.TcpEstablished == 0, "No ESTABLISHED TCP connections to test ports should remain after dispose.");
                Assert.True(result.Server.TotalRequests > 0, "The fake server should receive requests.");
                Assert.True(result.Server.AbortedConnections > 0, "The fake server should exercise broken connections.");
                Assert.True(result.Server.FailedResponses > 0, "The fake server should exercise HTTP 500 responses.");
                Assert.True(result.Server.SlowResponses > 0, "The fake server should exercise slow responses.");
            });

            Assert.True(last.ManagedAfterFullGc - first.ManagedAfterFullGc < maxManagedGrowthBytes,
                "Managed memory after full GC should not grow without bounds across cycles.");

            if (first.HandleCount >= 0 && last.HandleCount >= 0)
                Assert.True(last.HandleCount - first.HandleCount < 250, "Process handle count should stay bounded.");

            Assert.True(last.ThreadCount - first.ThreadCount < 20, "Process thread count should stay bounded.");

            Assert.True(last.PrivateBytes - first.PrivateBytes < 256L * 1024 * 1024, "Private bytes should stay bounded.");
            Assert.True(last.WorkingSet - first.WorkingSet < 256L * 1024 * 1024, "Working set should stay bounded.");
            Assert.True(last.TcpTimeWait < 1000, "TIME_WAIT connections to test ports should stay bounded.");
        }

        private static int GetPositiveIntEnvironment(string variableName, int defaultValue)
        {
            var rawValue = Environment.GetEnvironmentVariable(variableName);

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;

            return defaultValue;
        }

        private static TimeSpan GetSuiteSoakDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(30);
        }

        private static TimeSpan GetSuiteSoakMaxDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromMinutes(2);
        }

        private static void AssertWithinSuiteSoakMax(Stopwatch stopwatch, TimeSpan maxDuration)
        {
            Assert.True(stopwatch.Elapsed <= maxDuration,
                $"Suite soak exceeded hard limit {maxDuration}. Target duration is soft, but exceeding the hard limit means the suite likely hung.");
        }

        private sealed class SuiteSoakFactAttribute : FactAttribute
        {
            public SuiteSoakFactAttribute()
            {
                if (!string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_SUITE_SOAK"), "1", StringComparison.Ordinal))
                    Skip = "Set HSM_COLLECTOR_RUN_SUITE_SOAK=1 to run repeated suite soak tests.";
            }
        }

        private void WriteCycle(ResourceCycleResult result)
        {
            _output.WriteLine(
                "cycle={0}; requests={1}; data={2}; commands={3}; aborts={4}; failures={5}; slow={6}; bytes={7}; handles={8}->{9}; threads={10}->{11}; managedGc={12}->{13}; private={14}->{15}; workingSet={16}->{17}; tcpEstablished={18}; tcpTimeWait={19}",
                result.Cycle,
                result.Server.TotalRequests,
                result.Server.DataRequests,
                result.Server.CommandRequests,
                result.Server.AbortedConnections,
                result.Server.FailedResponses,
                result.Server.SlowResponses,
                result.Server.RequestBytes,
                result.Before.HandleCount,
                result.After.HandleCount,
                result.Before.ThreadCount,
                result.After.ThreadCount,
                result.Before.ManagedAfterFullGc,
                result.After.ManagedAfterFullGc,
                result.Before.PrivateBytes,
                result.After.PrivateBytes,
                result.Before.WorkingSet,
                result.After.WorkingSet,
                result.After.TcpEstablished,
                result.After.TcpTimeWait);
        }

        private void WriteTotals(IReadOnlyList<ResourceCycleResult> results)
        {
            _output.WriteLine("totalCycles={0}", results.Count);
            _output.WriteLine("totalRequests={0}", results.Sum(r => r.Server.TotalRequests));
            _output.WriteLine("totalDataRequests={0}", results.Sum(r => r.Server.DataRequests));
            _output.WriteLine("totalCommandRequests={0}", results.Sum(r => r.Server.CommandRequests));
            _output.WriteLine("totalAbortedConnections={0}", results.Sum(r => r.Server.AbortedConnections));
            _output.WriteLine("totalFailedResponses={0}", results.Sum(r => r.Server.FailedResponses));
            _output.WriteLine("totalSlowResponses={0}", results.Sum(r => r.Server.SlowResponses));
            _output.WriteLine("totalRequestBytes={0}", results.Sum(r => r.Server.RequestBytes));
        }

        private sealed class ResourceLeakStressFactAttribute : FactAttribute
        {
            public ResourceLeakStressFactAttribute()
            {
                if (!string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_RESOURCE_LEAK_STRESS"), "1", StringComparison.Ordinal))
                    Skip = "Set HSM_COLLECTOR_RUN_RESOURCE_LEAK_STRESS=1 to run the long resource leak stress test.";
            }
        }

        private sealed class ResourceCycleResult
        {
            public ResourceCycleResult(int cycle, int port, ResourceSnapshot before, ResourceSnapshot after, ResourceHsmServerStats server)
            {
                Cycle = cycle;
                Port = port;
                Before = before;
                After = after;
                Server = server;
            }

            public int Cycle { get; }

            public int Port { get; }

            public ResourceSnapshot Before { get; }

            public ResourceSnapshot After { get; }

            public ResourceHsmServerStats Server { get; }
        }

        private sealed class ResourceSnapshot
        {
            private ResourceSnapshot(
                long managedAfterFullGc,
                long privateBytes,
                long workingSet,
                int handleCount,
                int threadCount,
                int tcpEstablished,
                int tcpTimeWait,
                int tcpTotal)
            {
                ManagedAfterFullGc = managedAfterFullGc;
                PrivateBytes = privateBytes;
                WorkingSet = workingSet;
                HandleCount = handleCount;
                ThreadCount = threadCount;
                TcpEstablished = tcpEstablished;
                TcpTimeWait = tcpTimeWait;
                TcpTotal = tcpTotal;
            }

            public long ManagedAfterFullGc { get; }

            public long PrivateBytes { get; }

            public long WorkingSet { get; }

            public int HandleCount { get; }

            public int ThreadCount { get; }

            public int TcpEstablished { get; }

            public int TcpTimeWait { get; }

            public int TcpTotal { get; }

            public static ResourceSnapshot Capture(IReadOnlyCollection<int> ports)
            {
                ForceFullGc();

                using (var process = Process.GetCurrentProcess())
                {
                    process.Refresh();

                    var tcpStates = TcpStateCounters.Capture(ports);

                    return new ResourceSnapshot(
                        GC.GetTotalMemory(forceFullCollection: false),
                        process.PrivateMemorySize64,
                        process.WorkingSet64,
                        GetHandleCount(process),
                        process.Threads.Count,
                        tcpStates.Established,
                        tcpStates.TimeWait,
                        tcpStates.Total);
                }
            }

            private static void ForceFullGc()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            private static int GetHandleCount(Process process)
            {
                try
                {
                    return process.HandleCount;
                }
                catch
                {
                    return -1;
                }
            }
        }

        private sealed class TcpStateCounters
        {
            private TcpStateCounters(int established, int timeWait, int total)
            {
                Established = established;
                TimeWait = timeWait;
                Total = total;
            }

            public int Established { get; }

            public int TimeWait { get; }

            public int Total { get; }

            public static TcpStateCounters Capture(IReadOnlyCollection<int> ports)
            {
                if (ports == null || ports.Count == 0)
                    return new TcpStateCounters(0, 0, 0);

                try
                {
                    var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                        .Where(c => ports.Contains(c.LocalEndPoint.Port) || ports.Contains(c.RemoteEndPoint.Port))
                        .ToArray();

                    return new TcpStateCounters(
                        connections.Count(c => c.State == TcpState.Established),
                        connections.Count(c => c.State == TcpState.TimeWait),
                        connections.Length);
                }
                catch
                {
                    return new TcpStateCounters(0, 0, 0);
                }
            }
        }

        private sealed class ResourceHsmServerOptions
        {
            public int AlwaysSucceedFirstRequests { get; set; }

            public int FailureEvery { get; set; }

            public int AbortEvery { get; set; }

            public int SlowEvery { get; set; }

            public TimeSpan SlowDelay { get; set; }
        }

        private sealed class ResourceHsmServerStats
        {
            public static readonly ResourceHsmServerStats Empty = new ResourceHsmServerStats();

            public long TotalRequests { get; set; }

            public long CommandRequests { get; set; }

            public long DataRequests { get; set; }

            public long FailedResponses { get; set; }

            public long AbortedConnections { get; set; }

            public long SlowResponses { get; set; }

            public long RequestBytes { get; set; }
        }

        private sealed class ResourceHsmServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
            private readonly ResourceHsmServerOptions _options;
            private readonly Task _listenTask;

            private long _totalRequests;
            private long _commandRequests;
            private long _dataRequests;
            private long _failedResponses;
            private long _abortedConnections;
            private long _slowResponses;
            private long _requestBytes;

            private ResourceHsmServer(ResourceHsmServerOptions options)
            {
                _options = options;
                Port = GetFreePort();

                _listener.Prefixes.Add("http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/");
                _listener.Start();
                _listenTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));
            }

            public int Port { get; }

            public ResourceHsmServerStats Stats => new ResourceHsmServerStats
            {
                TotalRequests = Interlocked.Read(ref _totalRequests),
                CommandRequests = Interlocked.Read(ref _commandRequests),
                DataRequests = Interlocked.Read(ref _dataRequests),
                FailedResponses = Interlocked.Read(ref _failedResponses),
                AbortedConnections = Interlocked.Read(ref _abortedConnections),
                SlowResponses = Interlocked.Read(ref _slowResponses),
                RequestBytes = Interlocked.Read(ref _requestBytes)
            };

            public static ResourceHsmServer Start(ResourceHsmServerOptions options)
            {
                return new ResourceHsmServer(options ?? new ResourceHsmServerOptions());
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
