using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tables;
using Xunit;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Tables
{
    /// <summary>
    /// The findings of the review of #1846 part 1, each pinned by a test that fails without its fix. Orders of events
    /// are forced through the pipeline's test hooks; no test waits for time to pass.
    /// </summary>
    public sealed class TablePipelineRaceTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private static CollectorOptions Options(IDataSender sender, Action<CollectorOptions> tune = null)
        {
            var options = new CollectorOptions { AccessKey = "table-race-tests", DataSender = sender, ManualTableTick = true };
            tune?.Invoke(options);
            return options;
        }

        private static TableSensorOptions Quotes() => new TableSensorOptions
        {
            SensorLocation = SensorLocation.Product,
            Columns = { TableColumn.Double("bid"), TableColumn.Double("ask") },
        };

        private static async Task<DataCollector> StartedAsync(CollectorOptions options)
        {
            var collector = new DataCollector(options);
            await collector.Start();
            return collector;
        }


        // Item 1: a cleared buffer that grew does not keep its arrays beyond the collector-wide bound.
        [Fact]
        public async Task Buffers_that_grew_give_their_arrays_up_when_cleared()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var tables = Enumerable.Range(0, 5).Select(i => collector.CreateTableSensor($"t{i}", Quotes())).Cast<TableSensor>().ToList();
                await collector.TickTablesAsync();

                // A different sensor is busy at every tick.
                foreach (var table in tables)
                {
                    for (var row = 0; row < 5000; row++)
                        table.AddRow(T0.AddTicks(row), row, row);

                    await collector.TickTablesAsync();
                }

                Assert.All(tables, t => Assert.True(t.RetainedRowCapacity <= 2 * TableRowBuffer.RetainedCapacity, $"{t.Path} keeps arrays for {t.RetainedRowCapacity} rows"));
                Assert.Equal(25000, tables.Sum(t => sender.RowsOf(t.Path).Count));
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);
            }
        }

        // Item 2: after the final tick of a stop took the rows, rows are refused and counted until the next Start.
        [Fact]
        public async Task Rows_after_the_final_tick_are_refused_until_the_next_start()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();

                table.AddRow(T0, 1.0, 2.0);

                // The table drain of a stop, while the collector is still Stopping for its value queues.
                await collector.TablePipeline.FlushAsync(CancellationToken.None);

                Assert.Single(sender.RowsOf("t"));
                Assert.False(table.AddRow(T0, 3.0, 4.0));
                Assert.Equal(1, table.RowsDropped);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);

                await collector.Stop();
                await collector.Start();

                Assert.True(table.AddRow(T0, 5.0, 6.0));
            }
        }

        // Item 3: a write that reserved room while the sensor was Pending and appends after a refusal is dropped.
        [Fact]
        public async Task A_write_overtaken_by_a_refusal_is_dropped_and_gives_its_room_back()
        {
            using (var collector = await StartedAsync(Options(new FakeTableSender())))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                var sensor = (TableSensor)table;

                sensor.BeforeAppendForTests = () =>
                {
                    sensor.TryMove(TableSensorState.Pending, TableSensorState.Refused, "refused meanwhile");
                    sensor.DiscardRows(TableDropReason.SensorRefused);
                };

                Assert.False(table.AddRow(T0, 1.0, 2.0));

                sensor.TryMove(TableSensorState.Refused, TableSensorState.Pending);
                Assert.Equal(0, table.AddRows(new[] { T0, T0 }, new TableCell[] { 1.0, 2.0, 3.0, 4.0 }, 2));

                Assert.Equal(3, table.RowsDropped);
                Assert.Equal(0, table.RowsAccepted);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);
            }
        }

        // Item 4, first half: the final pack observes the drain budget between its passes.
        [Fact]
        public async Task The_final_pack_stops_at_the_drain_budget_and_counts_the_rest()
        {
            var sender = new FakeTableSender { MaxBlockBytes = 300 };

            using (var collector = await StartedAsync(Options(sender)))
            {
                var tables = Enumerable.Range(0, 6).Select(i => collector.CreateTableSensor($"t{i}", Quotes())).ToList();
                await collector.TickTablesAsync();

                var random = new Random(4);

                foreach (var table in tables)
                    for (var row = 0; row < 50; row++)
                        table.AddRow(T0.AddTicks(random.Next()), random.NextDouble(), random.NextDouble());

                var budget = new CancellationTokenSource();
                var passes = 0;

                collector.TablePipeline.OnPackPassForTests = () =>
                {
                    passes++;
                    budget.Cancel();
                };

                await collector.TablePipeline.FlushAsync(budget.Token);

                Assert.Equal(1, passes);
                Assert.Equal(300, tables.Sum(t => t.RowsDropped));
                Assert.Empty(sender.Attempts);
            }
        }

        // Item 4, second half: the value queues drain while the tables are still packing.
        [Fact]
        public async Task The_table_drain_does_not_hold_the_value_drain_back()
        {
            var sender = new FakeTableSender();
            var valueSentWhilePacking = false;

            using (var collector = await StartedAsync(Options(sender, o => o.PackageCollectPeriod = TimeSpan.FromHours(1))))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();

                collector.CreateIntSensor("values/int").AddValue(42);
                table.AddRow(T0, 1.0, 2.0);

                collector.TablePipeline.OnPackPassForTests = () => valueSentWhilePacking = sender.ValueSent.Wait(TimeSpan.FromSeconds(30));

                await collector.Stop();
            }

            Assert.True(valueSentWhilePacking, "the value queues did not drain while the table drain was packing");
            Assert.Single(sender.RowsOf("t"));
        }

        // Item 5: a stop (here the rollback) and a tick that is packing do not interleave.
        [Fact]
        public async Task A_stop_waits_for_a_tick_that_is_packing()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();
                table.AddRow(T0, 1.0, 2.0);

                var pipeline = collector.TablePipeline;
                var packing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopWaits = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                pipeline.OnPackPassForTests = () =>
                {
                    packing.TrySetResult(true);
                    resume.Task.Wait();
                };
                pipeline.OnStopWaitsForPackForTests = () => stopWaits.TrySetResult(true);
                sender.SetDown(true);

                var tick = Task.Run(() => collector.TickTablesAsync());
                await packing.Task;

                var stop = Task.Run(() => pipeline.StopWithoutFlushAsync());
                await Task.WhenAny(stop, stopWaits.Task);

                resume.SetResult(true);
                await Task.WhenAll(tick, stop);

                Assert.Equal(0, pipeline.BacklogBlocks);
                Assert.Equal(1, table.RowsDropped);
            }
        }

        // Item 6: a block that got its answer is never evicted by the bound as if it were lost.
        [Fact]
        public async Task An_answered_block_is_not_evicted_before_it_is_settled()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender, o => o.MaxTableBacklogBytes = 20_000)))
            {
                var small = collector.CreateTableSensor("small", Quotes());
                var large = collector.CreateTableSensor("large", Quotes());
                await collector.TickTablesAsync();

                small.AddRow(T0, 1.0, 2.0);

                var pipeline = collector.TablePipeline;
                var once = 0;

                pipeline.OnAckForTests = () =>
                {
                    if (Interlocked.Exchange(ref once, 1) == 1)
                        return;

                    // Between the answer and its settlement a tick packs a block larger than the bound.
                    var random = new Random(6);

                    for (var row = 0; row < 3000; row++)
                        large.AddRow(T0.AddTicks(random.Next()), random.NextDouble(), random.NextDouble());

                    pipeline.PackAsync(CancellationToken.None, TableDropReason.NotDeliveredAtStop).GetAwaiter().GetResult();
                };

                await collector.TickTablesAsync();

                Assert.Single(sender.RowsOf("small"));
                Assert.Equal(0, small.RowsDropped);
                Assert.Equal(3000, large.RowsDropped);
            }
        }

        // Item 7: every table loss of a run is in a published bar or in the stop line.
        [Fact]
        public async Task Losses_reported_after_the_sensors_stopped_are_in_the_stop_line()
        {
            var sender = new FakeTableSender();
            var logger = new CapturingLogger();

            // One row of time, bid and ask takes 24 bytes: room for one.
            var collector = new DataCollector(Options(sender, o => o.MaxTableBufferBytes = 24));
            collector.AddCustomLogger(logger);
            if (DataCollector.IsWindowsOS)
                collector.Windows.AddQueueOverflow();
            else
                collector.Unix.AddQueueOverflow();

            await collector.Start();

            var table = collector.CreateTableSensor("t", Quotes());
            await collector.TickTablesAsync();
            sender.SetDown(true);

            Assert.True(table.AddRow(T0, 1.0, 2.0));
            Assert.False(table.AddRow(T0, 1.0, 2.0));
            Assert.False(table.AddRow(T0, 1.0, 2.0));
            Assert.False(table.AddRow(T0, 1.0, 2.0));

            collector.Dispose();

            Assert.Equal(4, table.RowsDropped);
            Assert.Contains(logger.Errors, e => e.Contains("4 table row(s) were lost during this run"));
        }

        // Item 9: the answer to a registration request of an earlier run neither holds the new run back nor changes it.
        [Fact]
        public async Task A_registration_answer_of_an_earlier_run_changes_nothing()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                var pipeline = collector.TablePipeline;

                var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                pipeline.OnRegistrationAnsweredForTests = () =>
                {
                    answered.TrySetResult(true);
                    return resume.Task;
                };

                var oldTick = Task.Run(() => collector.TickTablesAsync());
                await answered.Task;

                pipeline.OnRegistrationAnsweredForTests = null;

                await collector.Stop();
                await collector.Start();

                sender.RefuseRegistration("t", "refused in the new run");
                await collector.TickTablesAsync();

                Assert.Equal(2, sender.Registrations.Count);
                Assert.Equal(TableSensorState.Refused, table.State);

                resume.SetResult(true);
                await oldTick;

                Assert.Equal(TableSensorState.Refused, table.State);
                Assert.Equal("refused in the new run", table.StateReason);
            }
        }

        // Item 10: a reservation that cannot fit never touches the counter.
        [Fact]
        public async Task An_oversized_reservation_never_touches_the_counter()
        {
            // One row of time and one value takes 16 bytes: the bound holds one.
            using (var collector = await StartedAsync(Options(new FakeTableSender(), o => o.MaxTableBufferBytes = 16)))
            {
                var table = collector.CreateTableSensor("t", new TableSensorOptions { Columns = { TableColumn.Double("v") } });
                var pipeline = collector.TablePipeline;
                var seen = new ConcurrentQueue<long>();

                pipeline.OnReservationForTests = () => seen.Enqueue(pipeline.BufferedBytes);

                Assert.Equal(0, table.AddRows(new[] { T0, T0 }, new TableCell[] { 1.0, 2.0 }, 2));
                Assert.All(seen, bytes => Assert.Equal(0, bytes));

                Assert.True(table.AddRow(T0, 3.0));
                Assert.Equal(16, pipeline.BufferedBytes);
            }
        }


        private sealed class CapturingLogger : ICollectorLogger
        {
            public ConcurrentQueue<string> Errors { get; } = new ConcurrentQueue<string>();

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Error(string message) => Errors.Enqueue(message);

            public void Error(Exception ex) => Errors.Enqueue(ex.ToString());
        }
    }
}
