using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.Client.Session;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using Xunit;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// Table sensor registrations over a real collector session (#1846): a <c>commands</c> request of their own, and the
    /// statuses and per-path errors of its ack decide the sensor's state. A refusal a repeat cannot cure is final; a
    /// fault is sent again and never final.
    /// </summary>
    public sealed class TableRegistrationSessionTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

        private static CollectorOptions OptionsFor(ScriptedSessionServer server) => new CollectorOptions
        {
            ServerAddress = server.ServerAddress,
            Port = server.Port,
            AccessKey = "table-session-key",
            AllowPlaintextTransport = true,
            Transport = CollectorTransport.Session,
            PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(10),
            ManualTableTick = true,
            SessionTimings = new SessionTimings
            {
                MinBackoff = TimeSpan.FromMilliseconds(20),
                MaxBackoff = TimeSpan.FromMilliseconds(100),
            },
        };

        private static TableSensorOptions Quotes() => new TableSensorOptions
        {
            SensorLocation = SensorLocation.Product,
            Columns = { TableColumn.Double("bid"), TableColumn.Double("ask") },
        };

        // Ticks until the condition holds: a registration needs the session up, which comes in the background.
        private static async Task TickUntilAsync(DataCollector collector, Func<bool> done)
        {
            var clock = Stopwatch.StartNew();

            while (!done())
            {
                Assert.True(clock.Elapsed < Patience, "the condition did not come true");
                await collector.TickTablesAsync();
                await Task.Delay(20);
            }
        }

        private static bool IsTableRegistration(JsonElement element) =>
            element.TryGetProperty("SensorType", out var type) && type.ValueKind == JsonValueKind.Number && type.GetInt32() == 11;


        [Fact]
        public async Task Table_registrations_travel_alone_and_a_refused_path_is_final()
        {
            using (var server = new ScriptedSessionServer())
            {
                server.RefuseRegistrationOf("quotes/B", "The table sensor registration is refused: another schema");

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var a = collector.CreateTableSensor("quotes/A", Quotes());
                    var b = collector.CreateTableSensor("quotes/B", Quotes());
                    collector.CreateIntSensor("values/int");

                    await collector.Start();
                    await TickUntilAsync(collector, () => a.State != TableSensorState.Pending && b.State != TableSensorState.Pending);

                    Assert.Equal(TableSensorState.Registered, a.State);
                    Assert.Equal(TableSensorState.Refused, b.State);
                    Assert.Equal("The table sensor registration is refused: another schema", b.StateReason);

                    var commands = server.Requests.Where(r => r.Op == "commands").ToList();
                    var tableRequests = commands.Where(r => JsonDocument.Parse(r.Body).RootElement.EnumerateArray().Any(IsTableRegistration)).ToList();

                    var table = Assert.Single(tableRequests);
                    var elements = JsonDocument.Parse(table.Body).RootElement.EnumerateArray().ToList();
                    Assert.All(elements, e => Assert.True(IsTableRegistration(e)));
                    Assert.Equal(new[] { "quotes/A", "quotes/B" }, elements.Select(e => e.GetProperty("Path").GetString()).OrderBy(p => p));
                    Assert.Equal("[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"bid\",\"Type\":3},{\"Name\":\"ask\",\"Type\":3}]",
                                 elements[0].GetProperty("TableColumns").GetRawText());

                    // Final: further ticks do not register the refused path again.
                    await collector.TickTablesAsync();
                    await collector.TickTablesAsync();
                    Assert.Single(server.Requests.Where(r => r.Op == "commands" && r.Body.Contains("quotes/B")));
                }
            }
        }

        [Fact]
        public async Task A_frame_that_faults_does_not_hold_the_other_frames_back()
        {
            // Frames of one registration each: a registration with this description does not fit twice in 1,100 bytes.
            using (var server = new ScriptedSessionServer { MaxFrameBytes = 1100 })
            {
                server.FaultCommandsCarrying("a/fault", 500);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var description = new string('d', 400);
                    var faulting = collector.CreateTableSensor("a/fault", new TableSensorOptions { SensorLocation = SensorLocation.Product, Description = description, Columns = { TableColumn.Double("v") } });
                    var healthy = collector.CreateTableSensor("b/ok", new TableSensorOptions { SensorLocation = SensorLocation.Product, Description = description, Columns = { TableColumn.Double("v") } });

                    await collector.Start();
                    await TickUntilAsync(collector, () => healthy.State != TableSensorState.Pending);

                    Assert.Equal(TableSensorState.Registered, healthy.State);
                    Assert.Equal(TableSensorState.Pending, faulting.State);

                    // The paths went in frames of their own, the faulting one first (path order).
                    var tableFrames = server.Requests.Where(r => r.Op == "commands" && (r.Body.Contains("a/fault") || r.Body.Contains("b/ok"))).ToList();
                    Assert.All(tableFrames, r => Assert.False(r.Body.Contains("a/fault") && r.Body.Contains("b/ok")));
                }
            }
        }

        [Fact]
        public async Task A_fault_is_sent_again_and_a_4xx_is_final()
        {
            using (var server = new ScriptedSessionServer())
            {
                server.AckWith("commands", 500);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var table = collector.CreateTableSensor("quotes/A", Quotes());

                    await collector.Start();
                    await TickUntilAsync(collector, () => server.Requests.Count(r => r.Op == "commands" && r.Body.Contains("quotes/A")) >= 3);

                    Assert.Equal(TableSensorState.Pending, table.State);

                    server.AckWith("commands", 200);
                    await TickUntilAsync(collector, () => table.State != TableSensorState.Pending);

                    Assert.Equal(TableSensorState.Registered, table.State);
                }

                server.AckWith("commands", 406);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var table = collector.CreateTableSensor("quotes/C", Quotes());

                    await collector.Start();
                    await TickUntilAsync(collector, () => table.State != TableSensorState.Pending);

                    Assert.Equal(TableSensorState.Refused, table.State);
                    Assert.Contains("406", table.StateReason);

                    var sent = server.Requests.Count(r => r.Op == "commands" && r.Body.Contains("quotes/C"));
                    await collector.TickTablesAsync();
                    await collector.TickTablesAsync();
                    Assert.Equal(sent, server.Requests.Count(r => r.Op == "commands" && r.Body.Contains("quotes/C")));
                }
            }
        }
    }
}
