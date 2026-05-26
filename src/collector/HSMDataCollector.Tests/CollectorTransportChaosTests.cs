using HSMDataCollector.Core;
using HSMDataCollector.Options;
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
    [Collection("Collector transport chaos")]
    public sealed class CollectorTransportChaosTests
    {
        private readonly ITestOutputHelper _output;
        private const int HighVolumeMixedValueCount = 100000;

        public CollectorTransportChaosTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Server_accepts_and_disconnects_repeatedly_does_not_leak_sockets()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "accept-drop",
                (request, number) => ChaosResponse.DropAfter(TimeSpan.FromMilliseconds(100)),
                sensorCount: 12,
                valuesPerSensor: 80).ConfigureAwait(false);

            Assert.True(stats.DroppedConnections > 0);
        }

        [Fact]
        public async Task Server_accepts_and_never_responds_dispose_cancels_requests()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "never-respond",
                (request, number) => ChaosResponse.NeverRespond(),
                sensorCount: 8,
                valuesPerSensor: 40,
                requestTimeout: TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);

            Assert.True(stats.HungConnections > 0);
        }

        [Fact]
        public async Task Server_socket_is_open_but_never_accepts_while_values_are_added_does_not_hang_or_leak()
        {
            using (var server = NoAcceptTcpServer.Start())
            {
                var before = TransportResourceSnapshot.Capture(new[] { server.Port });

                using (var collector = CreateCollector(
                    server.Port,
                    TimeSpan.FromMilliseconds(300),
                    1,
                    50000,
                    "no-accept-server"))
                {
                    collector.Initialize(false);

                    var flood = await ProduceMixedSensorLoadAsync(
                        collector,
                        "transport/no-accept",
                        HighVolumeMixedValueCount).ConfigureAwait(false);
                    var badServerCpuStart = CpuUsageSnapshot.Capture();
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    var badServerCpu = CpuUsageSnapshot.Capture().Subtract(badServerCpuStart);

                    await DisposeWithinAsync(collector, TimeSpan.FromSeconds(7)).ConfigureAwait(false);

                    WriteMixedFloodStats("no-accept-server", flood);
                    WriteCpuStats("no-accept-server", badServerCpu);
                    _output.WriteLine("scenario=no-accept-server; addValues={0}; port={1}", flood.TotalAddValueCalls, server.Port);

                    Assert.Equal(HighVolumeMixedValueCount, flood.TotalAddValueCalls);
                    Assert.True(flood.AllWritersUsed, "The high-volume no-accept scenario should send all configured sensor value types.");
                    AssertCpuBudget(badServerCpu, TimeSpan.FromSeconds(6), "no-accept server");
                }

                server.Dispose();

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                var after = TransportResourceSnapshot.Capture(new[] { server.Port });

                _output.WriteLine(
                    "scenario=no-accept-server-resources; handles={0}->{1}; threads={2}->{3}; managedGc={4}->{5}; private={6}->{7}; workingSet={8}->{9}; tcpEstablished={10}; tcpTimeWait={11}",
                    before.HandleCount,
                    after.HandleCount,
                    before.ThreadCount,
                    after.ThreadCount,
                    before.ManagedAfterFullGc,
                    after.ManagedAfterFullGc,
                    before.PrivateBytes,
                    after.PrivateBytes,
                    before.WorkingSet,
                    after.WorkingSet,
                    after.TcpEstablished,
                    after.TcpTimeWait);

                AssertHighVolumeBadServerResourcesStayBounded(before, after, "no-accept server");
            }
        }

        [Fact]
        public async Task Server_accepts_but_never_reads_body_or_responds_while_mixed_values_are_generated_stays_bounded()
        {
            using (var server = RawChaosServer.Start((request, number) => ChaosResponse.NeverRespond(TimeSpan.FromSeconds(2))))
            {
                var before = TransportResourceSnapshot.Capture(new[] { server.Port });

                using (var collector = CreateCollector(
                    server.Port,
                    TimeSpan.FromMilliseconds(300),
                    1,
                    50000,
                    "accept-no-body-server"))
                {
                    collector.Initialize(false);

                    var flood = await ProduceMixedSensorLoadAsync(
                        collector,
                        "transport/accept-no-body",
                        HighVolumeMixedValueCount).ConfigureAwait(false);
                    var badServerCpuStart = CpuUsageSnapshot.Capture();
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    var badServerCpu = CpuUsageSnapshot.Capture().Subtract(badServerCpuStart);

                    await DisposeWithinAsync(collector, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    WriteMixedFloodStats("accept-no-body-server", flood);
                    WriteCpuStats("accept-no-body-server", badServerCpu);
                    WriteStats("accept-no-body-server", server.Stats);

                    Assert.Equal(HighVolumeMixedValueCount, flood.TotalAddValueCalls);
                    Assert.True(flood.AllWritersUsed, "The high-volume accept/no-body scenario should send all configured sensor value types.");
                    AssertCpuBudget(badServerCpu, TimeSpan.FromSeconds(6), "accept/no-body server");
                }

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                var after = TransportResourceSnapshot.Capture(new[] { server.Port });

                _output.WriteLine(
                    "scenario=accept-no-body-server-resources; handles={0}->{1}; threads={2}->{3}; managedGc={4}->{5}; private={6}->{7}; workingSet={8}->{9}; tcpEstablished={10}; tcpTimeWait={11}",
                    before.HandleCount,
                    after.HandleCount,
                    before.ThreadCount,
                    after.ThreadCount,
                    before.ManagedAfterFullGc,
                    after.ManagedAfterFullGc,
                    before.PrivateBytes,
                    after.PrivateBytes,
                    before.WorkingSet,
                    after.WorkingSet,
                    after.TcpEstablished,
                    after.TcpTimeWait);

                Assert.True(server.Stats.HungConnections > 0, "The accept/no-body server should accept requests and keep them hanging.");
                AssertHighVolumeBadServerResourcesStayBounded(before, after, "accept/no-body server");
            }
        }

        [Fact]
        public async Task Server_accepts_and_replies_slowly_while_mixed_values_are_generated_stays_bounded()
        {
            using (var server = RawChaosServer.Start((request, number) => ChaosResponse.DelayedOk(TimeSpan.FromMilliseconds(400))))
            {
                var before = TransportResourceSnapshot.Capture(new[] { server.Port });

                using (var collector = CreateCollector(
                    server.Port,
                    TimeSpan.FromMilliseconds(900),
                    1,
                    50000,
                    "slow-reply-server"))
                {
                    collector.Initialize(false);

                    var flood = await ProduceMixedSensorLoadAsync(
                        collector,
                        "transport/slow-reply",
                        HighVolumeMixedValueCount).ConfigureAwait(false);
                    var badServerCpuStart = CpuUsageSnapshot.Capture();
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    var badServerCpu = CpuUsageSnapshot.Capture().Subtract(badServerCpuStart);

                    await DisposeWithinAsync(collector, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    WriteMixedFloodStats("slow-reply-server", flood);
                    WriteCpuStats("slow-reply-server", badServerCpu);
                    WriteStats("slow-reply-server", server.Stats);

                    Assert.Equal(HighVolumeMixedValueCount, flood.TotalAddValueCalls);
                    Assert.True(flood.AllWritersUsed, "The high-volume slow-reply scenario should send all configured sensor value types.");
                    AssertCpuBudget(badServerCpu, TimeSpan.FromSeconds(6), "slow-reply server");
                }

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                var after = TransportResourceSnapshot.Capture(new[] { server.Port });

                _output.WriteLine(
                    "scenario=slow-reply-server-resources; handles={0}->{1}; threads={2}->{3}; managedGc={4}->{5}; private={6}->{7}; workingSet={8}->{9}; tcpEstablished={10}; tcpTimeWait={11}",
                    before.HandleCount,
                    after.HandleCount,
                    before.ThreadCount,
                    after.ThreadCount,
                    before.ManagedAfterFullGc,
                    after.ManagedAfterFullGc,
                    before.PrivateBytes,
                    after.PrivateBytes,
                    before.WorkingSet,
                    after.WorkingSet,
                    after.TcpEstablished,
                    after.TcpTimeWait);

                Assert.True(server.Stats.OkResponses > 0, "The slow-reply server should eventually send delayed OK responses.");
                Assert.True(server.Stats.RequestBytes > 0, "The slow-reply server should receive request bodies.");
                AssertHighVolumeBadServerResourcesStayBounded(before, after, "slow-reply server");
            }
        }

        [Fact]
        public async Task Server_reads_request_body_slowly_does_not_block_dispose()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "slow-read",
                (request, number) => ChaosResponse.SlowReadBody(TimeSpan.FromMilliseconds(5)),
                sensorCount: 8,
                valuesPerSensor: 60).ConfigureAwait(false);

            Assert.True(stats.SlowReads > 0);
        }

        [Fact]
        public async Task Server_sends_headers_and_never_completes_body_does_not_hang_dispose()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "headers-no-body",
                (request, number) => ChaosResponse.HeadersOnly(),
                sensorCount: 8,
                valuesPerSensor: 50,
                requestTimeout: TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

            Assert.True(stats.HeaderOnlyResponses > 0);
        }

        [Fact]
        public async Task Server_returns_malformed_http_does_not_leak_connections()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "malformed-http",
                (request, number) => ChaosResponse.MalformedHttp(),
                sensorCount: 8,
                valuesPerSensor: 50).ConfigureAwait(false);

            Assert.True(stats.MalformedResponses > 0);
        }

        [Fact]
        public async Task Server_resets_connection_during_request_body_does_not_leak_connections()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "reset-during-body",
                (request, number) => ChaosResponse.ResetDuringBody(bytesToReadBeforeReset: 128),
                sensorCount: 8,
                valuesPerSensor: 80).ConfigureAwait(false);

            Assert.True(stats.ResetConnections > 0);
        }

        [Fact]
        public async Task Command_endpoint_hangs_data_endpoint_still_disposes()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "command-hangs",
                (request, number) => request.IsCommand ? ChaosResponse.NeverRespond() : ChaosResponse.Ok(),
                sensorCount: 8,
                valuesPerSensor: 80,
                requestTimeout: TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

            Assert.True(stats.CommandRequests > 0);
            Assert.True(stats.DataRequests > 0);
            Assert.True(stats.HungConnections > 0);
        }

        [Fact]
        public async Task Data_endpoint_hangs_command_endpoint_still_disposes()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "data-hangs",
                (request, number) => request.IsData ? ChaosResponse.NeverRespond() : ChaosResponse.Ok(),
                sensorCount: 8,
                valuesPerSensor: 80,
                requestTimeout: TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

            Assert.True(stats.CommandRequests > 0);
            Assert.True(stats.DataRequests > 0);
            Assert.True(stats.HungConnections > 0);
        }

        [Fact]
        public async Task Server_starts_after_connection_refused_collector_recovers()
        {
            var port = GetFreePort();
            RawChaosServer server = null;

            try
            {
                using (var collector = CreateCollector(port, TimeSpan.FromMilliseconds(700), 100, 10000, "late-server"))
                {
                    var sensor = collector.CreateDoubleSensor("transport/late-server/double");

                    collector.Initialize(false);

                    for (var i = 0; i < 200; i++)
                        sensor.AddValue(i);

                    await Task.Delay(TimeSpan.FromMilliseconds(800)).ConfigureAwait(false);

                    server = RawChaosServer.Start(port, (request, number) => ChaosResponse.Ok());

                    for (var i = 0; i < 200; i++)
                        sensor.AddValue(i);

                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                    await DisposeWithinAsync(collector, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            finally
            {
                server?.Dispose();
            }

            await AssertNoEstablishedConnectionsAsync(new[] { port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.NotNull(server);
            Assert.True(server.Stats.TotalRequests > 0);
        }

        [Fact]
        public async Task Many_collectors_to_one_flaky_server_do_not_exhaust_resources()
        {
            using (var server = RawChaosServer.Start((request, number) =>
                number % 3 == 0 ? ChaosResponse.DropAfter(TimeSpan.FromMilliseconds(50)) : ChaosResponse.Ok()))
            {
                var collectors = Enumerable.Range(0, 8)
                    .Select(i => CreateCollector(server.Port, TimeSpan.FromMilliseconds(800), 100, 15000, "many-one-" + i.ToString(CultureInfo.InvariantCulture)))
                    .ToArray();

                try
                {
                    foreach (var collector in collectors)
                    {
                        var sensor = collector.CreateDoubleSensor("transport/many-one/double");
                        collector.Initialize(false);

                        for (var i = 0; i < 200; i++)
                            sensor.AddValue(i);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                finally
                {
                    foreach (var collector in collectors)
                        collector.Dispose();
                }

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.True(server.Stats.TotalRequests > 0);
                Assert.True(server.Stats.DroppedConnections > 0);
            }
        }

        [Fact]
        public async Task Many_collectors_on_many_flaky_ports_do_not_leave_connections()
        {
            var servers = Enumerable.Range(0, 5)
                .Select(_ => RawChaosServer.Start((request, number) =>
                    number % 2 == 0 ? ChaosResponse.ResetDuringBody(64) : ChaosResponse.Ok()))
                .ToArray();

            var collectors = servers.Select((server, i) =>
                CreateCollector(server.Port, TimeSpan.FromMilliseconds(800), 100, 12000, "many-ports-" + i.ToString(CultureInfo.InvariantCulture))).ToArray();

            try
            {
                foreach (var collector in collectors)
                {
                    var sensor = collector.CreateDoubleSensor("transport/many-ports/double");
                    collector.Initialize(false);

                    for (var i = 0; i < 200; i++)
                        sensor.AddValue(i);
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                foreach (var collector in collectors)
                    collector.Dispose();

                foreach (var server in servers)
                    server.Dispose();
            }

            await AssertNoEstablishedConnectionsAsync(servers.Select(s => s.Port).ToArray(), TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            for (var i = 0; i < servers.Length; i++)
                WriteStats("many-ports-" + i.ToString(CultureInfo.InvariantCulture), servers[i].Stats);

            Assert.True(servers.Sum(s => s.Stats.TotalRequests) > 0);
            Assert.True(servers.Sum(s => s.Stats.ResetConnections) > 0);
        }

        [Fact]
        public async Task Huge_string_and_comment_payload_under_disconnects_stays_bounded()
        {
            var stats = await RunSingleCollectorScenarioAsync(
                "huge-string",
                (request, number) => number % 2 == 0 ? ChaosResponse.DropAfter(TimeSpan.FromMilliseconds(20)) : ChaosResponse.Ok(),
                createAndSend: collector =>
                {
                    var sensor = collector.CreateStringSensor("transport/huge-string/value");
                    var payload = new string('x', 64 * 1024);
                    var comment = new string('c', 64 * 1024);

                    for (var i = 0; i < 20; i++)
                        sensor.AddValue(payload, SensorStatus.Ok, comment);

                    return Task.CompletedTask;
                }).ConfigureAwait(false);

            Assert.True(stats.RequestBytes > 512 * 1024);
            Assert.True(stats.DroppedConnections > 0);
        }

        [Fact]
        public async Task File_sensor_flood_under_disconnects_releases_files_and_sockets()
        {
            var tempFiles = CreateTempFiles(count: 5, sizeBytes: 64 * 1024);

            try
            {
                var stats = await RunSingleCollectorScenarioAsync(
                    "file-flood",
                    (request, number) => number % 2 == 0 ? ChaosResponse.DropAfter(TimeSpan.FromMilliseconds(20)) : ChaosResponse.Ok(),
                    createAndSend: async collector =>
                    {
                        var sensor = collector.CreateFileSensor("transport/file-flood/file", "resource", "bin");

                        foreach (var file in tempFiles)
                            await sensor.SendFile(file, SensorStatus.Ok, "file-flood").ConfigureAwait(false);
                    },
                    waitAfterSend: TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                Assert.True(stats.DataRequests > 0 || stats.TotalRequests > 0);
                Assert.True(stats.DroppedConnections > 0);
            }
            finally
            {
                foreach (var file in tempFiles)
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
        }

        [Fact]
        public async Task Dispose_while_http_request_is_mid_flight_closes_connection()
        {
            var dataRequestAccepted = new TaskCompletionSource<bool>();

            using (var server = RawChaosServer.Start((request, number) =>
            {
                if (request.IsData)
                    dataRequestAccepted.TrySetResult(true);

                return request.IsData ? ChaosResponse.NeverRespond() : ChaosResponse.Ok();
            }))
            using (var collector = CreateCollector(server.Port, TimeSpan.FromSeconds(5), 50, 10000, "mid-flight"))
            {
                var sensor = collector.CreateDoubleSensor("transport/mid-flight/double");

                collector.Initialize(false);
                sensor.AddValue(1);

                var accepted = await Task.WhenAny(dataRequestAccepted.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
                Assert.True(accepted == dataRequestAccepted.Task, "The server should accept a data request before dispose.");

                await DisposeWithinAsync(collector, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task Constant_disconnect_retry_storm_stays_bounded()
        {
            var before = Process.GetCurrentProcess().TotalProcessorTime;

            var stats = await RunSingleCollectorScenarioAsync(
                "retry-storm",
                (request, number) => ChaosResponse.DropAfter(TimeSpan.Zero),
                sensorCount: 12,
                valuesPerSensor: 200,
                requestTimeout: TimeSpan.FromMilliseconds(500),
                waitAfterSend: TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            var cpu = Process.GetCurrentProcess().TotalProcessorTime - before;

            _output.WriteLine("retryStormCpuMs={0}", cpu.TotalMilliseconds);
            _output.WriteLine("retryStormRequests={0}", stats.TotalRequests);

            Assert.True(stats.DroppedConnections > 0);
            Assert.True(stats.TotalRequests < 500, "Retry storm should stay bounded during this short scenario.");
            Assert.True(cpu < TimeSpan.FromSeconds(15), "Retry storm should not burn excessive CPU in the test process.");
        }

        [TransportChaosSoakFact]
        public async Task Mixed_transport_chaos_suite_repeated_on_one_server_stays_bounded()
        {
            var duration = GetTransportSoakDuration();
            var maxDuration = GetTransportSoakMaxDuration();
            var collectorsPerPhase = GetPositiveIntEnvironment("HSM_COLLECTOR_TRANSPORT_SOAK_COLLECTORS", 8);
            var valuesPerCollector = GetPositiveIntEnvironment("HSM_COLLECTOR_TRANSPORT_SOAK_VALUES", 250);
            var minConnections = GetPositiveIntEnvironment("HSM_COLLECTOR_TRANSPORT_SOAK_MIN_CONNECTIONS", 200);
            var currentScenario = (int)TransportSoakScenario.AcceptDrop;
            var scenarios = new[]
            {
                TransportSoakScenario.AcceptDrop,
                TransportSoakScenario.NeverRespond,
                TransportSoakScenario.SlowReadBody,
                TransportSoakScenario.HeadersOnly,
                TransportSoakScenario.MalformedHttp,
                TransportSoakScenario.ResetDuringBody
            };
            var before = TransportResourceSnapshot.Capture(Array.Empty<int>());
            var port = 0;
            ChaosServerStats stats;
            int cycles;
            var phaseResults = new List<TransportSoakPhaseResult>();
            TransportResourceSnapshot trendBaseline = null;
            TransportResourceSnapshot trendLast = null;
            var stopwatch = Stopwatch.StartNew();
            long addValueCalls = 0;

            using (var server = RawChaosServer.Start((request, number) =>
                CreateSoakResponse((TransportSoakScenario)Volatile.Read(ref currentScenario), request, number)))
            {
                port = server.Port;
                var deadline = DateTime.UtcNow + duration;
                cycles = 0;

                while (DateTime.UtcNow < deadline)
                {
                    cycles++;

                    foreach (var scenario in scenarios)
                    {
                        if (DateTime.UtcNow >= deadline)
                            break;

                        Volatile.Write(ref currentScenario, (int)scenario);

                        var phaseBefore = server.Stats;

                        await RunTransportSoakPhaseAsync(
                            server,
                            phaseBefore.AcceptedConnections,
                            scenario,
                            cycles,
                            collectorsPerPhase,
                            valuesPerCollector).ConfigureAwait(false);
                        addValueCalls += collectorsPerPhase * (long)valuesPerCollector;

                        await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                        var phaseAfter = server.Stats;
                        var result = TransportSoakPhaseResult.FromDelta(cycles, scenario, phaseBefore, phaseAfter);
                        phaseResults.Add(result);
                        WriteSoakPhase(result);

                        var trendSnapshot = TransportResourceSnapshot.Capture(new[] { server.Port });
                        if (trendBaseline == null && phaseResults.Count >= scenarios.Length)
                            trendBaseline = trendSnapshot;

                        trendLast = trendSnapshot;
                        AssertWithinTransportSoakMax(stopwatch, maxDuration);
                    }
                }

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                stats = server.Stats;
            }

            await WaitForTransportSoakSettleAsync(new[] { port }, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            var after = TransportResourceSnapshot.Capture(new[] { port });

            _output.WriteLine(
                "transportSoakTotals; durationSeconds={0}; maxSeconds={1}; elapsedSeconds={2}; cycles={3}; addValues={4}; accepted={5}; requests={6}; commands={7}; data={8}; ok={9}; dropped={10}; hung={11}; slowReads={12}; headerOnly={13}; malformed={14}; resets={15}; bytes={16}; tcpEstablished={17}; tcpTimeWait={18}; handles={19}->{20}; threads={21}->{22}; managedGc={23}->{24}; private={25}->{26}; workingSet={27}->{28}",
                duration.TotalSeconds,
                maxDuration.TotalSeconds,
                stopwatch.Elapsed.TotalSeconds,
                cycles,
                addValueCalls,
                stats.AcceptedConnections,
                stats.TotalRequests,
                stats.CommandRequests,
                stats.DataRequests,
                stats.OkResponses,
                stats.DroppedConnections,
                stats.HungConnections,
                stats.SlowReads,
                stats.HeaderOnlyResponses,
                stats.MalformedResponses,
                stats.ResetConnections,
                stats.RequestBytes,
                after.TcpEstablished,
                after.TcpTimeWait,
                before.HandleCount,
                after.HandleCount,
                before.ThreadCount,
                after.ThreadCount,
                before.ManagedAfterFullGc,
                after.ManagedAfterFullGc,
                before.PrivateBytes,
                after.PrivateBytes,
                before.WorkingSet,
                after.WorkingSet);

            Assert.True(cycles > 0, "The repeated transport suite should run at least one full or partial cycle.");
            Assert.True(phaseResults.Count > 0, "The repeated transport suite should record phase results.");
            Assert.NotNull(trendBaseline);
            Assert.NotNull(trendLast);

            _output.WriteLine(
                "transportSoakTrendAfterWarmup; handles={0}->{1}; threads={2}->{3}; managedGc={4}->{5}; private={6}->{7}; workingSet={8}->{9}",
                trendBaseline.HandleCount,
                trendLast.HandleCount,
                trendBaseline.ThreadCount,
                trendLast.ThreadCount,
                trendBaseline.ManagedAfterFullGc,
                trendLast.ManagedAfterFullGc,
                trendBaseline.PrivateBytes,
                trendLast.PrivateBytes,
                trendBaseline.WorkingSet,
                trendLast.WorkingSet);

            Assert.True(stats.AcceptedConnections >= minConnections, "The transport soak should create enough real TCP connections to make socket leaks visible.");
            Assert.True(stats.DroppedConnections > 0, "Accept/drop scenario should run.");
            Assert.True(stats.HungConnections > 0, "Never-respond scenario should run.");
            Assert.True(stats.SlowReads > 0, "Slow request-body read scenario should run.");
            Assert.True(stats.HeaderOnlyResponses > 0, "Headers-only scenario should run.");
            Assert.True(stats.MalformedResponses > 0, "Malformed HTTP scenario should run.");
            Assert.True(stats.ResetConnections > 0, "Reset-during-body scenario should run.");
            Assert.True(after.TcpEstablished == 0, "No ESTABLISHED TCP connections to the single chaos server should remain after the repeated suite.");
            Assert.True(after.TcpTimeWait < 1000, "TIME_WAIT connections to the single chaos server should stay bounded.");
            Assert.True(trendLast.ThreadCount - trendBaseline.ThreadCount < 40, "Thread count should stay bounded between post-GC transport soak cycles after warm-up.");
            Assert.True(trendLast.ManagedAfterFullGc - trendBaseline.ManagedAfterFullGc < 128L * 1024 * 1024, "Managed memory after full GC should stay bounded between transport soak cycles after warm-up.");

            if (trendBaseline.HandleCount >= 0 && trendLast.HandleCount >= 0)
                Assert.True(trendLast.HandleCount - trendBaseline.HandleCount < 250, "Process handle count should stay bounded between transport soak cycles after warm-up.");

            Assert.True(trendLast.PrivateBytes - trendBaseline.PrivateBytes < 256L * 1024 * 1024, "Private bytes should stay bounded between transport soak cycles after warm-up.");
            Assert.True(trendLast.WorkingSet - trendBaseline.WorkingSet < 256L * 1024 * 1024, "Working set should stay bounded between transport soak cycles after warm-up.");
        }

        private async Task<ChaosServerStats> RunSingleCollectorScenarioAsync(
            string name,
            Func<ChaosRequest, long, ChaosResponse> behavior,
            int sensorCount = 8,
            int valuesPerSensor = 50,
            TimeSpan? requestTimeout = null,
            TimeSpan? waitAfterSend = null,
            Func<DataCollector, Task> createAndSend = null)
        {
            using (var server = RawChaosServer.Start(behavior))
            using (var collector = CreateCollector(server.Port, requestTimeout ?? TimeSpan.FromMilliseconds(800), 100, 20000, name))
            {
                collector.Initialize(false);

                if (createAndSend != null)
                {
                    await createAndSend(collector).ConfigureAwait(false);
                }
                else
                {
                    var sensors = Enumerable.Range(0, sensorCount)
                        .Select(i => collector.CreateDoubleSensor("transport/" + name + "/double/" + i.ToString(CultureInfo.InvariantCulture)))
                        .ToArray();

                    foreach (var sensor in sensors)
                    {
                        for (var i = 0; i < valuesPerSensor; i++)
                            sensor.AddValue(i);
                    }
                }

                await Task.Delay(waitAfterSend ?? TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);

                await DisposeWithinAsync(collector, TimeSpan.FromSeconds(7)).ConfigureAwait(false);

                await AssertNoEstablishedConnectionsAsync(new[] { server.Port }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                WriteStats(name, server.Stats);

                return server.Stats;
            }
        }

        private static async Task RunTransportSoakPhaseAsync(
            RawChaosServer server,
            long acceptedConnectionsBeforePhase,
            TransportSoakScenario scenario,
            int cycle,
            int collectorsPerPhase,
            int valuesPerCollector)
        {
            var collectors = new List<DataCollector>();
            var sensors = new List<IInstantValueSensor<double>>();

            try
            {
                for (var collectorIndex = 0; collectorIndex < collectorsPerPhase; collectorIndex++)
                {
                    var collector = CreateCollector(
                        server.Port,
                        TimeSpan.FromMilliseconds(200),
                        1,
                        Math.Max(20000, collectorsPerPhase * valuesPerCollector * 2),
                        "soak-" + scenario + "-" + cycle.ToString(CultureInfo.InvariantCulture) + "-" + collectorIndex.ToString(CultureInfo.InvariantCulture));

                    collectors.Add(collector);
                    collector.Initialize(false);
                    sensors.Add(collector.CreateDoubleSensor(
                        "transport/soak/" + scenario + "/" + cycle.ToString(CultureInfo.InvariantCulture) + "/" + collectorIndex.ToString(CultureInfo.InvariantCulture)));
                }

                var producers = sensors.Select((sensor, sensorIndex) => Task.Run(() =>
                {
                    for (var value = 0; value < valuesPerCollector; value++)
                        sensor.AddValue(sensorIndex * valuesPerCollector + value);
                })).ToArray();

                await Task.WhenAll(producers).ConfigureAwait(false);
                await WaitForAcceptedConnectionsAsync(
                    server,
                    acceptedConnectionsBeforePhase,
                    Math.Max(1, Math.Min(collectorsPerPhase, 4)),
                    TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                foreach (var collector in collectors)
                    await DisposeWithinAsync(collector, TimeSpan.FromSeconds(7)).ConfigureAwait(false);
            }
        }

        private static ChaosResponse CreateSoakResponse(TransportSoakScenario scenario, ChaosRequest request, long number)
        {
            switch (scenario)
            {
                case TransportSoakScenario.AcceptDrop:
                    return ChaosResponse.DropAfter(TimeSpan.Zero);

                case TransportSoakScenario.NeverRespond:
                    return ChaosResponse.NeverRespond(TimeSpan.FromMilliseconds(500));

                case TransportSoakScenario.SlowReadBody:
                    return ChaosResponse.SlowReadBody(TimeSpan.FromMilliseconds(1));

                case TransportSoakScenario.HeadersOnly:
                    return ChaosResponse.HeadersOnly(TimeSpan.FromMilliseconds(500));

                case TransportSoakScenario.MalformedHttp:
                    return ChaosResponse.MalformedHttp();

                case TransportSoakScenario.ResetDuringBody:
                    return ChaosResponse.ResetDuringBody(bytesToReadBeforeReset: 32);

                default:
                    return ChaosResponse.Ok();
            }
        }

        private static DataCollector CreateCollector(int port, TimeSpan requestTimeout, int valuesPerPackage, int maxQueueSize, string module)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "transport-chaos-key",
                ClientName = "transport-chaos-client",
                ComputerName = "transport-chaos-host",
                Module = module,
                ServerAddress = "http://127.0.0.1",
                Port = port,
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = valuesPerPackage,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = requestTimeout,
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(200),
                MaxDeduplicatedMessages = 500
            });
        }

        private static async Task<MixedValueFloodResult> ProduceMixedSensorLoadAsync(DataCollector collector, string pathPrefix, int totalValues)
        {
            var writers = CreateMixedValueWriters(collector, pathPrefix);
            var counts = new long[writers.Count];
            var workerCount = 8;
            var valuesPerWorker = totalValues / workerCount;
            var remainder = totalValues % workerCount;

            var producers = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(() =>
                {
                    var start = worker * valuesPerWorker + Math.Min(worker, remainder);
                    var count = valuesPerWorker + (worker < remainder ? 1 : 0);

                    for (var offset = 0; offset < count; offset++)
                    {
                        var value = start + offset;
                        var writerIndex = value % writers.Count;
                        writers[writerIndex].AddValue(value);
                        Interlocked.Increment(ref counts[writerIndex]);
                    }
                }))
                .ToArray();

            await Task.WhenAll(producers).ConfigureAwait(false);

            return new MixedValueFloodResult(writers.Select(w => w.Name).ToArray(), counts);
        }

        private static IReadOnlyList<MixedValueWriter> CreateMixedValueWriters(DataCollector collector, string pathPrefix)
        {
            var boolSensor = collector.CreateBoolSensor(pathPrefix + "/instant/bool");
            var intSensor = collector.CreateIntSensor(pathPrefix + "/instant/int");
            var doubleSensor = collector.CreateDoubleSensor(pathPrefix + "/instant/double");
            var stringSensor = collector.CreateStringSensor(pathPrefix + "/instant/string");
            var versionSensor = collector.CreateVersionSensor(pathPrefix + "/instant/version");
            var timeSensor = collector.CreateTimeSensor(pathPrefix + "/instant/time");
            var enumSensor = collector.CreateEnumSensor(pathPrefix + "/instant/enum");
            var lastBoolSensor = collector.CreateLastValueBoolSensor(pathPrefix + "/last/bool", false);
            var lastIntSensor = collector.CreateLastValueIntSensor(pathPrefix + "/last/int", 0);
            var lastDoubleSensor = collector.CreateLastValueDoubleSensor(pathPrefix + "/last/double", 0);
            var lastStringSensor = collector.CreateLastValueStringSensor(pathPrefix + "/last/string", string.Empty);
            var lastVersionSensor = collector.CreateLastValueVersionSensor(pathPrefix + "/last/version", new Version(1, 0));
            var lastTimeSensor = collector.CreateLastValueTimeSpanSensor(pathPrefix + "/last/time", TimeSpan.Zero);
            var intBarSensor = collector.CreateIntBarSensor(pathPrefix + "/bar/int", barPeriod: 1000, postPeriod: 200);
            var doubleBarSensor = collector.CreateDoubleBarSensor(pathPrefix + "/bar/double", barPeriod: 1000, postPeriod: 200);
            var rateSensor = collector.CreateRateSensor(pathPrefix + "/rate", new RateSensorOptions
            {
                PostDataPeriod = TimeSpan.FromMilliseconds(200)
            });
            var fileSensor = collector.CreateFileSensor(pathPrefix + "/file", "mixed", "txt");

            return new[]
            {
                new MixedValueWriter("instantBool", value => boolSensor.AddValue((value & 1) == 0, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("instantInt", value => intSensor.AddValue(value, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("instantDouble", value => doubleSensor.AddValue(value + 0.25, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("instantString", value => stringSensor.AddValue("value-" + value.ToString(CultureInfo.InvariantCulture), SensorStatus.Ok, "mixed")),
                new MixedValueWriter("instantVersion", value => versionSensor.AddValue(CreateVersionValue(value), SensorStatus.Ok, "mixed")),
                new MixedValueWriter("instantTime", value => timeSensor.AddValue(TimeSpan.FromMilliseconds(value), SensorStatus.Ok, "mixed")),
                new MixedValueWriter("instantEnum", value => enumSensor.AddValue(value % 4, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("lastBool", value => lastBoolSensor.AddValue((value & 1) == 0, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("lastInt", value => lastIntSensor.AddValue(value, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("lastDouble", value => lastDoubleSensor.AddValue(value + 0.5, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("lastString", value => lastStringSensor.AddValue("last-" + value.ToString(CultureInfo.InvariantCulture), SensorStatus.Ok, "mixed")),
                new MixedValueWriter("lastVersion", value => lastVersionSensor.AddValue(CreateVersionValue(value), SensorStatus.Ok, "mixed")),
                new MixedValueWriter("lastTime", value => lastTimeSensor.AddValue(TimeSpan.FromTicks(value), SensorStatus.Ok, "mixed")),
                new MixedValueWriter("barInt", value => intBarSensor.AddValue(value)),
                new MixedValueWriter("barDouble", value => doubleBarSensor.AddValue(value + 0.75)),
                new MixedValueWriter("rate", value => rateSensor.AddValue(value + 1.0, SensorStatus.Ok, "mixed")),
                new MixedValueWriter("file", value => fileSensor.AddValue("file-payload-" + value.ToString(CultureInfo.InvariantCulture), SensorStatus.Ok, "mixed"))
            };
        }

        private static Version CreateVersionValue(int value)
        {
            return new Version(1, value % 100, value % 1000);
        }

        private static void AssertHighVolumeBadServerResourcesStayBounded(TransportResourceSnapshot before, TransportResourceSnapshot after, string scenario)
        {
            Assert.True(after.TcpEstablished == 0, "No ESTABLISHED TCP connections should remain when the " + scenario + " is unhealthy.");
            Assert.True(after.ThreadCount - before.ThreadCount < 80, "Thread count should stay bounded when the " + scenario + " is unhealthy.");
            Assert.True(after.ManagedAfterFullGc - before.ManagedAfterFullGc < 128L * 1024 * 1024, "Managed memory should stay bounded when the " + scenario + " is unhealthy.");
            Assert.True(after.PrivateBytes - before.PrivateBytes < 256L * 1024 * 1024, "Private bytes should stay bounded when the " + scenario + " is unhealthy.");
            Assert.True(after.WorkingSet - before.WorkingSet < 256L * 1024 * 1024, "Working set should stay bounded when the " + scenario + " is unhealthy.");

            if (before.HandleCount >= 0 && after.HandleCount >= 0)
                Assert.True(after.HandleCount - before.HandleCount < 500, "Handle count should stay bounded when the " + scenario + " is unhealthy.");
        }

        private static void AssertCpuBudget(CpuUsageSnapshot cpu, TimeSpan maxCpu, string scenario)
        {
            Assert.True(cpu.Cpu <= maxCpu,
                $"{scenario} should not burn excessive CPU while the server is unhealthy. CPU={cpu.Cpu}, wall={cpu.Wall}.");
        }

        private static int GetPositiveIntEnvironment(string variableName, int defaultValue)
        {
            var rawValue = Environment.GetEnvironmentVariable(variableName);

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;

            return defaultValue;
        }

        private static TimeSpan GetTransportSoakDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_TRANSPORT_SOAK_SECONDS")
                ?? Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(30);
        }

        private static TimeSpan GetTransportSoakMaxDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_TRANSPORT_SOAK_MAX_SECONDS")
                ?? Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromMinutes(2);
        }

        private static void AssertWithinTransportSoakMax(Stopwatch stopwatch, TimeSpan maxDuration)
        {
            Assert.True(stopwatch.Elapsed <= maxDuration,
                $"Transport soak exceeded hard limit {maxDuration}. Target duration is soft, but exceeding the hard limit means the suite likely hung.");
        }

        private static async Task DisposeWithinAsync(DataCollector collector, TimeSpan timeout)
        {
            var disposeTask = Task.Run(() => collector.Dispose());
            var completed = await Task.WhenAny(disposeTask, Task.Delay(timeout)).ConfigureAwait(false);

            Assert.True(completed == disposeTask, "Collector disposal should finish under transport chaos.");

            await disposeTask.ConfigureAwait(false);
        }

        private static async Task AssertNoEstablishedConnectionsAsync(IReadOnlyCollection<int> ports, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            TcpCounts counts;

            do
            {
                counts = TcpCounts.Capture(ports);

                if (counts.Established == 0)
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            Assert.True(counts.Established == 0, "No ESTABLISHED TCP connections to chaos server ports should remain after dispose.");
        }

        private static async Task WaitForAcceptedConnectionsAsync(RawChaosServer server, long before, int minAcceptedDelta, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (server.Stats.AcceptedConnections - before >= minAcceptedDelta)
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }
        }

        private static async Task WaitForTransportSoakSettleAsync(IReadOnlyCollection<int> ports, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                var counts = TcpCounts.Capture(ports);

                if (counts.Established == 0)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        private void WriteStats(string scenario, ChaosServerStats stats)
        {
            _output.WriteLine(
                "scenario={0}; requests={1}; commands={2}; data={3}; ok={4}; dropped={5}; hung={6}; slowReads={7}; headerOnly={8}; malformed={9}; resets={10}; bytes={11}",
                scenario,
                stats.TotalRequests,
                stats.CommandRequests,
                stats.DataRequests,
                stats.OkResponses,
                stats.DroppedConnections,
                stats.HungConnections,
                stats.SlowReads,
                stats.HeaderOnlyResponses,
                stats.MalformedResponses,
                stats.ResetConnections,
                stats.RequestBytes);
        }

        private void WriteMixedFloodStats(string scenario, MixedValueFloodResult flood)
        {
            _output.WriteLine(
                "scenario={0}-mixed-values; totalAddValues={1}; types={2}",
                scenario,
                flood.TotalAddValueCalls,
                flood.ToSummary());
        }

        private void WriteCpuStats(string scenario, CpuUsageSnapshot cpu)
        {
            _output.WriteLine(
                "scenario={0}-cpu; wallMs={1}; cpuMs={2}; cpuCores={3:F3}",
                scenario,
                cpu.Wall.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                cpu.Cpu.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                cpu.CpuCores);
        }

        private void WriteSoakPhase(TransportSoakPhaseResult result)
        {
            _output.WriteLine(
                "transportSoakPhase; cycle={0}; scenario={1}; accepted={2}; requests={3}; dropped={4}; hung={5}; slowReads={6}; headerOnly={7}; malformed={8}; resets={9}; bytes={10}",
                result.Cycle,
                result.Scenario,
                result.AcceptedConnections,
                result.TotalRequests,
                result.DroppedConnections,
                result.HungConnections,
                result.SlowReads,
                result.HeaderOnlyResponses,
                result.MalformedResponses,
                result.ResetConnections,
                result.RequestBytes);
        }

        private static string[] CreateTempFiles(int count, int sizeBytes)
        {
            return Enumerable.Range(0, count)
                .Select(i =>
                {
                    var path = Path.Combine(Path.GetTempPath(), "hsm-transport-chaos-" + Guid.NewGuid().ToString("N") + "-" + i.ToString(CultureInfo.InvariantCulture) + ".bin");
                    File.WriteAllBytes(path, Enumerable.Range(0, sizeBytes).Select(v => (byte)(v % 251)).ToArray());
                    return path;
                })
                .ToArray();
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

        private sealed class TransportChaosSoakFactAttribute : FactAttribute
        {
            public TransportChaosSoakFactAttribute()
            {
                var enabled = string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_TRANSPORT_SOAK"), "1", StringComparison.Ordinal)
                    || string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_SUITE_SOAK"), "1", StringComparison.Ordinal);

                if (!enabled)
                    Skip = "Set HSM_COLLECTOR_RUN_TRANSPORT_SOAK=1 to run the repeated single-server transport soak test.";
            }
        }

        private enum TransportSoakScenario
        {
            AcceptDrop,
            NeverRespond,
            SlowReadBody,
            HeadersOnly,
            MalformedHttp,
            ResetDuringBody
        }

        private sealed class TransportSoakPhaseResult
        {
            private TransportSoakPhaseResult(
                int cycle,
                TransportSoakScenario scenario,
                long acceptedConnections,
                long totalRequests,
                long droppedConnections,
                long hungConnections,
                long slowReads,
                long headerOnlyResponses,
                long malformedResponses,
                long resetConnections,
                long requestBytes)
            {
                Cycle = cycle;
                Scenario = scenario;
                AcceptedConnections = acceptedConnections;
                TotalRequests = totalRequests;
                DroppedConnections = droppedConnections;
                HungConnections = hungConnections;
                SlowReads = slowReads;
                HeaderOnlyResponses = headerOnlyResponses;
                MalformedResponses = malformedResponses;
                ResetConnections = resetConnections;
                RequestBytes = requestBytes;
            }

            public int Cycle { get; }

            public TransportSoakScenario Scenario { get; }

            public long AcceptedConnections { get; }

            public long TotalRequests { get; }

            public long DroppedConnections { get; }

            public long HungConnections { get; }

            public long SlowReads { get; }

            public long HeaderOnlyResponses { get; }

            public long MalformedResponses { get; }

            public long ResetConnections { get; }

            public long RequestBytes { get; }

            public static TransportSoakPhaseResult FromDelta(int cycle, TransportSoakScenario scenario, ChaosServerStats before, ChaosServerStats after)
            {
                return new TransportSoakPhaseResult(
                    cycle,
                    scenario,
                    after.AcceptedConnections - before.AcceptedConnections,
                    after.TotalRequests - before.TotalRequests,
                    after.DroppedConnections - before.DroppedConnections,
                    after.HungConnections - before.HungConnections,
                    after.SlowReads - before.SlowReads,
                    after.HeaderOnlyResponses - before.HeaderOnlyResponses,
                    after.MalformedResponses - before.MalformedResponses,
                    after.ResetConnections - before.ResetConnections,
                    after.RequestBytes - before.RequestBytes);
            }
        }

        private sealed class TransportResourceSnapshot
        {
            private TransportResourceSnapshot(
                long managedAfterFullGc,
                long privateBytes,
                long workingSet,
                int handleCount,
                int threadCount,
                int tcpEstablished,
                int tcpTimeWait)
            {
                ManagedAfterFullGc = managedAfterFullGc;
                PrivateBytes = privateBytes;
                WorkingSet = workingSet;
                HandleCount = handleCount;
                ThreadCount = threadCount;
                TcpEstablished = tcpEstablished;
                TcpTimeWait = tcpTimeWait;
            }

            public long ManagedAfterFullGc { get; }

            public long PrivateBytes { get; }

            public long WorkingSet { get; }

            public int HandleCount { get; }

            public int ThreadCount { get; }

            public int TcpEstablished { get; }

            public int TcpTimeWait { get; }

            public static TransportResourceSnapshot Capture(IReadOnlyCollection<int> ports)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                using (var process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    var tcpCounts = TcpCounts.Capture(ports);

                    return new TransportResourceSnapshot(
                        GC.GetTotalMemory(forceFullCollection: false),
                        process.PrivateMemorySize64,
                        process.WorkingSet64,
                        GetHandleCount(process),
                        process.Threads.Count,
                        tcpCounts.Established,
                        tcpCounts.TimeWait);
                }
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

        private sealed class MixedValueWriter
        {
            public MixedValueWriter(string name, Action<int> addValue)
            {
                Name = name;
                AddValue = addValue;
            }

            public string Name { get; }

            public Action<int> AddValue { get; }
        }

        private sealed class MixedValueFloodResult
        {
            public MixedValueFloodResult(IReadOnlyList<string> names, IReadOnlyList<long> counts)
            {
                Names = names;
                Counts = counts;
            }

            public IReadOnlyList<string> Names { get; }

            public IReadOnlyList<long> Counts { get; }

            public long TotalAddValueCalls => Counts.Sum();

            public bool AllWritersUsed => Counts.All(count => count > 0);

            public string ToSummary()
            {
                return string.Join(
                    ",",
                    Names.Select((name, index) => name + "=" + Counts[index].ToString(CultureInfo.InvariantCulture)));
            }
        }

        private sealed class CpuUsageSnapshot
        {
            private CpuUsageSnapshot(TimeSpan cpu, long timestamp, TimeSpan wall)
            {
                Cpu = cpu;
                Timestamp = timestamp;
                Wall = wall;
            }

            public TimeSpan Cpu { get; }

            private long Timestamp { get; }

            public TimeSpan Wall { get; }

            public double CpuCores => Wall.TotalMilliseconds <= 0
                ? 0
                : Cpu.TotalMilliseconds / Wall.TotalMilliseconds;

            public static CpuUsageSnapshot Capture()
            {
                using (var process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    return new CpuUsageSnapshot(process.TotalProcessorTime, Stopwatch.GetTimestamp(), TimeSpan.Zero);
                }
            }

            public CpuUsageSnapshot Subtract(CpuUsageSnapshot before)
            {
                var cpu = Cpu - before.Cpu;
                var wall = StopwatchTicksToTimeSpan(Timestamp - before.Timestamp);

                return new CpuUsageSnapshot(cpu, 0, wall);
            }

            private static TimeSpan StopwatchTicksToTimeSpan(long stopwatchTicks)
            {
                return TimeSpan.FromSeconds(stopwatchTicks / (double)Stopwatch.Frequency);
            }
        }

        private sealed class TcpCounts
        {
            private TcpCounts(int established, int timeWait, int total)
            {
                Established = established;
                TimeWait = timeWait;
                Total = total;
            }

            public int Established { get; }

            public int TimeWait { get; }

            public int Total { get; }

            public static TcpCounts Capture(IReadOnlyCollection<int> ports)
            {
                try
                {
                    var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                        .Where(c => ports.Contains(c.LocalEndPoint.Port) || ports.Contains(c.RemoteEndPoint.Port))
                        .ToArray();

                    return new TcpCounts(
                        connections.Count(c => c.State == TcpState.Established),
                        connections.Count(c => c.State == TcpState.TimeWait),
                        connections.Length);
                }
                catch
                {
                    return new TcpCounts(0, 0, 0);
                }
            }
        }

        private sealed class NoAcceptTcpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private int _disposed;

            private NoAcceptTcpServer(int port)
            {
                Port = port;
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start(backlog: 1);
            }

            public int Port { get; }

            public static NoAcceptTcpServer Start()
            {
                return new NoAcceptTcpServer(GetFreePort());
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                _listener.Stop();
            }
        }

        private sealed class RawChaosServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
            private readonly Func<ChaosRequest, long, ChaosResponse> _behavior;
            private readonly Task _listenTask;
            private readonly List<Task> _clientTasks = new List<Task>();
            private readonly object _clientTasksLock = new object();

            private long _acceptedConnections;
            private long _totalRequests;
            private long _commandRequests;
            private long _dataRequests;
            private long _okResponses;
            private long _droppedConnections;
            private long _hungConnections;
            private long _slowReads;
            private long _headerOnlyResponses;
            private long _malformedResponses;
            private long _resetConnections;
            private long _requestBytes;

            private RawChaosServer(int port, Func<ChaosRequest, long, ChaosResponse> behavior)
            {
                Port = port;
                _behavior = behavior;
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _listenTask = Task.Run(() => ListenAsync(_tokenSource.Token));
            }

            public int Port { get; }

            public ChaosServerStats Stats => new ChaosServerStats
            {
                AcceptedConnections = Interlocked.Read(ref _acceptedConnections),
                TotalRequests = Interlocked.Read(ref _totalRequests),
                CommandRequests = Interlocked.Read(ref _commandRequests),
                DataRequests = Interlocked.Read(ref _dataRequests),
                OkResponses = Interlocked.Read(ref _okResponses),
                DroppedConnections = Interlocked.Read(ref _droppedConnections),
                HungConnections = Interlocked.Read(ref _hungConnections),
                SlowReads = Interlocked.Read(ref _slowReads),
                HeaderOnlyResponses = Interlocked.Read(ref _headerOnlyResponses),
                MalformedResponses = Interlocked.Read(ref _malformedResponses),
                ResetConnections = Interlocked.Read(ref _resetConnections),
                RequestBytes = Interlocked.Read(ref _requestBytes)
            };

            public static RawChaosServer Start(Func<ChaosRequest, long, ChaosResponse> behavior)
            {
                return Start(GetFreePort(), behavior);
            }

            public static RawChaosServer Start(int port, Func<ChaosRequest, long, ChaosResponse> behavior)
            {
                return new RawChaosServer(port, behavior ?? ((request, number) => ChaosResponse.Ok()));
            }

            public void Dispose()
            {
                _tokenSource.Cancel();

                try
                {
                    _listener.Stop();
                }
                catch (ObjectDisposedException)
                {
                }

                try
                {
                    _listenTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }

                Task[] tasks;
                lock (_clientTasksLock)
                    tasks = _clientTasks.ToArray();

                try
                {
                    Task.WaitAll(tasks, TimeSpan.FromSeconds(3));
                }
                catch (AggregateException)
                {
                }

                _tokenSource.Dispose();
            }

            private async Task ListenAsync(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client;

                    try
                    {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        break;
                    }

                    Interlocked.Increment(ref _acceptedConnections);

                    var task = Task.Run(() => HandleClientAsync(client, token), token);

                    lock (_clientTasksLock)
                    {
                        _clientTasks.RemoveAll(t => t.IsCompleted);
                        _clientTasks.Add(task);
                    }
                }
            }

            private async Task HandleClientAsync(TcpClient client, CancellationToken token)
            {
                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        var request = await ChaosRequest.ReadAsync(stream, token).ConfigureAwait(false);
                        var requestNumber = Interlocked.Increment(ref _totalRequests);

                        if (request.IsCommand)
                            Interlocked.Increment(ref _commandRequests);
                        else if (request.IsData)
                            Interlocked.Increment(ref _dataRequests);

                        var response = _behavior(request, requestNumber);

                        await ApplyResponseAsync(client, stream, request, response, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (SocketException)
                    {
                    }
                }
            }

            private async Task ApplyResponseAsync(TcpClient client, NetworkStream stream, ChaosRequest request, ChaosResponse response, CancellationToken token)
            {
                switch (response.Kind)
                {
                    case ChaosResponseKind.Ok:
                        await DrainBodyAsync(stream, request, token).ConfigureAwait(false);
                        await WriteOkAsync(stream, request).ConfigureAwait(false);
                        Interlocked.Increment(ref _okResponses);
                        break;

                    case ChaosResponseKind.DelayedOk:
                        await DrainBodyAsync(stream, request, token).ConfigureAwait(false);
                        await Task.Delay(response.Delay, token).ConfigureAwait(false);
                        await WriteOkAsync(stream, request).ConfigureAwait(false);
                        Interlocked.Increment(ref _okResponses);
                        break;

                    case ChaosResponseKind.DropAfter:
                        Interlocked.Increment(ref _droppedConnections);
                        if (response.Delay > TimeSpan.Zero)
                            await Task.Delay(response.Delay, token).ConfigureAwait(false);
                        break;

                    case ChaosResponseKind.NeverRespond:
                        Interlocked.Increment(ref _hungConnections);
                        await Task.Delay(response.Delay, token).ConfigureAwait(false);
                        break;

                    case ChaosResponseKind.SlowReadBody:
                        Interlocked.Increment(ref _slowReads);
                        await DrainBodySlowlyAsync(stream, request, response.Delay, token).ConfigureAwait(false);
                        await WriteOkAsync(stream, request).ConfigureAwait(false);
                        Interlocked.Increment(ref _okResponses);
                        break;

                    case ChaosResponseKind.HeadersOnly:
                        Interlocked.Increment(ref _headerOnlyResponses);
                        await DrainBodyAsync(stream, request, token).ConfigureAwait(false);
                        await WriteHeadersOnlyAsync(stream).ConfigureAwait(false);
                        await Task.Delay(response.Delay, token).ConfigureAwait(false);
                        break;

                    case ChaosResponseKind.MalformedHttp:
                        Interlocked.Increment(ref _malformedResponses);
                        await WriteAsciiAsync(stream, "this is not http\r\n\r\n").ConfigureAwait(false);
                        break;

                    case ChaosResponseKind.ResetDuringBody:
                        Interlocked.Increment(ref _resetConnections);
                        await DrainBodyPartiallyAsync(stream, response.BytesToReadBeforeReset, token).ConfigureAwait(false);
                        client.LingerState = new LingerOption(true, 0);
                        client.Close();
                        break;
                }
            }

            private async Task DrainBodyAsync(NetworkStream stream, ChaosRequest request, CancellationToken token)
            {
                var remaining = request.ContentLength;
                var buffer = new byte[8192];

                while (remaining > 0 && !token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining), token).ConfigureAwait(false);

                    if (read <= 0)
                        break;

                    remaining -= read;
                    Interlocked.Add(ref _requestBytes, read);
                }
            }

            private async Task DrainBodySlowlyAsync(NetworkStream stream, ChaosRequest request, TimeSpan delay, CancellationToken token)
            {
                var remaining = request.ContentLength;
                var buffer = new byte[1];

                while (remaining > 0 && !token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, 0, 1, token).ConfigureAwait(false);

                    if (read <= 0)
                        break;

                    remaining -= read;
                    Interlocked.Add(ref _requestBytes, read);

                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, token).ConfigureAwait(false);
                }
            }

            private async Task DrainBodyPartiallyAsync(NetworkStream stream, int bytesToRead, CancellationToken token)
            {
                var remaining = Math.Max(0, bytesToRead);
                var buffer = new byte[256];

                while (remaining > 0 && !token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining), token).ConfigureAwait(false);

                    if (read <= 0)
                        break;

                    remaining -= read;
                    Interlocked.Add(ref _requestBytes, read);
                }
            }

            private static Task WriteOkAsync(NetworkStream stream, ChaosRequest request)
            {
                var body = request.IsCommand ? "{}" : "\"ok\"";
                var response = "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json\r\n"
                    + "Content-Length: " + Encoding.UTF8.GetByteCount(body).ToString(CultureInfo.InvariantCulture) + "\r\n"
                    + "Connection: close\r\n"
                    + "\r\n"
                    + body;

                return WriteAsciiAsync(stream, response);
            }

            private static Task WriteHeadersOnlyAsync(NetworkStream stream)
            {
                return WriteAsciiAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1048576\r\nConnection: close\r\n\r\n");
            }

            private static async Task WriteAsciiAsync(NetworkStream stream, string value)
            {
                var bytes = Encoding.ASCII.GetBytes(value);
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
        }

        private sealed class ChaosRequest
        {
            private ChaosRequest(string rawUrl, int contentLength)
            {
                RawUrl = rawUrl ?? string.Empty;
                ContentLength = contentLength;
            }

            public string RawUrl { get; }

            public int ContentLength { get; }

            public bool IsCommand => RawUrl.EndsWith("/commands", StringComparison.OrdinalIgnoreCase)
                || RawUrl.EndsWith("/addOrUpdate", StringComparison.OrdinalIgnoreCase);

            public bool IsData => RawUrl.IndexOf("/api/sensors/", StringComparison.OrdinalIgnoreCase) >= 0
                && !IsCommand
                && !RawUrl.EndsWith("/testConnection", StringComparison.OrdinalIgnoreCase);

            public static async Task<ChaosRequest> ReadAsync(NetworkStream stream, CancellationToken token)
            {
                var headerBytes = new List<byte>();
                var buffer = new byte[1];

                while (headerBytes.Count < 64 * 1024 && !token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, 0, 1, token).ConfigureAwait(false);

                    if (read <= 0)
                        break;

                    headerBytes.Add(buffer[0]);

                    if (EndsWithHeaderTerminator(headerBytes))
                        break;
                }

                var header = Encoding.ASCII.GetString(headerBytes.ToArray());
                var lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var rawUrl = ParseRawUrl(lines.FirstOrDefault());
                var contentLength = ParseContentLength(lines);

                return new ChaosRequest(rawUrl, contentLength);
            }

            private static bool EndsWithHeaderTerminator(IReadOnlyList<byte> bytes)
            {
                var count = bytes.Count;

                return count >= 4
                    && bytes[count - 4] == '\r'
                    && bytes[count - 3] == '\n'
                    && bytes[count - 2] == '\r'
                    && bytes[count - 1] == '\n';
            }

            private static string ParseRawUrl(string requestLine)
            {
                if (string.IsNullOrWhiteSpace(requestLine))
                    return string.Empty;

                var parts = requestLine.Split(' ');

                return parts.Length >= 2 ? parts[1] : string.Empty;
            }

            private static int ParseContentLength(IEnumerable<string> lines)
            {
                foreach (var line in lines)
                {
                    if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = line.Substring("Content-Length:".Length).Trim();

                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var contentLength))
                        return contentLength;
                }

                return 0;
            }
        }

        private sealed class ChaosResponse
        {
            private ChaosResponse(ChaosResponseKind kind, TimeSpan delay, int bytesToReadBeforeReset)
            {
                Kind = kind;
                Delay = delay;
                BytesToReadBeforeReset = bytesToReadBeforeReset;
            }

            public ChaosResponseKind Kind { get; }

            public TimeSpan Delay { get; }

            public int BytesToReadBeforeReset { get; }

            public static ChaosResponse Ok()
            {
                return new ChaosResponse(ChaosResponseKind.Ok, TimeSpan.Zero, 0);
            }

            public static ChaosResponse DelayedOk(TimeSpan delay)
            {
                return new ChaosResponse(ChaosResponseKind.DelayedOk, delay, 0);
            }

            public static ChaosResponse DropAfter(TimeSpan delay)
            {
                return new ChaosResponse(ChaosResponseKind.DropAfter, delay, 0);
            }

            public static ChaosResponse NeverRespond()
            {
                return NeverRespond(TimeSpan.FromSeconds(30));
            }

            public static ChaosResponse NeverRespond(TimeSpan holdFor)
            {
                return new ChaosResponse(ChaosResponseKind.NeverRespond, holdFor, 0);
            }

            public static ChaosResponse SlowReadBody(TimeSpan delay)
            {
                return new ChaosResponse(ChaosResponseKind.SlowReadBody, delay, 0);
            }

            public static ChaosResponse HeadersOnly()
            {
                return HeadersOnly(TimeSpan.FromSeconds(30));
            }

            public static ChaosResponse HeadersOnly(TimeSpan holdFor)
            {
                return new ChaosResponse(ChaosResponseKind.HeadersOnly, holdFor, 0);
            }

            public static ChaosResponse MalformedHttp()
            {
                return new ChaosResponse(ChaosResponseKind.MalformedHttp, TimeSpan.Zero, 0);
            }

            public static ChaosResponse ResetDuringBody(int bytesToReadBeforeReset)
            {
                return new ChaosResponse(ChaosResponseKind.ResetDuringBody, TimeSpan.Zero, bytesToReadBeforeReset);
            }
        }

        private enum ChaosResponseKind
        {
            Ok,
            DelayedOk,
            DropAfter,
            NeverRespond,
            SlowReadBody,
            HeadersOnly,
            MalformedHttp,
            ResetDuringBody
        }

        private sealed class ChaosServerStats
        {
            public long AcceptedConnections { get; set; }

            public long TotalRequests { get; set; }

            public long CommandRequests { get; set; }

            public long DataRequests { get; set; }

            public long OkResponses { get; set; }

            public long DroppedConnections { get; set; }

            public long HungConnections { get; set; }

            public long SlowReads { get; set; }

            public long HeaderOnlyResponses { get; set; }

            public long MalformedResponses { get; set; }

            public long ResetConnections { get; set; }

            public long RequestBytes { get; set; }
        }
    }

    [CollectionDefinition("Collector transport chaos", DisableParallelization = true)]
    public sealed class CollectorTransportChaosCollection
    {
    }
}
