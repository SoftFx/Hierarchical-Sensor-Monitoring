using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors;
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
    /// The findings of the second review of #1846 part 1: the lifecycle of the table pipeline. A run is one object
    /// (TableRun), nothing exists before the first table sensor, and the losses of a run are in one ledger with one stop
    /// line. One test per finding; orders of events are forced through hooks and barriers, never by waiting for time.
    /// </summary>
    public sealed class TableRunLifecycleTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private static CollectorOptions Options(IDataSender sender, Action<CollectorOptions> tune = null)
        {
            var options = new CollectorOptions { AccessKey = "table-run-tests", DataSender = sender, ManualTableTick = true };
            tune?.Invoke(options);
            return options;
        }

        private static TableSensorOptions Quotes() => new TableSensorOptions
        {
            SensorLocation = SensorLocation.Product,
            Columns = { TableColumn.Double("bid"), TableColumn.Double("ask") },
        };

        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task Within(Task task, string what)
        {
            Assert.True(await Task.WhenAny(task, Task.Delay(Patience)).ConfigureAwait(false) == task, what);
            await task.ConfigureAwait(false);
        }

        private static IEnumerable<string> StopLines(CapturingLogger logger) =>
            logger.Errors.Where(e => e.StartsWith("Collector stop", StringComparison.Ordinal) && e.Contains("table row"));


        // Finding 1: a report written while the pipeline held its packing gate deadlocked a stop started by the host's
        // logger. Reports are now written only after the gates are released.
        [Fact]
        public async Task A_logger_that_disposes_the_collector_cannot_deadlock_a_tick()
        {
            var sender = new FakeTableSender { MaxBlockBytes = 300 };
            DataCollector collector = null;
            var logger = new CapturingLogger { OnError = line => { if (line.Contains("cut short")) collector.Dispose(); } };

            collector = new DataCollector(Options(sender));
            collector.AddCustomLogger(logger);
            await collector.Start();

            var tables = Enumerable.Range(0, 6).Select(i => collector.CreateTableSensor($"t{i}", Quotes())).ToList();
            await collector.TickTablesAsync();

            var random = new Random(1);

            foreach (var table in tables)
                for (var row = 0; row < 50; row++)
                    table.AddRow(T0.AddTicks(random.Next()), random.NextDouble(), random.NextDouble());

            var pipeline = collector.TablePipeline;

            // The run is cancelled during the first compression pass: the rest of the pack is abandoned and said.
            pipeline.OnPackPassForTests = () => pipeline.CurrentRun?.Cancellation.Cancel();

            await Within(Task.Run(() => collector.TickTablesAsync()), "the tick did not finish: a report under the packing gate deadlocked the stop its logger started");

            Assert.Contains(logger.Errors, e => e.Contains("cut short"));
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
        }

        // Finding 2: a tick of the previous run that resumed after the next Start took the new run's rows and counted
        // them as lost.
        [Fact]
        public async Task A_tick_of_an_earlier_run_never_takes_rows_of_the_next_run()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender)))
            {
                await collector.Start();

                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();

                var pipeline = collector.TablePipeline;
                var paused = Signal();
                var resume = Signal();

                pipeline.OnTickBeforePackForTests = () =>
                {
                    paused.TrySetResult(true);
                    return resume.Task;
                };

                var oldTick = Task.Run(() => collector.TickTablesAsync());
                await paused.Task;

                pipeline.OnTickBeforePackForTests = null;

                await collector.Stop();
                await collector.Start();
                await collector.TickTablesAsync(); // registered in the new run

                Assert.True(table.AddRow(T0, 1.0, 2.0));

                resume.SetResult(true);
                await Within(oldTick, "the old tick did not end");

                Assert.Equal(0, table.RowsDropped);

                await collector.TickTablesAsync();

                var block = Assert.Single(sender.Stored);
                Assert.Single(block.RowsOf("t"));
                Assert.Equal(pipeline.CurrentRun.Producer, block.Producer);
            }
        }

        // Finding 3: a registration answer of an earlier run, applied after the next Start, turned the new run's
        // Pending sensor into Refused and discarded its rows.
        [Fact]
        public async Task A_registration_answer_of_an_earlier_run_never_changes_the_next_run()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender)))
            {
                await collector.Start();

                sender.RefuseRegistration("t", "refused in the old run");
                var table = collector.CreateTableSensor("t", Quotes());
                var pipeline = collector.TablePipeline;
                var applying = Signal();
                var resume = Signal();

                // Paused after the answer was taken and before it is applied to the sensor.
                pipeline.OnRegistrationApplyForTests = _ =>
                {
                    applying.TrySetResult(true);
                    return resume.Task;
                };

                var oldTick = Task.Run(() => collector.TickTablesAsync());
                await applying.Task;

                pipeline.OnRegistrationApplyForTests = null;

                await collector.Stop();
                await collector.Start();

                sender.AcceptRegistration("t");
                Assert.True(table.AddRow(T0, 1.0, 2.0));

                resume.SetResult(true);
                await Within(oldTick, "the old tick did not end");

                Assert.Equal(TableSensorState.Pending, table.State);
                Assert.Equal(0, table.RowsDropped);

                await collector.TickTablesAsync();

                Assert.Equal(TableSensorState.Registered, table.State);
                Assert.Single(sender.RowsOf("t"));
            }
        }

        // Finding 4: a cancellation that came with an answer cleared the in-flight mark, and the bound then evicted the
        // stored block as lost.
        [Fact]
        public async Task A_stored_answer_is_applied_although_the_run_was_cancelled_meanwhile()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender, o => o.MaxTableBacklogBytes = 20_000)))
            {
                await collector.Start();

                var small = collector.CreateTableSensor("small", Quotes());
                var large = collector.CreateTableSensor("large", Quotes());
                await collector.TickTablesAsync();

                var pipeline = collector.TablePipeline;
                var once = 0;

                small.AddRow(T0, 1.0, 2.0);

                // The server stores the block; the run is cancelled before the answer is back.
                sender.AnswerNext(_ =>
                {
                    pipeline.CurrentRun.Cancellation.Cancel();
                    return TableBlockAck.Stored();
                });

                // Between the answer and its settlement, a pack pushes the backlog over its bound.
                pipeline.OnAckForTests = () =>
                {
                    if (Interlocked.Exchange(ref once, 1) == 1)
                        return;

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

        // Finding 5, first half: a host that never creates a table sensor has nothing of the feature.
        [Fact]
        public async Task A_host_without_table_sensors_has_no_table_pipeline()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender, o => o.ManualTableTick = false)))
            {
                collector.CreateIntSensor("values/int");

                await collector.Start();
                Assert.Null(collector.TablePipeline);

                await collector.Stop();
                await collector.Start();
                await collector.Stop();

                Assert.Null(collector.TablePipeline);
            }
        }

        // Finding 5, second half: a compressor that cannot be made makes the sensor Unavailable and fails nothing else.
        [Fact]
        public async Task A_compressor_that_cannot_be_made_leaves_the_sensor_unavailable_and_the_start_whole()
        {
            var sender = new FakeTableSender();
            var logger = new CapturingLogger();

            using (var collector = new DataCollector(Options(sender, o => o.TableCompressorFactory = () => throw new InvalidOperationException("no compressor here"))))
            {
                collector.AddCustomLogger(logger);

                var before = collector.CreateTableSensor("before", Quotes());

                await collector.Start();
                Assert.Equal(CollectorStatus.Running, collector.Status);

                var after = collector.CreateTableSensor("after", Quotes());

                Assert.Equal(TableSensorState.Unavailable, before.State);
                Assert.Equal(TableSensorState.Unavailable, after.State);
                Assert.Contains("compressor", before.StateReason);
                Assert.False(after.AddRow(T0, 1.0, 2.0));
                Assert.Equal(1, after.RowsDropped);

                await collector.Stop();

                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.False(collector.TablePipeline.HasCompressor);

                // The row the sensor refused is a lost row like any other: the run carries its ledger without a compressor.
                Assert.Equal(new[] { "1" }, StopLines(logger).Select(CountIn));
            }
        }

        // Third review, finding a: the rows given to a sensor that cannot send (here a collector with a custom sender, as
        // one on HTTP) were counted on the sensor and said by no report: a pipeline whose sensors cannot send had no run,
        // so no ledger, no tick and no stop line.
        [Fact]
        public async Task Rows_given_to_an_unavailable_sensor_are_in_the_stop_line()
        {
            var sender = new RecordingValueSender();
            var logger = new CapturingLogger();

            using (var collector = new DataCollector(Options(sender)))
            {
                collector.AddCustomLogger(logger);

                var table = collector.CreateTableSensor("t", Quotes());

                Assert.Equal(TableSensorState.Unavailable, table.State);

                await collector.Start();

                for (var row = 0; row < 10; row++)
                    Assert.False(table.AddRow(T0.AddTicks(row), 1.0, 2.0));

                await collector.TickTablesAsync();
                await collector.Stop();

                Assert.Equal(10, table.RowsDropped);
                Assert.False(collector.TablePipeline.HasCompressor);
                Assert.Equal(new[] { "10" }, StopLines(logger).Select(CountIn));
            }
        }

        // The same rows with the Queue overflow sensor registered: the tick hands them to it, and the stop line
        // says only what that sensor did not take. Each row is reported once.
        [Fact]
        public async Task Rows_given_to_an_unavailable_sensor_are_reported_once_by_the_overflow_sensor_or_the_stop_line()
        {
            var sender = new RecordingValueSender();
            var logger = new CapturingLogger();

            using (var collector = new DataCollector(Options(sender)))
            {
                collector.AddCustomLogger(logger);

                if (DataCollector.IsWindowsOS)
                    collector.Windows.AddQueueOverflow();
                else
                    collector.Unix.AddQueueOverflow();

                await collector.Start();

                // Created while the collector runs: the sensor belongs to the run at once.
                var table = collector.CreateTableSensor("t", Quotes());

                Assert.Equal(TableSensorState.Unavailable, table.State);

                for (var row = 0; row < 10; row++)
                    Assert.False(table.AddRow(T0.AddTicks(row), 1.0, 2.0));

                await collector.TickTablesAsync();

                // One more after the tick: the stop finds the sensor's last bar published and says it itself.
                Assert.False(table.AddRow(T0, 1.0, 2.0));

                await collector.Stop();

                Assert.Equal(11, table.RowsDropped);

                var bar = sender.OverflowBars.Last();

                Assert.Contains($"{TablePipeline.DroppedRowsQueueName}: 10", bar.Comment);
                Assert.Equal(new[] { "1" }, StopLines(logger).Select(CountIn));
            }
        }

        // Third review, finding b: a table sensor created while the rollback of a failed start ran (the collector still
        // Starting, the rollback's run already taken out of the pipeline) started a second run, which the rollback never
        // closed: the collector reached Stopped with a tick and a delivery loop alive, and the next Start reused that
        // run and its producer.
        [Fact]
        public async Task A_table_sensor_created_during_a_start_rollback_starts_no_run()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender)))
            {
                collector.CreateTableSensor("t", Quotes());

                var pipeline = collector.TablePipeline;
                var once = 0;
                var statusAtCreation = CollectorStatus.Stopped;
                ITableSensor late = null;

                // The rollback has taken its run out of the pipeline and has not ended.
                pipeline.OnStopWaitsForPackForTests = () =>
                {
                    if (Interlocked.Exchange(ref once, 1) == 1)
                        return;

                    statusAtCreation = collector.Status;
                    late = collector.CreateTableSensor("late", Quotes());
                };

                await Within(collector.Start(Task.FromException(new InvalidOperationException("scripted: the starting task failed"))), "the failed start did not end");

                Assert.NotNull(late);
                Assert.Equal(CollectorStatus.Starting, statusAtCreation);
                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.True(pipeline.CurrentRun == null, "the collector is Stopped with a table run alive");
                Assert.Equal(1, pipeline.RunCount);

                // The next Start makes a run of its own, and the sensor created during the rollback is in it.
                await collector.Start();

                var run = pipeline.CurrentRun;

                Assert.Equal(2, run.Number);

                await collector.TickTablesAsync();

                Assert.Equal(TableSensorState.Registered, late.State);
                Assert.True(late.AddRow(T0, 1.0, 2.0));

                await collector.TickTablesAsync();

                var block = Assert.Single(sender.Stored);

                Assert.Equal(run.Producer, block.Producer);
                Assert.Single(block.RowsOf("late"));
            }
        }

        // Finding 6: a buffer that cannot grow leaves AddRow returning false, the reservation given back and the buffer
        // as it was; the rows before and after go out whole.
        [Fact]
        public async Task A_failed_growth_drops_the_rows_and_leaves_the_buffer_as_it_was()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender)))
            {
                await collector.Start();

                var table = collector.CreateTableSensor("t", Quotes());
                var sensor = (TableSensor)table;
                await collector.TickTablesAsync();

                var fail = 1;
                sensor.BeforeBufferGrowthForTests = () =>
                {
                    if (Interlocked.Exchange(ref fail, 0) == 1)
                        throw new OutOfMemoryException("scripted");
                };

                // The first row grows the empty buffer: refused, counted, nothing reserved.
                Assert.False(table.AddRow(T0, 1.0, 2.0));
                Assert.Equal(1, table.RowsDropped);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);

                // A full buffer of 64 rows, then a batch with nulls that must grow it, failing once.
                var times = Enumerable.Range(0, 64).Select(i => T0.AddTicks(i)).ToArray();
                var cells = Enumerable.Range(0, 128).Select(i => (TableCell)(double)i).ToArray();
                Assert.Equal(64, table.AddRows(times, cells, 64));

                fail = 1;
                var nulls = new TableCell[] { 1.0, TableCell.Null, TableCell.Null, 2.0 };
                Assert.Equal(0, table.AddRows(new[] { T0, T0 }, nulls, 2));
                Assert.Equal(3, table.RowsDropped);

                Assert.Equal(2, table.AddRows(new[] { T0, T0 }, nulls, 2));
                await collector.TickTablesAsync();

                var rows = sender.RowsOf("t");
                Assert.Equal(66, rows.Count);
                Assert.Equal(126.0, rows[63].Values[0]);
                Assert.Null(rows[64].Values[1]);
                Assert.Null(rows[65].Values[0]);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);
            }
        }

        // Finding 7: the Queue overflow sensor published its last bar while the stop still waited for another sensor;
        // a loss reported in between went into a bar nobody would see.
        [Fact]
        public async Task A_loss_reported_after_the_overflow_sensor_stopped_is_in_the_stop_line()
        {
            var sender = new FakeTableSender();
            var logger = new CapturingLogger();

            // One row of time, bid and ask takes 24 bytes: room for one.
            using (var collector = new DataCollector(Options(sender, o => o.MaxTableBufferBytes = 24)))
            {
                collector.AddCustomLogger(logger);

                if (DataCollector.IsWindowsOS)
                    collector.Windows.AddQueueOverflow();
                else
                    collector.Unix.AddQueueOverflow();

                await collector.Start();

                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();

                var overflow = DataCollector.IsWindowsOS
                    ? ((DefaultSensorsCollection)collector.Windows).QueueOverflowSensor
                    : ((DefaultSensorsCollection)collector.Unix).QueueOverflowSensor;

                // As the stop does, sensor by sensor: the overflow sensor has published its last bar.
                await overflow.StopAsync();

                Assert.True(table.AddRow(T0, 1.0, 2.0));
                Assert.False(table.AddRow(T0, 1.0, 2.0));

                await collector.TickTablesAsync();
                await collector.Stop();

                Assert.Equal(new[] { "1" }, StopLines(logger).Select(CountIn));
            }
        }

        // Finding 8: a row refused after the table drain reported its losses (the value drain still running) was in
        // no report.
        [Fact]
        public async Task A_row_refused_after_the_table_drain_is_in_the_stop_line()
        {
            var sender = new FakeTableSender();
            var logger = new CapturingLogger();

            using (var collector = new DataCollector(Options(sender)))
            {
                collector.AddCustomLogger(logger);
                await collector.Start();

                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();

                Assert.True(table.AddRow(T0, 1.0, 2.0));

                // The table drain of a stop, while the collector is still Stopping for its value queues.
                await collector.TablePipeline.FlushAsync(CancellationToken.None);

                Assert.False(table.AddRow(T0, 3.0, 4.0));

                await collector.Stop();

                Assert.Single(sender.RowsOf("t"));
                Assert.Equal(new[] { "1" }, StopLines(logger).Select(CountIn));

                // After the stop line a refused row is counted on its sensor only.
                Assert.False(table.AddRow(T0, 5.0, 6.0));
                Assert.Equal(2, table.RowsDropped);
            }
        }

        // Finding 9: a reservation of an earlier run, still held by a paused writer, made the next run's first row
        // "buffer full", and the old writer then appended into the new run.
        [Fact]
        public async Task A_reservation_never_crosses_runs()
        {
            var sender = new FakeTableSender();

            // One row of time, bid and ask takes 24 bytes: room for one.
            using (var collector = new DataCollector(Options(sender, o => o.MaxTableBufferBytes = 24)))
            {
                await collector.Start();

                var table = collector.CreateTableSensor("t", Quotes());
                var sensor = (TableSensor)table;
                await collector.TickTablesAsync();

                var paused = Signal();
                var resume = new ManualResetEventSlim();
                var once = 0;

                sensor.BeforeAppendForTests = () =>
                {
                    if (Interlocked.Exchange(ref once, 1) == 1)
                        return;

                    paused.TrySetResult(true);
                    resume.Wait();
                };

                var oldWriter = Task.Run(() => table.AddRow(T0, 1.0, 2.0));
                await paused.Task;

                await collector.Stop();
                await collector.Start();
                await collector.TickTablesAsync();

                Assert.True(table.AddRow(T0, 3.0, 4.0), "the new run's first row found the old run's reservation");

                resume.Set();
                Assert.False(await oldWriter);

                await collector.TickTablesAsync();

                var row = Assert.Single(sender.RowsOf("t"));
                Assert.Equal(3.0, row.Values[0]);
                Assert.All(sender.Stored, b => Assert.Equal(collector.TablePipeline.CurrentRun.Producer, b.Producer));
            }
        }

        // Finding 10: one row not delivered at stop was said by two "Collector stop" lines.
        [Fact]
        public async Task A_row_not_delivered_at_stop_is_in_one_stop_line()
        {
            var sender = new FakeTableSender();
            var logger = new CapturingLogger();

            using (var collector = new DataCollector(Options(sender)))
            {
                collector.AddCustomLogger(logger);
                await collector.Start();

                var table = collector.CreateTableSensor("t", Quotes());
                await collector.TickTablesAsync();

                sender.SetDown(true);
                Assert.True(table.AddRow(T0, 1.0, 2.0));
                await collector.TickTablesAsync();
                await collector.Stop();

                Assert.Equal(1, table.RowsDropped);
                Assert.Equal(new[] { "1" }, StopLines(logger).Select(CountIn));
            }
        }

        // Start, Stop, Start, Stop, Start, Stop with writers that write through every boundary. Barriers make the overlap,
        // never time: the writers start after the first Start and do not pause until the last stop is over; a run
        // goes on until every writer had rows accepted in it; every stop is held, once its run stopped taking rows and
        // again before each pass of its final pack, until every writer has written more; and a Start comes only once
        // every writer was refused by the stopped collector. Each accepted call records the run that accepted it, and a
        // delivered row must come in a block of that run's producer: a row of one run sent under the producer of the
        // next is caught, which uniqueness, totals and order would not catch.
        [Fact]
        public async Task Rows_written_across_starts_and_stops_are_delivered_by_the_run_that_accepted_them()
        {
            const int Writers = 4;
            const int Runs = 3;
            const int MinAcceptedPerWriterPerRun = 200;
            const int CallsThroughABoundary = 50;

            var sender = new FakeTableSender();

            // Room for 2,000 rows (time and two integers, 24 bytes): a full buffer refuses rows, counted, so the writers
            // never wait and the rows to check stay few.
            using (var collector = new DataCollector(Options(sender, o => o.MaxTableBufferBytes = 2_000 * 24)))
            {
                var tables = Enumerable.Range(0, Writers).Select(i => collector.CreateTableSensor($"w{i}", Quotes(TableColumn.Int64("writer"), TableColumn.Int64("n")))).ToList();
                var pipeline = collector.TablePipeline;

                var calls = new long[Writers];
                var offered = new long[Writers];
                var refusedInEpoch = new long[Writers];
                var acceptedInRun = Enumerable.Range(0, Writers).Select(_ => new long[Runs + 1]).ToArray();
                var acceptedBy = Enumerable.Range(0, Writers).Select(_ => new Dictionary<long, TableRun>()).ToArray();
                var lastAccepting = new TableRun[Writers];
                long stopEpoch = 0;
                var writersStop = 0;

                bool AllWriters(Func<int, bool> condition) => Enumerable.Range(0, Writers).All(condition);

                void WaitFor(Func<bool> condition, string what) =>
                    Assert.True(SpinWait.SpinUntil(condition, Patience), what);

                // Blocks its caller (a stop, a pack) until every writer has made more calls: the writers write through it.
                void LetTheWritersWrite()
                {
                    var before = Enumerable.Range(0, Writers).Select(w => Interlocked.Read(ref calls[w])).ToArray();

                    WaitFor(() => AllWriters(w => Interlocked.Read(ref calls[w]) >= before[w] + CallsThroughABoundary), "a writer stopped writing");
                }

                for (var w = 0; w < Writers; w++)
                {
                    var writer = w;

                    // On the writer's own thread, inside its AddRow: the run that took the row.
                    ((TableSensor)tables[w]).OnAcceptedForTests = run => lastAccepting[writer] = run;
                }

                // Each writer on a thread of its own, never on the thread pool: a writer loops without a pause until the
                // last stop is over, and that stop needs the pool. Four such loops on pool threads, on a machine of four
                // cores, held the pool's threads and its CPU, and the pool adds no thread while the CPU is that busy:
                // this test and every test running beside it stood still for seconds (a table connection's reconnect
                // after a 1013 missed its put's wait; #1846).
                var writersStart = new ManualResetEventSlim();
                var writers = Enumerable.Range(0, Writers).Select(w => Task.Factory.StartNew(() =>
                {
                    writersStart.Wait();

                    long n = 0;

                    for (; Volatile.Read(ref writersStop) == 0; n++)
                    {
                        var epoch = Interlocked.Read(ref stopEpoch);

                        if (tables[w].AddRow(T0, w, n))
                        {
                            var run = lastAccepting[w];

                            acceptedBy[w].Add(n, run);
                            Interlocked.Increment(ref acceptedInRun[w][run.Number]);
                        }
                        else
                        {
                            // A call that began after a stop was over and was refused: the writer wrote while stopped.
                            Interlocked.Exchange(ref refusedInEpoch[w], epoch);
                        }

                        Interlocked.Increment(ref calls[w]);
                    }

                    offered[w] = n;
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

                // A stop that has ended its run's tick and loop (the run refuses rows now), and every pass of a pack,
                // a tick's or a stop's final one: held until the writers have written more.
                pipeline.OnStopWaitsForPackForTests = LetTheWritersWrite;
                pipeline.OnPackPassForTests = LetTheWritersWrite;

                var runs = new List<TableRun>();

                for (var number = 1; number <= Runs; number++)
                {
                    await collector.Start();

                    var run = pipeline.CurrentRun;

                    Assert.Equal(number, run.Number);
                    runs.Add(run);

                    // The writers start after the first Start: nothing is written before a run exists.
                    writersStart.Set();

                    // The run goes on, tick after tick, until every writer had rows accepted in it.
                    var ticks = 0;

                    do
                    {
                        await collector.TickTablesAsync();
                        Assert.True(++ticks < 100_000, $"run {number} did not accept {MinAcceptedPerWriterPerRun} rows of every writer");
                    }
                    while (!AllWriters(w => Interlocked.Read(ref acceptedInRun[w][number]) >= MinAcceptedPerWriterPerRun));

                    await collector.Stop();

                    Assert.Equal(CollectorStatus.Stopped, collector.Status);

                    // Every writer is refused by the stopped collector before the next Start (or the end).
                    var epoch = Interlocked.Increment(ref stopEpoch);

                    WaitFor(() => AllWriters(w => Interlocked.Read(ref refusedInEpoch[w]) >= epoch), "a writer did not write while the collector was stopped");
                }

                Volatile.Write(ref writersStop, 1);
                await Within(Task.WhenAll(writers), "the writers did not end");

                var runOfProducer = runs.ToDictionary(r => r.Producer);
                var deliveredByRun = new long[Runs + 1];
                var seen = Enumerable.Range(0, Writers).Select(_ => new HashSet<long>()).ToArray();

                foreach (var block in sender.Stored)
                {
                    Assert.True(runOfProducer.TryGetValue(block.Producer, out var carrier), "a block of an unknown producer");

                    for (var w = 0; w < Writers; w++)
                        foreach (var row in block.RowsOf(tables[w].Path))
                        {
                            Assert.Equal(w, (long)row.Values[0].Value);

                            var n = (long)row.Values[1].Value;

                            Assert.True(seen[w].Add(n), $"row {n} of writer {w} was delivered twice");
                            Assert.True(acceptedBy[w].TryGetValue(n, out var accepting), $"row {n} of writer {w} was delivered and never accepted");
                            Assert.True(ReferenceEquals(accepting, carrier),
                                        $"row {n} of writer {w} was accepted by run {accepting.Number} and delivered under the producer of run {carrier.Number}");

                            deliveredByRun[carrier.Number]++;
                        }
                }

                for (var w = 0; w < Writers; w++)
                {
                    Assert.Equal(offered[w], tables[w].RowsAccepted + tables[w].RowsDropped);
                    Assert.Equal(acceptedBy[w].Count, tables[w].RowsAccepted);

                    for (var number = 1; number <= Runs; number++)
                        Assert.True(acceptedInRun[w][number] >= MinAcceptedPerWriterPerRun, $"run {number} accepted {acceptedInRun[w][number]} row(s) of writer {w}");
                }

                // Run by run: every row a run accepted was delivered under its producer, or is in its ledger as lost
                // after acceptance.
                foreach (var run in runs)
                {
                    var accepted = Enumerable.Range(0, Writers).Sum(w => acceptedInRun[w][run.Number]);
                    var lostAfterAcceptance = run.Ledger.TotalOf(TableDropReason.NotDeliveredAtStop) + run.Ledger.TotalOf(TableDropReason.BacklogFull)
                                            + run.Ledger.TotalOf(TableDropReason.RefusedByServer) + run.Ledger.TotalOf(TableDropReason.PackingFailed)
                                            + run.Ledger.TotalOf(TableDropReason.RowTooLarge);

                    Assert.Equal(accepted, deliveredByRun[run.Number] + lostAfterAcceptance);
                }

                // Every loss a run counted is on a sensor too; the sensors count more only for rows refused while no
                // run took them (after a run's stop line).
                Assert.True(tables.Sum(t => t.RowsDropped) >= runs.Sum(r => r.Ledger.Total));
            }
        }

        private static TableSensorOptions Quotes(params TableColumn[] columns)
        {
            var options = new TableSensorOptions { SensorLocation = SensorLocation.Product };

            foreach (var column in columns)
                options.Columns.Add(column);

            return options;
        }

        // The number at the start of "Collector stop: N table row(s) ...".
        private static string CountIn(string line) => line.Substring("Collector stop: ".Length).Split(' ')[0];


        private sealed class CapturingLogger : ICollectorLogger
        {
            public ConcurrentQueue<string> Errors { get; } = new ConcurrentQueue<string>();

            public Action<string> OnError { get; set; }

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Error(string message)
            {
                Errors.Enqueue(message);
                OnError?.Invoke(message);
            }

            public void Error(Exception ex) => Errors.Enqueue(ex.ToString());
        }
    }
}
