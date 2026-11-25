using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tables;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMTableBlocks;
using Xunit;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Tables
{
    /// <summary>
    /// Table sensors in the managed collector (#1846, ADR-0021 points 5–7): the public API, the buffer, the tick, the
    /// registration and the answers to blocks. The tick is driven by the internal hook, never by time.
    /// </summary>
    public sealed class TableSensorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private static CollectorOptions Options(IDataSender sender, Action<CollectorOptions> tune = null)
        {
            var options = new CollectorOptions
            {
                AccessKey = "table-tests",
                DataSender = sender,
                ManualTableTick = true,
            };

            tune?.Invoke(options);

            return options;
        }

        private static TableSensorOptions Quotes(params TableColumn[] columns)
        {
            var options = new TableSensorOptions { SensorLocation = SensorLocation.Product };

            if (columns.Length == 0)
                columns = new[] { TableColumn.Double("bid"), TableColumn.Double("ask") };

            options.Columns.AddRange(columns);

            return options;
        }

        private static async Task<DataCollector> StartedAsync(CollectorOptions options)
        {
            var collector = new DataCollector(options);
            await collector.Start();
            return collector;
        }


        // ---- the public API -----------------------------------------------------------------------------------------

        [Fact]
        public async Task Rows_of_many_sensors_go_out_in_one_block_per_tick_as_given()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var sensors = Enumerable.Range(0, 50).Select(i => collector.CreateTableSensor($"quotes/S{i}", Quotes())).ToList();

                foreach (var sensor in sensors)
                {
                    Assert.True(sensor.AddRow(T0.AddSeconds(2), 1.5, 1.6));
                    Assert.True(sensor.AddRow(T0.AddSeconds(1), 2.5, (double?)null)); // out of order, a null cell
                }

                await collector.TickTablesAsync();

                var block = Assert.Single(sender.Stored);
                Assert.Equal(50, block.Block.Tables.Count);
                Assert.Equal(0, block.Sequence);

                var rows = sender.RowsOf("quotes/S7");
                Assert.Equal(2, rows.Count);
                Assert.Equal(T0.AddSeconds(2).Ticks, rows[0].Ticks);
                Assert.Equal(new double?[] { 1.5, 1.6 }, rows[0].Values);
                Assert.Equal(T0.AddSeconds(1).Ticks, rows[1].Ticks);
                Assert.Equal(new double?[] { 2.5, null }, rows[1].Values);

                Assert.All(sensors, s => Assert.Equal(TableSensorState.Registered, s.State));
                Assert.All(sensors, s => Assert.Equal(2, s.RowsAccepted));
                Assert.All(sensors, s => Assert.Equal(0, s.RowsDropped));

                // Nothing new, nothing sent.
                await collector.TickTablesAsync();
                Assert.Single(sender.Attempts);
            }
        }

        [Fact]
        public async Task Every_add_form_takes_the_same_rows()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes(TableColumn.Double("a"), TableColumn.Int64("b"), TableColumn.Double("c"), TableColumn.Int64("d")));

                Assert.True(table.AddRow(T0, 1.0, 2L, 3.0, 4L));
                Assert.True(table.AddRow(T0, new TableCell[] { 1.0, 2L, 3.0, 4L }));
                Assert.True(table.AddRow(T0, new ReadOnlySpan<TableCell>(new TableCell[] { 1.0, 2L, 3.0, 4L })));
                Assert.Equal(2, table.AddRows(new[] { T0, T0, T0 }, new TableCell[] { 1.0, 2L, 3.0, 4L, 1.0, 2L, 3.0, 4L, 9.0, 9L, 9.0, 9L }, 2));
                Assert.Equal(1, table.AddRows(new ReadOnlySpan<DateTime>(new[] { T0 }), new ReadOnlySpan<TableCell>(new TableCell[] { 1, 2, 3, 4 })));

                await collector.TickTablesAsync();

                var rows = sender.RowsOf("t");
                Assert.Equal(6, rows.Count);
                Assert.All(rows, r => Assert.Equal(new double?[] { 1, 2, 3, 4 }, r.Values));
                Assert.Equal(6, table.RowsAccepted);
            }
        }

        [Fact]
        public async Task Values_travel_bit_for_bit_and_a_local_time_is_converted_to_utc()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes(TableColumn.Double("d"), TableColumn.Int64("i")));
                var local = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Local);

                table.AddRow(local, double.NaN, long.MinValue);
                table.AddRow(T0, -0.0, long.MaxValue);
                table.AddRow(T0, double.PositiveInfinity, (long?)null);

                await collector.TickTablesAsync();

                var block = sender.Stored.Single().Block.Tables.Single();
                Assert.Equal(local.ToUniversalTime().Ticks, block.Time[0]);
                Assert.True(double.IsNaN(block.Columns[1].DoubleValues[0]));
                Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(block.Columns[1].DoubleValues[1]));
                Assert.Equal(long.MinValue, block.Columns[2].Int64Values[0]);
                Assert.True(block.Columns[2].IsNull(2));
                Assert.False(block.Columns[1].HasNulls);
            }
        }

        [Fact]
        public void Mistakes_of_the_call_throw()
        {
            using (var collector = new DataCollector(Options(new FakeTableSender())))
            {
                Assert.Throws<ArgumentNullException>(() => collector.CreateTableSensor(null, Quotes()));
                Assert.Throws<ArgumentNullException>(() => collector.CreateTableSensor("t", null));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor(" / ", Quotes()));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("a\\b", Quotes()));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor(new string('x', 1025), Quotes()));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("t", new TableSensorOptions()));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("t", Quotes(TableColumn.Double("time"))));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("t", Quotes(TableColumn.Double("a"), TableColumn.Int64("a"))));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("t", Quotes(TableColumn.Double(""))));

                var first = collector.CreateTableSensor(" q / EURUSD ", Quotes());
                Assert.Equal("q/EURUSD", first.Path);
                Assert.Same(first, collector.CreateTableSensor("q//EURUSD", Quotes()));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("q/EURUSD", Quotes(TableColumn.Double("bid"))));

                // A row of the wrong shape.
                Assert.Throws<ArgumentException>(() => first.AddRow(T0, 1.0));
                Assert.Throws<ArgumentException>(() => collector.CreateTableSensor("i", Quotes(TableColumn.Int64("n"))).AddRow(T0, 1.5));
                Assert.Throws<ArgumentException>(() => first.AddRows(new[] { T0, T0 }, new TableCell[] { 1.0, 2.0, 3.0 }, 2));
                Assert.Throws<ArgumentOutOfRangeException>(() => first.AddRows(new[] { T0 }, new TableCell[] { 1.0, 2.0 }, 2));
            }
        }

        [Fact]
        public void The_path_starts_where_the_location_says()
        {
            var options = Options(new FakeTableSender(), o =>
            {
                o.ComputerName = "host";
                o.Module = "feed";
            });

            using (var collector = new DataCollector(options))
            {
                Assert.Equal("host/feed/quotes/EURUSD", collector.CreateTableSensor("quotes/EURUSD", new TableSensorOptions { Columns = { TableColumn.Double("bid") } }).Path);
                Assert.Equal("quotes/GBPUSD", collector.CreateTableSensor("quotes/GBPUSD", Quotes()).Path);
            }
        }

        [Fact]
        public void The_extension_method_creates_through_the_capability()
        {
            IDataCollector collector = new DataCollector(Options(new FakeTableSender()));

            using (collector)
                Assert.Equal(TableSensorState.Pending, collector.CreateTableSensor("t", Quotes()).State);

            Assert.Throws<ArgumentNullException>(() => TableSensorExtensions.CreateTableSensor(null, "t", Quotes()));
        }

        [Fact]
        public void Options_are_validated()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataCollector(Options(new FakeTableSender(), o => o.TableTickPeriod = TimeSpan.FromMilliseconds(999))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataCollector(Options(new FakeTableSender(), o => o.MaxTableBufferBytes = 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DataCollector(Options(new FakeTableSender(), o => o.MaxTableBacklogBytes = -1)));

            var defaults = new CollectorOptions();
            Assert.Equal(TimeSpan.FromSeconds(10), defaults.TableTickPeriod);
            Assert.Equal(64L * 1024 * 1024, defaults.MaxTableBufferBytes);
            Assert.Equal(64L * 1024 * 1024, defaults.MaxTableBacklogBytes);
        }


        // ---- a collector that cannot send tables ---------------------------------------------------------------------

        [Fact]
        public async Task A_collector_on_http_gives_an_unavailable_sensor_and_counts_its_rows()
        {
            var options = new CollectorOptions { AccessKey = "k", Transport = CollectorTransport.Http, ManualTableTick = true };

            using (var collector = await StartedAsync(options))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                Assert.Equal(TableSensorState.Unavailable, table.State);
                Assert.Contains("HTTP", table.StateReason);
                Assert.False(table.AddRow(T0, 1.0, 2.0));
                Assert.Equal(0, table.AddRows(new[] { T0, T0 }, new TableCell[] { 1.0, 2.0, 3.0, 4.0 }, 2));
                Assert.Equal(3, table.RowsDropped);
                Assert.Equal(0, table.RowsAccepted);
            }
        }

        [Fact]
        public void A_custom_sender_without_tables_gives_an_unavailable_sensor()
        {
            using (var collector = new DataCollector(Options(new ValuesOnlySender())))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                Assert.Equal(TableSensorState.Unavailable, table.State);
                Assert.Contains("DataSender", table.StateReason);
            }
        }

        [Fact]
        public void A_collector_on_the_session_of_another_gives_an_unavailable_sensor()
        {
            var options = new CollectorOptions { AccessKey = "k", ServerAddress = "http://localhost", Port = 1, AllowPlaintextTransport = true, Transport = CollectorTransport.Session };

            using (var owner = new DataCollector(options))
            using (var second = new DataCollector(options))
            {
                Assert.Equal(TableSensorState.Pending, owner.CreateTableSensor("t", Quotes()).State);

                var table = second.CreateTableSensor("t", Quotes());
                Assert.Equal(TableSensorState.Unavailable, table.State);
                Assert.Contains("created first", table.StateReason);
            }
        }

        [Fact]
        public async Task Rows_added_while_the_collector_is_stopped_are_counted_as_dropped()
        {
            using (var collector = new DataCollector(Options(new FakeTableSender())))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                Assert.False(table.AddRow(T0, 1.0, 2.0));
                Assert.Equal(1, table.RowsDropped);

                await collector.Start();
                Assert.True(table.AddRow(T0, 1.0, 2.0));
                Assert.Equal(1, table.RowsAccepted);
            }
        }


        // ---- registration --------------------------------------------------------------------------------------------

        [Fact]
        public async Task A_registration_carries_the_schema_with_the_time_first_and_nothing_else()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                collector.CreateTableSensor("quotes/EURUSD", new TableSensorOptions
                {
                    SensorLocation = SensorLocation.Product,
                    Description = "EURUSD quotes",
                    KeepHistory = TimeSpan.FromDays(14),
                    Columns = { TableColumn.Double("bid"), TableColumn.Double("ask"), TableColumn.Int64("size") },
                });

                await collector.TickTablesAsync();

                var request = Assert.Single(Assert.Single(sender.Registrations));
                Assert.Equal("quotes/EURUSD", request.Path);
                Assert.Equal(SensorType.TableSensor, request.SensorType);
                Assert.Equal("EURUSD quotes", request.Description);
                Assert.Equal(TimeSpan.FromDays(14).Ticks, request.KeepHistory);
                Assert.Equal(new[] { "time:Time", "bid:Double", "ask:Double", "size:Int64" }, request.TableColumns.Select(c => $"{c.Name}:{c.Type}"));
                Assert.Null(request.Alerts);
                Assert.Null(request.TtlAlerts);
                Assert.Null(request.TTLs);
                Assert.Null(request.PreviousPaths);
            }
        }

        [Fact]
        public async Task A_refused_registration_is_final_and_never_sent_again()
        {
            var sender = new FakeTableSender();
            sender.RefuseRegistration("t", "another schema is registered under this path");

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                Assert.True(table.AddRow(T0, 1.0, 2.0)); // Pending: kept until the answer.

                await collector.TickTablesAsync();

                Assert.Equal(TableSensorState.Refused, table.State);
                Assert.Equal("another schema is registered under this path", table.StateReason);
                Assert.Equal(1, table.RowsDropped);
                Assert.False(table.AddRow(T0, 1.0, 2.0));
                Assert.Equal(2, table.RowsDropped);

                await collector.TickTablesAsync();
                await collector.TickTablesAsync();

                Assert.Single(sender.Registrations);
                Assert.Empty(sender.Attempts);
            }
        }

        [Fact]
        public async Task A_registration_without_an_answer_is_sent_again_and_never_final()
        {
            var sender = new FakeTableSender();
            sender.FailRegistrations(3);

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                Assert.True(table.AddRow(T0, 1.0, 2.0));

                for (var tick = 0; tick < 3; tick++)
                {
                    await collector.TickTablesAsync();
                    Assert.Equal(TableSensorState.Pending, table.State);
                    Assert.Empty(sender.Attempts);
                }

                await collector.TickTablesAsync();

                Assert.Equal(TableSensorState.Registered, table.State);
                Assert.Equal(4, sender.Registrations.Count);
                Assert.Single(sender.RowsOf("t"));
                Assert.Equal(0, table.RowsDropped);
            }
        }

        [Fact]
        public async Task Every_start_registers_again_under_a_new_producer()
        {
            var sender = new FakeTableSender();

            using (var collector = new DataCollector(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                await collector.Start();
                table.AddRow(T0, 1.0, 2.0);
                await collector.TickTablesAsync();
                var first = sender.Stored.Single().Producer;
                await collector.Stop();

                await collector.Start();
                Assert.Equal(TableSensorState.Pending, table.State);
                table.AddRow(T0, 1.0, 2.0);
                await collector.TickTablesAsync();

                Assert.Equal(2, sender.Registrations.Count);
                Assert.Equal(2, sender.Stored.Count);
                Assert.NotEqual(first, sender.Stored[1].Producer);
                Assert.Equal(0, sender.Stored[1].Sequence);
            }
        }


        // ---- bounds ---------------------------------------------------------------------------------------------------

        [Fact]
        public async Task The_buffer_bound_drops_and_counts_rows_and_a_bulk_add_is_all_or_nothing()
        {
            var sender = new FakeTableSender();

            // 24 bytes a row (time, bid, ask): room for 10 rows.
            using (var collector = await StartedAsync(Options(sender, o => o.MaxTableBufferBytes = 240)))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                for (var i = 0; i < 8; i++)
                    Assert.True(table.AddRow(T0, i, i));

                var times = new[] { T0, T0, T0 };
                Assert.Equal(0, table.AddRows(times, new TableCell[] { 1.0, 1.0, 2.0, 2.0, 3.0, 3.0 }, 3));
                Assert.True(table.AddRow(T0, 8.0, 8.0));
                Assert.True(table.AddRow(T0, 9.0, 9.0));
                Assert.False(table.AddRow(T0, 10.0, 10.0));

                Assert.Equal(10, table.RowsAccepted);
                Assert.Equal(4, table.RowsDropped);
                Assert.Equal(240, collector.TablePipeline.BufferedBytes);

                await collector.TickTablesAsync();

                Assert.Equal(10, sender.RowsOf("t").Count);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);
                Assert.True(table.AddRow(T0, 11.0, 11.0));
            }
        }

        [Fact]
        public async Task A_block_larger_than_a_frame_is_split_by_tables_and_then_by_rows()
        {
            var sender = new FakeTableSender { MaxBlockBytes = 600 };

            using (var collector = await StartedAsync(Options(sender)))
            {
                var tables = Enumerable.Range(0, 4).Select(i => collector.CreateTableSensor($"t{i}", Quotes())).ToList();
                var random = new Random(1846);

                foreach (var table in tables)
                    for (var i = 0; i < 40; i++)
                        table.AddRow(T0.AddTicks(random.Next()), random.NextDouble(), random.NextDouble());

                await collector.TickTablesAsync();

                Assert.True(sender.Stored.Count > 4, $"{sender.Stored.Count} block(s)");
                Assert.All(sender.Stored, b => Assert.True(b.Bytes.Length <= 600, $"{b.Bytes.Length} bytes"));
                Assert.All(sender.Stored, b => Assert.Equal(b.TableNames.Count, b.TableNames.Distinct().Count()));
                Assert.Equal(Enumerable.Range(0, sender.Stored.Count).Select(i => (long)i), sender.Stored.Select(b => b.Sequence));

                foreach (var table in tables)
                    Assert.Equal(40, sender.RowsOf(table.Path).Count);
            }
        }

        [Fact]
        public async Task The_block_limits_split_a_block_before_the_frame_does()
        {
            var sender = new FakeTableSender { BlockLimits = new TableBlockLimits(maxTables: 3, maxRows: 50) };

            using (var collector = await StartedAsync(Options(sender)))
            {
                var tables = Enumerable.Range(0, 7).Select(i => collector.CreateTableSensor($"t{i}", Quotes())).ToList();

                foreach (var table in tables)
                    for (var i = 0; i < 20; i++)
                        table.AddRow(T0.AddSeconds(i), i, i);

                tables[0].AddRows(Enumerable.Repeat(T0, 100).ToArray(), Enumerable.Repeat((TableCell)1.0, 200).ToArray(), 100);

                await collector.TickTablesAsync();

                Assert.All(sender.Stored, b => Assert.True(b.Block.Tables.Count <= 3 && b.Block.Header.RowCount <= 50));
                Assert.Equal(120, sender.RowsOf("t0").Count);
                Assert.All(tables.Skip(1), t => Assert.Equal(20, sender.RowsOf(t.Path).Count));
            }
        }

        [Fact]
        public async Task The_backlog_bound_drops_the_oldest_blocks_and_counts_their_rows()
        {
            var sender = new FakeTableSender();
            sender.SetDown(true);

            using (var collector = await StartedAsync(Options(sender, o => o.MaxTableBacklogBytes = 1000)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                var random = new Random(7);

                for (var tick = 0; tick < 10; tick++)
                {
                    for (var i = 0; i < 20; i++)
                        table.AddRow(T0.AddSeconds(tick * 20 + i), random.NextDouble(), random.NextDouble());

                    await collector.TickTablesAsync();
                }

                Assert.True(collector.TablePipeline.BacklogBytes <= 1000);
                Assert.True(table.RowsDropped > 0);
                Assert.Equal(200 - 20 * collector.TablePipeline.BacklogBlocks, table.RowsDropped);

                sender.SetDown(false);
                await collector.TickTablesAsync();

                // What is left goes in order, oldest first, and nothing twice.
                var stored = sender.Stored.Select(b => b.Sequence).ToList();
                Assert.Equal(stored.OrderBy(s => s), stored);
                Assert.Equal(200 - table.RowsDropped, sender.RowsOf("t").Count);
                Assert.Equal(0, collector.TablePipeline.BacklogBlocks);
            }
        }


        // ---- answers to blocks --------------------------------------------------------------------------------------

        [Fact]
        public async Task A_block_not_delivered_is_sent_again_with_the_same_producer_and_sequence()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                table.AddRow(T0, 1.0, 2.0);

                sender.AnswerNext(_ => TableBlockAck.NotDelivered("scripted 503", TimeSpan.Zero));
                await collector.TickTablesAsync();

                table.AddRow(T0, 3.0, 4.0);
                await collector.TickTablesAsync();

                var attempts = sender.Attempts;
                Assert.Equal(3, attempts.Count);
                Assert.Equal(attempts[0].Producer, attempts[1].Producer);
                Assert.Equal(new long[] { 0, 0, 1 }, attempts.Select(a => a.Sequence));
                Assert.Equal(attempts[0].Bytes, attempts[1].Bytes);
                Assert.Equal(2, sender.RowsOf("t").Count);
                Assert.Equal(0, table.RowsDropped);
            }
        }

        [Fact]
        public async Task A_duplicate_settles_the_block()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                table.AddRow(T0, 1.0, 2.0);

                sender.AnswerNext(_ => TableBlockAck.Duplicate());
                await collector.TickTablesAsync();
                await collector.TickTablesAsync();

                Assert.Single(sender.Attempts);
                Assert.Equal(0, collector.TablePipeline.BacklogBlocks);
                Assert.Equal(0, table.RowsDropped);
            }
        }

        [Fact]
        public async Task A_final_answer_drops_the_block_and_counts_its_rows()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var a = collector.CreateTableSensor("a", Quotes());
                var b = collector.CreateTableSensor("b", Quotes());

                a.AddRow(T0, 1.0, 2.0);
                b.AddRow(T0, 1.0, 2.0);
                b.AddRow(T0, 1.0, 2.0);
                sender.AnswerNext(_ => TableBlockAck.AllRefused(new[] { new TableRefusal(0, "a", "SensorCreatedAfterBlock"), new TableRefusal(1, "b", "SensorCreatedAfterBlock") }, 2));
                await collector.TickTablesAsync();

                a.AddRow(T0, 1.0, 2.0);
                sender.AnswerNext(_ => TableBlockAck.Invalid("invalid block: ChecksumMismatch"));
                await collector.TickTablesAsync();
                await collector.TickTablesAsync();

                Assert.Equal(2, sender.Attempts.Count);
                Assert.Equal(2, a.RowsDropped);
                Assert.Equal(2, b.RowsDropped);
                Assert.Equal(TableSensorState.Registered, a.State);
                Assert.Equal(0, collector.TablePipeline.BacklogBlocks);
            }
        }

        [Fact]
        public async Task Refused_tables_of_a_stored_block_are_counted_and_acted_on()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var removed = collector.CreateTableSensor("removed", Quotes());
                var reshaped = collector.CreateTableSensor("reshaped", Quotes());
                var fine = collector.CreateTableSensor("fine", Quotes());

                removed.AddRow(T0, 1.0, 2.0);
                reshaped.AddRow(T0, 1.0, 2.0);
                fine.AddRow(T0, 1.0, 2.0);

                sender.AnswerNext(block => TableBlockAck.Stored(new[]
                {
                    new TableRefusal(block.TableNames.ToList().IndexOf("removed"), "removed", "UnknownSensor"),
                    new TableRefusal(block.TableNames.ToList().IndexOf("reshaped"), "reshaped", "SchemaIdMismatch"),
                }, 2));
                await collector.TickTablesAsync();

                Assert.Equal(1, removed.RowsDropped);
                Assert.Equal(1, reshaped.RowsDropped);
                Assert.Equal(0, fine.RowsDropped);
                Assert.Equal(TableSensorState.Pending, removed.State);
                Assert.Equal(TableSensorState.Refused, reshaped.State);
                Assert.Contains("SchemaIdMismatch", reshaped.StateReason);

                // The removed sensor is registered again; the reshaped one never is.
                removed.AddRow(T0, 3.0, 4.0);
                await collector.TickTablesAsync();

                Assert.Equal(TableSensorState.Registered, removed.State);
                Assert.Equal(new[] { "removed" }, sender.Registrations.Last().Select(r => r.Path));
                Assert.Equal(new double?[] { 3.0, 4.0 }, sender.Stored.Last().RowsOf("removed").Single().Values);
            }
        }


        // ---- stop ----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Stop_sends_what_the_collector_holds_at_once()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());

                await collector.TickTablesAsync(); // registered

                table.AddRow(T0, 1.0, 2.0);
                table.AddRow(T0, 3.0, 4.0);

                await collector.Stop();

                Assert.Equal(2, sender.RowsOf("t").Count);
                Assert.Equal(0, table.RowsDropped);
            }
        }

        [Fact]
        public async Task What_a_stop_cannot_deliver_is_dropped_and_counted()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var registered = collector.CreateTableSensor("registered", Quotes());
                await collector.TickTablesAsync();

                var pending = collector.CreateTableSensor("pending", Quotes());
                sender.FailRegistrations(100);
                sender.SetDown(true);

                registered.AddRow(T0, 1.0, 2.0);
                await collector.TickTablesAsync(); // a block in the backlog
                registered.AddRow(T0, 1.0, 2.0);
                pending.AddRow(T0, 1.0, 2.0);

                await collector.Stop();

                Assert.Equal(2, registered.RowsDropped);
                Assert.Equal(1, pending.RowsDropped);
                Assert.Equal(0, collector.TablePipeline.BacklogBlocks);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);
            }
        }


        // ---- threads -------------------------------------------------------------------------------------------------

        [Fact]
        public async Task A_discard_while_a_pack_holds_the_buffer_leaves_the_sensor_writable()
        {
            var sender = new FakeTableSender();

            using (var collector = await StartedAsync(Options(sender)))
            {
                var table = collector.CreateTableSensor("t", Quotes());
                var sensor = (TableSensor)table;
                await collector.TickTablesAsync(); // registered

                // A pack takes the rows and holds the buffer (the spare is away while it packs).
                table.AddRow(T0, 1.0, 2.0);
                var heldByPack = sensor.TakeRows();
                Assert.NotNull(heldByPack);

                // Meanwhile the delivery loop discards the sensor's rows (a refusal) and the
                // application goes on adding rows.
                table.AddRow(T0, 3.0, 4.0);
                Assert.Equal(1, sensor.DiscardRows(TableDropReason.SensorRefused));
                Assert.True(table.AddRow(T0, 5.0, 6.0));

                // The pack gives its buffer back; the next take sees exactly the last row.
                sensor.ReturnRows(heldByPack);
                var next = sensor.TakeRows();
                Assert.Equal(1, next.Count);
                Assert.Equal(5.0, BitConverter.Int64BitsToDouble(next.Values(0)[0]));
                sensor.ReturnRows(next);

                Assert.True(table.AddRow(T0, 7.0, 8.0));
            }
        }


        [Fact]
        public async Task Rows_added_from_many_threads_while_ticks_run_arrive_once_each()
        {
            var sender = new FakeTableSender();
            const int Threads = 8;
            const int RowsPerThread = 20_000;

            using (var collector = await StartedAsync(Options(sender)))
            {
                var tables = Enumerable.Range(0, 4).Select(i => collector.CreateTableSensor($"t{i}", Quotes(TableColumn.Int64("thread"), TableColumn.Int64("n")))).ToList();
                await collector.TickTablesAsync();

                var start = new ManualResetEventSlim();
                var writers = Enumerable.Range(0, Threads).Select(thread => Task.Run(() =>
                {
                    start.Wait();
                    var table = tables[thread % tables.Count];
                    var batch = new TableCell[20];
                    var times = new DateTime[10];

                    for (var n = 0; n < RowsPerThread; n += 10)
                    {
                        if (n % 20 == 0)
                        {
                            for (var i = 0; i < 10; i++)
                            {
                                Assert.True(table.AddRow(T0, thread, n + i));
                            }
                        }
                        else
                        {
                            for (var i = 0; i < 10; i++)
                            {
                                times[i] = T0;
                                batch[2 * i] = thread;
                                batch[2 * i + 1] = n + i;
                            }

                            Assert.Equal(10, table.AddRows(times, batch, 10));
                        }
                    }
                })).ToArray();

                start.Set();

                var all = Task.WhenAll(writers);

                while (!all.IsCompleted)
                    await collector.TickTablesAsync();

                await all;
                await collector.TickTablesAsync();

                var seen = new HashSet<long>();

                foreach (var table in tables)
                    foreach (var row in sender.RowsOf(table.Path))
                        Assert.True(seen.Add((long)row.Values[0].Value * RowsPerThread + (long)row.Values[1].Value), "a row arrived twice");

                Assert.Equal(Threads * RowsPerThread, seen.Count);
                Assert.Equal(0, collector.TablePipeline.BufferedBytes);
            }
        }


        private sealed class ValuesOnlySender : IDataSender
        {
            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendDataAsync(IEnumerable<HSMSensorDataObjects.SensorValueRequests.SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendPriorityDataAsync(IEnumerable<HSMSensorDataObjects.SensorValueRequests.SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendFileAsync(HSMSensorDataObjects.SensorValueRequests.FileSensorValue file, CancellationToken token) => default;

            public void Dispose() { }
        }
    }
}
