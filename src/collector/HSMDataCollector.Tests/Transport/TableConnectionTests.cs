using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.Session;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tables;
using Xunit;
using static HSMDataCollector.Tests.Transport.ScriptedSessionServer;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// The table connection of the managed collector (#1846 part 2) against the scripted server: the upgrade, the table
    /// hello and its limits, the probe, one block at a time with its ack, every status of an ack, a lost ack, 1013, the
    /// upgrade refusals, the replacement after a new session, the stop, a broken server. Runs on both TFMs (the file is
    /// linked into the net8 integration tests). No test decides by waiting for time: conditions are polled to a deadline
    /// and outcomes come from the script.
    /// </summary>
    public sealed class TableConnectionTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
        private static readonly DateTime T0 = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private static CollectorOptions OptionsFor(ScriptedSessionServer server, Action<CollectorOptions> tune = null)
        {
            var options = new CollectorOptions
            {
                ServerAddress = server.ServerAddress,
                Port = server.Port,
                AccessKey = "table-connection-key",
                AllowPlaintextTransport = true,
                Transport = CollectorTransport.Session,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(10),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(50),
                ManualTableTick = true,
                SessionTimings = new SessionTimings
                {
                    MinBackoff = TimeSpan.FromMilliseconds(20),
                    MaxBackoff = TimeSpan.FromMilliseconds(100),
                    TableRefusedRetry = TimeSpan.FromMilliseconds(200),
                    TableConnectionWait = TimeSpan.FromSeconds(5),
                },
            };

            tune?.Invoke(options);
            return options;
        }

        private static TableSensorOptions Quotes() => new TableSensorOptions
        {
            SensorLocation = SensorLocation.Product,
            Columns = { TableColumn.Double("bid"), TableColumn.Double("ask") },
        };

        private static async Task UntilAsync(Func<bool> condition, string what)
        {
            var clock = Stopwatch.StartNew();

            while (!condition())
            {
                Assert.True(clock.Elapsed < Patience, what);
                await Task.Delay(10);
            }
        }

        // Ticks until the sensor is registered: a registration needs the session, which comes up in the background.
        private static async Task RegisterAsync(DataCollector collector, ITableSensor table)
        {
            var clock = Stopwatch.StartNew();

            while (table.State == TableSensorState.Pending)
            {
                Assert.True(clock.Elapsed < Patience, "the table sensor was not registered");
                await collector.TickTablesAsync();
                await Task.Delay(10);
            }

            Assert.Equal(TableSensorState.Registered, table.State);
        }

        private static TableConnection TableLink(DataCollector collector, CollectorOptions options) =>
            ((SessionDataSender)options.DataSender).Tables;

        private sealed class Rig : IDisposable
        {
            internal Rig(Action<ScriptedSessionServer> script = null, Action<CollectorOptions> tune = null)
            {
                Server = new ScriptedSessionServer();
                script?.Invoke(Server);
                Options = OptionsFor(Server, tune);
                Collector = new DataCollector(Options);
                Collector.AddCustomLogger(Logger);
                Table = Collector.CreateTableSensor("quotes/A", Quotes());
            }

            internal ScriptedSessionServer Server { get; }

            internal CollectorOptions Options { get; }

            internal DataCollector Collector { get; }

            internal ITableSensor Table { get; }

            internal CapturingLogger Logger { get; } = new CapturingLogger();

            internal TableConnection Link => ((SessionDataSender)Options.DataSender).Tables;

            internal async Task StartAsync()
            {
                await Collector.Start();
                await RegisterAsync(Collector, Table);
            }

            public void Dispose()
            {
                Collector.Dispose();
                Server.Dispose();
            }
        }


        [Fact]
        public async Task A_block_goes_as_one_frame_after_the_hello_and_the_probe()
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                Assert.True(rig.Table.AddRow(T0, 1.5, 1.6));
                Assert.True(rig.Table.AddRow(T0.AddSeconds(1), 2.5, TableCell.Null));
                await rig.Collector.TickTablesAsync();

                var frame = Assert.Single(rig.Server.TableFrames);
                Assert.Equal("stored", frame.Outcome);
                Assert.Equal(0, frame.Sequence);
                Assert.Equal(rig.Collector.TablePipeline.Producer, frame.Producer);
                Assert.Null(frame.DecodeError);

                var rows = rig.Server.StoredTableRowsOf("quotes/A");
                Assert.Equal(2, rows.Count);
                Assert.Equal(T0.Ticks, rows[0].Ticks);
                Assert.Equal(new double?[] { 1.5, 1.6 }, rows[0].Values);
                Assert.Equal(new double?[] { 2.5, null }, rows[1].Values);

                // The upgrade named the live session, and a probe went before the block.
                var upgrade = Assert.Single(rig.Server.TableUpgradeHeaders);
                Assert.Equal(rig.Server.SessionIds[0], upgrade["X-Hsm-Session"]);
                Assert.Equal("table-connection-key", upgrade["Key"]);
                Assert.Single(rig.Server.TableProbes);
                Assert.True(rig.Server.TableProbes[0] < frame.Id);
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        [Fact]
        public async Task A_collector_without_table_sensors_opens_no_table_connection()
        {
            using (var server = new ScriptedSessionServer())
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                var sensor = collector.CreateIntSensor("values/int");
                await collector.Start();

                await UntilAsync(() =>
                {
                    sensor.AddValue(42);
                    return server.Accepted.Any(r => r.Op == "list");
                }, "no value was delivered");

                Assert.Empty(server.TableUpgradeHeaders);
                Assert.Null(collector.TablePipeline);
            }
        }

        // Nothing of the table connection exists before the first table sensor: no object, no loop, no task.
        [Fact]
        public async Task The_table_connection_is_made_with_the_first_table_sensor()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);

                using (var collector = new DataCollector(options))
                {
                    var sender = (SessionDataSender)options.DataSender;

                    await collector.Start();
                    await sender.WaitForConnectionAsync(CancellationToken.None);

                    Assert.Null(sender.TablesIfMade);

                    // Created while the collector runs: the connection is made, started and used.
                    var table = collector.CreateTableSensor("quotes/late", Quotes());

                    Assert.NotNull(sender.TablesIfMade);

                    await RegisterAsync(collector, table);

                    Assert.True(table.AddRow(T0, 1.0, 2.0));
                    await collector.TickTablesAsync();

                    Assert.Single(server.StoredTableRowsOf("quotes/late"));
                }
            }
        }

        // Every status an ack can carry, and whether a repeat can cure it.
        [Theory]
        [InlineData("stored-unknown-sensor", 1, false, TableSensorState.Pending)]
        [InlineData("duplicate-with-refusals", 1, false, TableSensorState.Refused)]
        [InlineData("duplicate-refusals-unknown", 0, false, TableSensorState.Registered)]
        [InlineData("all-refused", 1, false, TableSensorState.Registered)]
        [InlineData("unknown-outcome", 0, false, TableSensorState.Registered)]
        [InlineData("400", 1, false, TableSensorState.Registered)]
        [InlineData("404", 1, false, TableSensorState.Registered)]
        [InlineData("408", 0, true, TableSensorState.Registered)]
        [InlineData("429", 0, true, TableSensorState.Registered)]
        [InlineData("500", 0, true, TableSensorState.Registered)]
        [InlineData("503", 0, true, TableSensorState.Registered)]
        public async Task Every_ack_status_maps_to_its_answer(string answer, int dropped, bool sentAgain, TableSensorState state)
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                rig.Server.AnswerTableNext(Scripted(answer));
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                Assert.Equal(dropped, rig.Table.RowsDropped);
                Assert.Equal(state, rig.Table.State);

                // The next delivery: a block a repeat can cure goes again with its sequence; a settled one does not.
                await rig.Collector.TickTablesAsync();

                var sequences = rig.Server.TableFrames.Select(f => f.Sequence).ToList();

                if (sentAgain)
                {
                    Assert.Equal(new long[] { 0, 0 }, sequences);
                    Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                }
                else
                {
                    Assert.Equal(new long[] { 0 }, sequences);
                }
            }
        }

        private static TableAnswer Scripted(string answer)
        {
            switch (answer)
            {
                case "stored-unknown-sensor": return TableAnswer.Store(new System.Collections.Generic.Dictionary<string, string> { ["quotes/A"] = "UnknownSensor" });
                case "duplicate-with-refusals": return TableAnswer.WithText("{\"ack\":{id},\"status\":200,\"body\":{\"outcome\":\"duplicate\",\"tables\":1,\"refusedCount\":1,\"refused\":[{\"index\":0,\"table\":\"quotes/A\",\"reason\":\"SchemaIdMismatch\"}]}}");
                case "duplicate-refusals-unknown": return TableAnswer.WithText("{\"ack\":{id},\"status\":200,\"body\":{\"outcome\":\"duplicate\",\"tables\":1,\"refusedKnown\":false}}");
                case "all-refused": return TableAnswer.WithText("{\"ack\":{id},\"status\":200,\"body\":{\"outcome\":\"allRefused\",\"tables\":1,\"refusedCount\":1,\"refused\":[{\"index\":0,\"table\":\"quotes/A\",\"reason\":\"SensorCreatedAfterBlock\"}]}}");
                case "unknown-outcome": return TableAnswer.WithText("{\"ack\":{id},\"status\":200,\"body\":{\"outcome\":\"merged\",\"tables\":1}}");
                case "503": return TableAnswer.WithStatus(503, 0);
                default: return TableAnswer.WithStatus(int.Parse(answer));
            }
        }

        // The wait for an ack ends when the test says so (AckDeadlineForTests), not when a clock does: the first block's
        // wait is ended once the server has stored it without answering, and no later wait ever ends.
        [Fact]
        public async Task A_block_without_an_ack_goes_again_with_its_sequence_and_the_repeat_is_a_duplicate()
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                var noAckInTime = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var waits = 0;

                rig.Link.AckDeadlineForTests = () => Interlocked.Increment(ref waits) == 1 ? noAckInTime.Task : new TaskCompletionSource<bool>().Task;

                // Stored, and the ack is lost.
                rig.Server.AnswerTableNext(TableAnswer.StoreSilently());
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));

                var tick = Task.Run(() => rig.Collector.TickTablesAsync());

                await UntilAsync(() => rig.Server.TableFrames.Count == 1, "the block was not sent");
                noAckInTime.SetResult(true);
                await tick;

                // The connection that got no ack was closed with a close frame, not cut.
                await UntilAsync(() => rig.Server.TableCloses.Contains(1000), "the collector did not close the connection that got no ack");

                var frames = rig.Server.TableFrames;
                Assert.Equal(2, frames.Count);
                Assert.Equal(frames[0].Producer, frames[1].Producer);
                Assert.Equal(frames[0].Sequence, frames[1].Sequence);
                Assert.Equal(new[] { 1, 2 }, frames.Select(f => f.Connection));
                Assert.Equal("duplicate", frames[1].Outcome);

                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                Assert.Equal(0, rig.Table.RowsDropped);
                Assert.Equal(0, rig.Collector.TablePipeline.BacklogBlocks);
            }
        }

        [Fact]
        public async Task A_close_1013_keeps_the_block_and_sends_the_same_frame_again()
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                rig.Server.AnswerTableNext(TableAnswer.CloseWith(1013, "the server holds too many table frames; try again later"));
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                var frames = rig.Server.TableFrames;
                Assert.Equal(2, frames.Count);
                Assert.Equal(frames[0].Sequence, frames[1].Sequence);
                Assert.Equal("stored", frames[1].Outcome);
                Assert.Equal(2, rig.Server.TableConnections);
                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        // 404 (table connections off) and 400 (a server without the subprotocol): said once, asked again on a long
        // period, blocks kept in the backlog meanwhile.
        [Theory]
        [InlineData(404)]
        [InlineData(400)]
        public async Task A_table_upgrade_refused_for_good_is_said_once_and_asked_again_later(int status)
        {
            using (var rig = new Rig(s => s.RefuseTableUpgrades(status), o => o.SessionTimings.TableConnectionWait = TimeSpan.FromMilliseconds(300)))
            {
                await rig.StartAsync();

                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                await UntilAsync(() => rig.Server.RefusedTableUpgrades >= 3, "the table upgrade was not asked again");

                Assert.Empty(rig.Server.TableFrames);
                Assert.Equal(1, rig.Collector.TablePipeline.BacklogBlocks);
                Assert.Single(rig.Logger.Errors.Where(e => e.Contains($"refused the table connection with {status}")));

                rig.Server.AcceptTableUpgrades();
                await UntilAsync(() => rig.Link.IsUp, "the table connection did not come up");
                await rig.Collector.TickTablesAsync();

                Assert.Equal(0L, Assert.Single(rig.Server.TableFrames).Sequence);
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        // A server restarted with table connections switched on (or upgraded) comes back with a new session: the
        // refusal is asked again with it, not only when the long period is over.
        [Theory]
        [InlineData(404)]
        [InlineData(400)]
        public async Task A_table_upgrade_refused_for_good_is_asked_again_with_the_next_session(int status)
        {
            using (var rig = new Rig(s => s.RefuseTableUpgrades(status), o =>
            {
                o.SessionTimings.TableRefusedRetry = TimeSpan.FromHours(1);
                o.SessionTimings.TableConnectionWait = TimeSpan.FromMilliseconds(300);
            }))
            {
                await rig.StartAsync();
                await UntilAsync(() => rig.Server.RefusedTableUpgrades == 1, "no table upgrade was tried");

                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                Assert.Equal(1, rig.Server.RefusedTableUpgrades);

                rig.Server.AcceptTableUpgrades();
                rig.Server.DropSessions();

                await UntilAsync(() => rig.Link.IsUp, "the table connection was not asked for again with the next session");
                await rig.Collector.TickTablesAsync();

                Assert.Equal(rig.Server.SessionIds[1], rig.Server.TableUpgradeHeaders.Last()["X-Hsm-Session"]);
                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
            }
        }

        // 406 and 503 pass by themselves: the session's backoff, then the block goes.
        [Theory]
        [InlineData(406)]
        [InlineData(503)]
        public async Task A_table_upgrade_refused_for_now_is_tried_again_with_the_backoff(int status)
        {
            using (var rig = new Rig(s => s.RefuseTableUpgrades(status), o => o.SessionTimings.TableRefusedRetry = TimeSpan.FromHours(1)))
            {
                await rig.StartAsync();

                await UntilAsync(() => rig.Server.RefusedTableUpgrades >= 3, "the table upgrade was not tried again with the backoff");

                rig.Server.AcceptTableUpgrades();
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
            }
        }

        // 409: the server knows no live session under that id; the next session is the next chance, and no attempt is
        // made before it.
        [Fact]
        public async Task A_409_waits_for_the_next_session()
        {
            using (var rig = new Rig(s => s.RefuseTableUpgrades(409), o => o.SessionTimings.TableConnectionWait = TimeSpan.FromMilliseconds(300)))
            {
                await rig.StartAsync();
                await UntilAsync(() => rig.Server.RefusedTableUpgrades >= 1, "no table upgrade was tried");

                rig.Server.AcceptTableUpgrades();
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();
                await rig.Collector.TickTablesAsync();

                Assert.Equal(1, rig.Server.RefusedTableUpgrades);
                Assert.Equal(0, rig.Server.TableConnections);

                rig.Server.DropSessions();
                await UntilAsync(() => rig.Link.IsUp, "the table connection did not come up with the next session");
                await rig.Collector.TickTablesAsync();

                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                Assert.Equal(rig.Server.SessionIds[1], rig.Server.TableUpgradeHeaders.Last()["X-Hsm-Session"]);
            }
        }

        // The session is lost while a block waits for its ack: the server closes the table connection with its session,
        // and the block goes on the table connection of the next session.
        [Fact]
        public async Task A_block_in_flight_goes_on_the_table_connection_of_the_next_session()
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                rig.Server.AnswerTableNext(TableAnswer.NoAnswer());
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));

                var tick = Task.Run(() => rig.Collector.TickTablesAsync());
                await UntilAsync(() => rig.Server.TableFrames.Count == 1, "the block was not sent");

                rig.Server.DropSessions();
                await tick;

                var frames = rig.Server.TableFrames;
                Assert.Equal(2, frames.Count);
                Assert.Equal(frames[0].Sequence, frames[1].Sequence);
                Assert.Equal(frames[0].Producer, frames[1].Producer);
                Assert.Equal(2, frames[1].Connection);
                Assert.Equal(rig.Server.SessionIds[1], rig.Server.TableUpgradeHeaders[1]["X-Hsm-Session"]);
                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        // 1000 from the server while the session lives (the server replaced the connection, or ended a session the
        // collector still holds): a new table connection of the same session carries the block.
        [Fact]
        public async Task A_close_1000_from_the_server_is_followed_by_a_new_table_connection_with_the_same_block()
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                rig.Server.AnswerTableNext(TableAnswer.CloseWith(1000, "its session ended"));
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                var frames = rig.Server.TableFrames;
                Assert.Equal(2, frames.Count);
                Assert.Equal(frames[0].Sequence, frames[1].Sequence);
                Assert.Equal(frames[0].Producer, frames[1].Producer);
                Assert.Equal(new[] { 1, 2 }, frames.Select(f => f.Connection));
                Assert.Equal(1, rig.Server.Sessions);
                Assert.Equal(rig.Server.SessionIds[0], rig.Server.TableUpgradeHeaders[1]["X-Hsm-Session"]);
                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        [Fact]
        public async Task Blocks_keep_to_the_block_limits_of_the_table_hello()
        {
            using (var rig = new Rig(s => s.TableMaxRows = 100))
            {
                await rig.StartAsync();
                await UntilAsync(() => rig.Link.IsUp, "the table connection did not come up");

                Assert.Equal(100, rig.Link.BlockLimits.MaxRows);

                for (var row = 0; row < 250; row++)
                    Assert.True(rig.Table.AddRow(T0.AddTicks(row), row, row));

                await rig.Collector.TickTablesAsync();

                Assert.Equal(new[] { 100, 100, 50 }, rig.Server.TableFrames.Select(f => f.Decoded.Tables[0].RowCount));
                Assert.Equal(new long[] { 0, 1, 2 }, rig.Server.TableFrames.Select(f => f.Sequence));
                Assert.Equal(250, rig.Server.StoredTableRowsOf("quotes/A").Count);
            }
        }

        // A table connection with nothing to send pings like the session does, so the server does not drop it as silent.
        [Fact]
        public async Task An_idle_table_connection_sends_the_heartbeat()
        {
            using (var rig = new Rig(s => s.HeartbeatSeconds = 1))
            {
                await rig.StartAsync();
                await UntilAsync(() => rig.Server.TablePings >= 2, "the idle table connection sent no ping");

                Assert.Equal(1, rig.Server.TableConnections);
                Assert.True(rig.Link.IsUp);
            }
        }

        [Fact]
        public async Task Blocks_are_split_by_the_frame_limit_of_the_table_hello()
        {
            using (var rig = new Rig(s => s.TableMaxFrameBytes = 2000))
            {
                await rig.StartAsync();
                await UntilAsync(() => rig.Link.IsUp, "the table connection did not come up");

                Assert.Equal(2000 - 40, rig.Link.MaxBlockBytes);

                var random = new Random(7);

                for (var row = 0; row < 600; row++)
                    Assert.True(rig.Table.AddRow(T0.AddTicks(row), random.NextDouble(), random.NextDouble()));

                await rig.Collector.TickTablesAsync();

                Assert.True(rig.Server.TableFrames.Count > 1);
                Assert.All(rig.Server.TableFrames, f => Assert.True(f.FrameBytes <= 2000, $"a frame of {f.FrameBytes} bytes"));
                Assert.Equal(600, rig.Server.StoredTableRowsOf("quotes/A").Count);
            }
        }

        [Fact]
        public async Task Blocks_packed_before_a_smaller_hello_are_packed_again_to_fit()
        {
            // The table connection is refused while the block is packed for the default 4 MiB frame; the hello that
            // comes later allows 2,000 bytes.
            using (var rig = new Rig(s => { s.RefuseTableUpgrades(503); s.TableMaxFrameBytes = 2000; },
                                     o => o.SessionTimings.TableConnectionWait = TimeSpan.FromMilliseconds(200)))
            {
                await rig.StartAsync();

                var random = new Random(8);

                for (var row = 0; row < 600; row++)
                    Assert.True(rig.Table.AddRow(T0.AddTicks(row), random.NextDouble(), random.NextDouble()));

                await rig.Collector.TickTablesAsync();
                Assert.Equal(1, rig.Collector.TablePipeline.BacklogBlocks);

                rig.Server.AcceptTableUpgrades();
                await UntilAsync(() => rig.Link.IsUp, "the table connection did not come up");
                await rig.Collector.TickTablesAsync();

                Assert.True(rig.Server.TableFrames.Count > 1);
                Assert.All(rig.Server.TableFrames, f => Assert.True(f.FrameBytes <= 2000, $"a frame of {f.FrameBytes} bytes"));
                Assert.Equal(600, rig.Server.StoredTableRowsOf("quotes/A").Count);

                var sequences = rig.Server.TableFrames.Select(f => f.Sequence).ToList();
                Assert.Equal(sequences.OrderBy(s => s), sequences);
                Assert.Equal(sequences.Count, sequences.Distinct().Count());
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        [Fact]
        public async Task A_stop_with_a_block_in_flight_closes_the_table_connection_first_and_counts_the_rows_once()
        {
            using (var rig = new Rig(tune: o => o.RequestTimeout = TimeSpan.FromSeconds(2)))
            {
                await rig.StartAsync();

                // The request timeout sets the drain budget here (2 s) and would end the wait for an ack at the same
                // moment; which of the two came first would decide what the test sees. The wait for an ack never
                // ends on its own: only the stop and its budget end it.
                rig.Link.AckDeadlineForTests = () => new TaskCompletionSource<bool>().Task;

                // Neither the delivery nor the drain's resend is answered.
                rig.Server.AnswerTableNext(TableAnswer.NoAnswer());
                rig.Server.AnswerTableNext(TableAnswer.NoAnswer());
                rig.Server.AnswerTableNext(TableAnswer.NoAnswer());
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));

                // The collector's own state when its close of the session arrives at the server.
                var tableUpAtSessionClose = new ConcurrentQueue<bool>();
                var link = rig.Link;

                rig.Server.SessionCloseReceived = () => tableUpAtSessionClose.Enqueue(link.IsUp);

                var tick = Task.Run(() => rig.Collector.TickTablesAsync());
                await UntilAsync(() => rig.Server.TableFrames.Count >= 1, "the block was not sent");

                var stopped = Stopwatch.StartNew();
                await rig.Collector.Stop();
                await tick;

                Assert.True(stopped.Elapsed < TimeSpan.FromSeconds(15), $"the stop took {stopped.Elapsed}");
                Assert.Equal(1, rig.Table.RowsDropped);
                Assert.Single(rig.Logger.Errors.Where(e => e.StartsWith("Collector stop") && e.Contains("table row")));

                // The table connection was closed by the collector, with a close frame, before the session.
                await UntilAsync(() => rig.Server.Closes.Count >= 1, "the session was not closed");
                await UntilAsync(() => rig.Server.TableCloses.Contains(1000), "the table connection was not closed with 1000");
                Assert.Equal(new[] { false }, tableUpAtSessionClose);
            }
        }

        [Theory]
        [InlineData("binary", 1003)]
        [InlineData("oversized", 1009)]
        [InlineData("not-json", 1007)]
        [InlineData("foreign-ack", 1002)]
        public async Task A_broken_server_frame_closes_the_table_connection_and_the_block_goes_again(string broken, int code)
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                rig.Server.AnswerTableNext(Broken(broken));
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();

                await UntilAsync(() => rig.Server.TableCloses.Contains(code), $"the collector did not close with {code}");

                var frames = rig.Server.TableFrames;
                Assert.Equal(2, frames.Count);
                Assert.Equal(frames[0].Sequence, frames[1].Sequence);
                Assert.Single(rig.Server.StoredTableRowsOf("quotes/A"));
                Assert.Equal(0, rig.Table.RowsDropped);
                Assert.Single(rig.Logger.Errors.Where(e => e.Contains("The table connection to") && e.Contains("was closed")));
            }
        }

        private static TableAnswer Broken(string broken)
        {
            switch (broken)
            {
                case "binary": return TableAnswer.BinaryFrame();
                case "oversized": return TableAnswer.WithText("{\"op\":\"x\",\"body\":\"" + new string('x', 2 * 1024 * 1024) + "\"}");
                case "not-json": return TableAnswer.WithText("this is not json");
                default: return TableAnswer.WithText("{\"ack\":987654321,\"status\":200,\"body\":{}}");
            }
        }

        [Fact]
        public async Task Start_stop_start_opens_a_new_table_connection_under_a_new_producer()
        {
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();
                var first = rig.Collector.TablePipeline.Producer;

                await rig.Collector.Stop();
                await rig.Collector.Start();
                await RegisterAsync(rig.Collector, rig.Table);

                Assert.True(rig.Table.AddRow(T0, 3.0, 4.0));
                await rig.Collector.TickTablesAsync();

                var frames = rig.Server.TableFrames;
                Assert.Equal(2, frames.Count);
                Assert.Equal(first, frames[0].Producer);
                Assert.NotEqual(first, frames[1].Producer);
                Assert.Equal(0, frames[1].Sequence);
                Assert.Equal(2, frames[1].Connection);
                Assert.Equal(2, rig.Server.StoredTableRowsOf("quotes/A").Count);
            }
        }

        // Dispose and Stop from every state: never started, started with no session, up, with a block in flight, and
        // again after each other. Nothing throws and nothing hangs.
        [Fact]
        public async Task Dispose_and_stop_never_throw_whatever_the_state()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);

                using (var collector = new DataCollector(options))
                {
                    var sender = (SessionDataSender)options.DataSender;

                    // Never started.
                    var idle = new TableConnection(sender.Connection, null, null, options.SessionTimings, options.RequestTimeout);

                    await idle.StopAsync(TimeSpan.FromSeconds(1));
                    idle.Dispose();
                    idle.Dispose();
                    Assert.Equal(TableBlockOutcome.NotDelivered, (await idle.PutAsync(Guid.NewGuid(), 0, new byte[8], CancellationToken.None)).Outcome);

                    // Started with no session to wait for, and nothing wanted.
                    var waiting = new TableConnection(sender.Connection, null, null, options.SessionTimings, options.RequestTimeout);

                    waiting.Start();
                    waiting.Start();
                    await waiting.StopAsync(TimeSpan.FromSeconds(1));
                    await waiting.StopAsync(TimeSpan.FromSeconds(1));
                    waiting.Dispose();
                }
            }

            // With a block in flight: the dispose ends the put, which answers and does not throw into the tick.
            using (var rig = new Rig())
            {
                await rig.StartAsync();

                rig.Server.AnswerTableNext(TableAnswer.NoAnswer());
                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));

                var tick = Task.Run(() => rig.Collector.TickTablesAsync());
                await UntilAsync(() => rig.Server.TableFrames.Count == 1, "the block was not sent");

                rig.Link.Dispose();
                rig.Link.Dispose();
                await rig.Link.StopAsync(TimeSpan.FromSeconds(1));

                Assert.True(await Task.WhenAny(tick, Task.Delay(Patience)) == tick, "the tick did not end after the dispose");
                await tick;

                Assert.False(rig.Link.IsUp);
                Assert.Equal(1, rig.Collector.TablePipeline.BacklogBlocks);
                Assert.Equal(0, rig.Table.RowsDropped);
            }
        }

        [Fact]
        public async Task A_session_without_a_session_id_opens_no_table_connection_and_says_why_once()
        {
            using (var rig = new Rig(s => s.SendSessionId = false, o => o.SessionTimings.TableConnectionWait = TimeSpan.FromMilliseconds(200)))
            {
                await rig.StartAsync();

                Assert.True(rig.Table.AddRow(T0, 1.0, 2.0));
                await rig.Collector.TickTablesAsync();
                await rig.Collector.TickTablesAsync();

                Assert.Empty(rig.Server.TableUpgradeHeaders);
                Assert.Equal(1, rig.Collector.TablePipeline.BacklogBlocks);
                Assert.Single(rig.Logger.Errors.Where(e => e.StartsWith("Cannot open the table connection") && e.Contains("states no sessionId")));
            }
        }


        internal sealed class CapturingLogger : ICollectorLogger
        {
            private readonly ConcurrentQueue<string> _errors = new ConcurrentQueue<string>();

            public string[] Errors => _errors.ToArray();

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Error(string message) => _errors.Enqueue(message);

            public void Error(Exception ex) => _errors.Enqueue(ex.ToString());
        }
    }
}
