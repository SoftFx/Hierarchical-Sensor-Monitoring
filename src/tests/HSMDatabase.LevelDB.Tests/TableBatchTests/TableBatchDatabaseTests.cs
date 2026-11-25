using System.Text;
using System.Text.RegularExpressions;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using LevelDB;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.TableBatchTests
{
    /// <summary>
    /// The batch store of table sensors (#1850): one hour committed after its parts, crash points of the write, the hours
    /// and watermarks back after a restart and rebuilt from the marks, week databases flushed when their week closes and
    /// opened within a bound, dropped weeks, products apart, the region of moved batches, LevelDB's compactions.
    /// </summary>
    public sealed class TableBatchDatabaseTests : IDisposable
    {
        // A Monday: the week of the first hours.
        private static readonly long Week0 = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private const long Hour = TimeSpan.TicksPerHour;
        private const long Week = 7 * TimeSpan.TicksPerDay;

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-table-batches-{Guid.NewGuid():N}");
        private readonly Guid _product = Guid.NewGuid();
        private readonly Guid _sensorA = Guid.NewGuid();
        private readonly Guid _sensorB = Guid.NewGuid();
        private readonly Guid _producer = Guid.NewGuid();
        private readonly List<TableBatchDatabase> _stores = [];


        public void Dispose()
        {
            foreach (var store in _stores)
                store.Dispose();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
        }


        [Fact]
        public void Nothing_is_created_or_opened_before_the_first_write()
        {
            var store = Open();

            Assert.Empty(store.GetHours(_product));
            Assert.False(store.IsHourMerged(_product, Week0));
            Assert.Empty(store.GetWatermarks(_product));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 4, [Week0]));
            Assert.Empty(store.GetProducts());
            Assert.False(store.IsOpened);
            Assert.False(Directory.Exists(_root));
        }

        [Fact]
        public void An_hour_is_read_back_by_sensor_in_its_order_with_its_ranges_and_watermarks()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 10), [Part(4, (_sensorA, 100, 2), (_sensorB, 500, 1))]);

            Assert.True(store.IsHourMerged(_product, Week0));
            var hour = Assert.Single(store.GetHours(_product));
            var range = Assert.Single(hour.Classes);
            Assert.Equal(4, range.PeriodWeeks);
            Assert.Equal(Week0 + 100, range.MinTime);
            Assert.Equal(Week0 + 500 + 9, range.MaxTime);
            Assert.Equal(3, range.Batches);
            Assert.Equal(10L, store.GetWatermarks(_product)[_producer]);

            var read = store.ReadSensor(_product, _sensorA, 4, [Week0]);
            Assert.Equal([0, 1], read.Select(r => r.Ordinal));
            Assert.All(read, r => Assert.Equal(_sensorA, r.SensorId));
            Assert.Equal(Payload(_sensorA, Week0 + 100, 0), read[0].Batch);
            Assert.Equal(Payload(_sensorA, Week0 + 110, 1), read[1].Batch);

            // Another class of the same week has no database: nothing, and nothing created.
            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 1)));
        }

        [Fact]
        public void An_hour_of_two_period_classes_goes_to_two_databases()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]);

            Assert.Equal([new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4)], store.GetWeeks(_product));
            Assert.Single(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 4, [Week0]));
            Assert.Single(store.ReadSensor(_product, _sensorB, 4, [Week0]));
            Assert.Equal([1, 4], store.GetHours(_product).Single().Classes.Select(c => c.PeriodWeeks));
        }

        [Fact]
        public void Hours_of_two_weeks_go_to_two_week_databases_and_a_read_joins_them_in_hour_order()
        {
            var store = Open();
            long late = Week0 + Week - Hour, next = Week0 + Week;
            store.WriteHour(_product, Commit(late), [Part(4, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(next), [Part(4, (_sensorA, 0, 1))]);

            Assert.Equal([Week0, Week0 + Week], store.GetWeeks(_product).Select(w => w.WeekStartTicks));
            Assert.Equal([late, next], store.ReadSensor(_product, _sensorA, 4, [next, late]).Select(r => r.HourTicks));
        }

        [Fact]
        public void A_second_write_of_a_committed_hour_changes_nothing()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 5), [Part(4, (_sensorA, 0, 2))]);
            store.WriteHour(_product, Commit(Week0, sequence: 99), [Part(4, (_sensorA, 0, 3))]);

            Assert.Equal(2, store.ReadSensor(_product, _sensorA, 4, [Week0]).Count);
            Assert.Equal(5L, store.GetWatermarks(_product)[_producer]);
        }

        // === Crash points of the hour write ===

        [Theory]
        [InlineData("part-written", 1, false)]   // the first of two parts written, the second not
        [InlineData("before-commit", 2, false)]  // every part written, the commit not
        [InlineData("committed", 2, true)]       // committed
        public void A_crash_at_any_point_of_the_hour_write_leaves_the_hour_merged_or_not_and_a_repeat_completes_it_once(string point, int partsOnDisk, bool merged)
        {
            var store = Open();
            store.Checkpoint = p =>
            {
                if (p == point)
                    throw new CheckpointException(p);
            };

            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), TwoParts()));
            store.Dispose();

            var reopened = Open();
            Assert.Equal(merged, reopened.IsHourMerged(_product, Week0));
            Assert.Equal(merged ? 1 : 0, reopened.GetHours(_product).Count);
            Assert.Equal(partsOnDisk, reopened.GetWeeks(_product).Count);

            // The merge runs the hour again: the parts already written are skipped, the hour is committed once.
            reopened.WriteHour(_product, Commit(Week0), TwoParts());
            Assert.True(reopened.IsHourMerged(_product, Week0));
            Assert.Equal(2, reopened.ReadSensor(_product, _sensorA, 1, [Week0]).Count);
            Assert.Equal(1, reopened.ReadSensor(_product, _sensorB, 4, [Week0]).Count);
            Assert.Equal([1, 4], reopened.GetHours(_product).Single().Classes.Select(c => c.PeriodWeeks));
        }

        [Fact]
        public void A_store_that_stopped_without_closing_its_databases_reopens_with_every_committed_hour()
        {
            var store = Open();
            for (int h = 0; h < 5; h++)
                store.WriteHour(_product, Commit(Week0 + h * Hour, sequence: h), [Part(4, (_sensorA, 0, 1))]);

            // Closed with the memtables not flushed: every hour is only in the databases' logs, as after a stop that was not a
            // clean dispose.
            var week = store.WeekPath(_product, Week0, 4);
            store.CloseWithoutFlush();
            Assert.Contains(Directory.GetFiles(week, "*.log"), f => new FileInfo(f).Length > 0);
            Assert.Empty(Directory.GetFiles(week, "*.ldb"));

            var reopened = Open();
            Assert.Equal(5, reopened.GetHours(_product).Count);
            Assert.Equal(4L, reopened.GetWatermarks(_product)[_producer]);
            Assert.Equal(5, reopened.ReadSensor(_product, _sensorA, 4, Enumerable.Range(0, 5).Select(h => Week0 + h * Hour).ToArray()).Count);
        }

        [Fact]
        public void The_hours_and_the_newest_watermarks_come_back_after_a_restart()
        {
            var store = Open();
            var other = Guid.NewGuid();
            store.WriteHour(_product, Commit(Week0, sequence: 3) with { Watermarks = new Dictionary<Guid, long> { [_producer] = 3, [other] = 7 } }, [Part(4, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Hour, sequence: 9) with { UnreadableBlocks = [42, 43] }, [Part(4, (_sensorA, 0, 1))]);
            store.Dispose();

            var reopened = Open();
            var hours = reopened.GetHours(_product);
            Assert.Equal([Week0, Week0 + Hour], hours.Select(h => h.HourTicks));
            Assert.Equal([42L, 43], hours[1].UnreadableBlocks);

            // The newest commit holds the product's watermarks: the producer missing from it is not carried over by the store.
            Assert.Equal(new Dictionary<Guid, long> { [_producer] = 9 }, reopened.GetWatermarks(_product));
            Assert.Equal([_product], reopened.GetProducts());
        }

        [Fact]
        public void Missing_merged_hours_are_rebuilt_from_the_marks_and_an_hour_with_a_part_missing_is_not()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), TwoParts());
            store.WriteHour(_product, Commit(Week0 + Hour, sequence: 2), TwoParts());

            // The third hour stops after its first part.
            store.Checkpoint = p => { if (p == "part-written") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0 + 2 * Hour, sequence: 3), TwoParts()));
            store.Dispose();

            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName), recursive: true);

            var reopened = Open();
            Assert.Equal([Week0, Week0 + Hour], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal(2L, reopened.GetWatermarks(_product)[_producer]);
            Assert.Equal(2, reopened.Statistics.HoursRebuilt);
        }

        [Fact]
        public void The_commit_time_of_an_hour_survives_a_restart()
        {
            var committedAt = new DateTime(2026, 11, 2, 1, 7, 0, DateTimeKind.Utc);
            var store = Open(clock: () => committedAt);
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            Assert.Equal(committedAt.Ticks, store.GetHours(_product).Single().CommitTicks);
            store.Dispose();

            var reopened = Open(clock: () => committedAt.AddDays(3));
            Assert.Equal(committedAt.Ticks, reopened.GetHours(_product).Single().CommitTicks);
        }

        [Fact]
        public void A_rebuilt_merged_hours_database_takes_the_time_of_the_rebuild_as_the_commit_time()
        {
            var committedAt = new DateTime(2026, 11, 2, 1, 7, 0, DateTimeKind.Utc);
            var store = Open(clock: () => committedAt);
            store.WriteHour(_product, Commit(Week0, sequence: 4), [Part(4, (_sensorA, 0, 1))]);
            store.Dispose();

            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName), recursive: true);

            var rebuiltAt = committedAt.AddDays(2);
            var reopened = Open(clock: () => rebuiltAt);
            var hour = Assert.Single(reopened.GetHours(_product));
            Assert.Equal(rebuiltAt.Ticks, hour.CommitTicks);
            Assert.Equal(4L, reopened.GetWatermarks(_product)[_producer]);
            reopened.Dispose();

            // Written to the rebuilt database: the next open reads the rebuild time back.
            Assert.Equal(rebuiltAt.Ticks, Open(clock: () => rebuiltAt.AddDays(1)).GetHours(_product).Single().CommitTicks);
        }

        [Fact]
        public void A_damaged_merged_hours_database_is_put_aside_and_rebuilt()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), [Part(4, (_sensorA, 0, 1))]);
            store.Dispose();

            // A record that is not one: the open puts the database aside and rebuilds it from the marks.
            var hoursPath = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName);
            using (var db = new DB(hoursPath, new Options()))
                db.Put([0, 0, 0, 0, 0, 0, 0, 1], [9, 9, 9], new WriteOptions());

            var reopened = Open();
            Assert.Equal([Week0], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(hoursPath), TableBatchDatabase.HoursName + ".damaged-*"));
        }

        // === Weeks: flush, bound, drop ===

        [Fact]
        public void A_week_database_is_flushed_when_a_later_week_is_written_so_it_has_no_log_to_replay()
        {
            var store = Open();
            for (int h = 0; h < 3; h++)
                store.WriteHour(_product, Commit(Week0 + h * Hour), [Part(4, (_sensorA, 0, 1))]);

            var week0 = store.WeekPath(_product, Week0, 4);
            Assert.Contains(Directory.GetFiles(week0, "*.log"), f => new FileInfo(f).Length > 0);

            store.WriteHour(_product, Commit(Week0 + Week), [Part(4, (_sensorA, 0, 1))]);

            Assert.All(Directory.GetFiles(week0, "*.log"), f => Assert.Equal(0, new FileInfo(f).Length));
            Assert.Equal(1, store.Statistics.WeeksFlushed);
        }

        [Fact]
        public void A_read_across_more_weeks_than_the_bound_returns_every_batch_and_leaves_the_bound_respected()
        {
            var store = Open();
            store.MaxOpenDatabases = 2;
            var hours = new List<long>();

            for (int w = 0; w < 6; w++)
            {
                long hour = Week0 + w * Week + 5 * Hour;
                hours.Add(hour);
                store.WriteHour(_product, Commit(hour, sequence: w), [Part(4, (_sensorA, 0, 2))]);
            }

            Assert.True(store.OpenDatabases <= 2, $"{store.OpenDatabases} open after the writes");

            var read = store.ReadSensor(_product, _sensorA, 4, hours);

            Assert.Equal(12, read.Count);
            Assert.Equal(hours.SelectMany(h => new[] { h, h }), read.Select(r => r.HourTicks));
            Assert.True(store.OpenDatabases <= 2, $"{store.OpenDatabases} open after the read");
            Assert.True(store.Statistics.DatabasesClosed >= 4);

            // The bound lowered: closed at once down to it, the week being written stays.
            store.MaxOpenDatabases = 1;
            Assert.Equal(1, store.OpenDatabases);
        }

        [Fact]
        public void A_dropped_week_is_gone_from_disk_from_the_hours_and_from_the_ranges()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Hour), [Part(1, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Week), [Part(1, (_sensorA, 0, 1))]);
            Assert.Single(store.ReadSensor(_product, _sensorA, 1, [Week0]));

            store.DropWeek(_product, new TableBatchWeek(Week0, 1));

            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 1)));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0, Week0 + Hour]));

            // The first hour keeps its other class; the second had only the dropped one.
            var hours = store.GetHours(_product);
            Assert.Equal([Week0, Week0 + Week], hours.Select(h => h.HourTicks));
            Assert.Equal([4], hours[0].Classes.Select(c => c.PeriodWeeks));
            Assert.False(store.IsHourMerged(_product, Week0 + Hour));
            store.Dispose();

            var reopened = Open();
            Assert.Equal([Week0, Week0 + Week], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal([4], reopened.GetHours(_product)[0].Classes.Select(c => c.PeriodWeeks));
            Assert.Equal([new TableBatchWeek(Week0, 4), new TableBatchWeek(Week0 + Week, 1)], reopened.GetWeeks(_product));
        }

        [Fact]
        public void Two_products_do_not_see_each_other()
        {
            var store = Open();
            var other = Guid.NewGuid();
            store.WriteHour(_product, Commit(Week0, sequence: 1), [Part(4, (_sensorA, 0, 1))]);
            store.WriteHour(other, Commit(Week0 + Hour, sequence: 2), [Part(4, (_sensorA, 0, 3))]);

            Assert.Equal([Week0], store.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal([Week0 + Hour], store.GetHours(other).Select(h => h.HourTicks));
            Assert.Single(store.ReadSensor(_product, _sensorA, 4, [Week0, Week0 + Hour]));
            Assert.Equal(3, store.ReadSensor(other, _sensorA, 4, [Week0, Week0 + Hour]).Count);
            Assert.Equal(1L, store.GetWatermarks(_product)[_producer]);
            Assert.Equal(2L, store.GetWatermarks(other)[_producer]);

            store.DropWeek(other, new TableBatchWeek(Week0, 4));
            Assert.Single(store.ReadSensor(_product, _sensorA, 4, [Week0]));
        }

        [Fact]
        public void The_region_of_moved_batches_lies_after_every_hour_key_and_a_read_finds_both_regions()
        {
            Assert.True(TableBatchDatabase.MovedIndexKey(_sensorA, 0, 0, 0).AsSpan().SequenceCompareTo(TableBatchDatabase.HourKey(DateTime.MaxValue.Ticks, 0xFF)) > 0);

            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + 2 * Hour), [Part(4, (_sensorB, 0, 1))]);

            // What a move (#1850 PR 4) writes: sensor A's batches of hour 1 of the week, from another class.
            store.WriteMoved(_product, new TableBatchWeek(Week0, 4), Week0 + Hour, [Batch(_sensorA, Week0 + Hour + 7, 0), Batch(_sensorA, Week0 + Hour + 17, 1)]);

            var read = store.ReadSensor(_product, _sensorA, 4, [Week0, Week0 + Hour, Week0 + 2 * Hour]);
            Assert.Equal([(Week0, false, 0), (Week0 + Hour, true, 0), (Week0 + Hour, true, 1)], read.Select(r => (r.HourTicks, r.Moved, r.Ordinal)));
            Assert.Equal(Payload(_sensorA, Week0 + Hour + 17, 1), read[2].Batch);

            // Only the hours asked for.
            Assert.Single(store.ReadSensor(_product, _sensorA, 4, [Week0]));
        }

        // === LevelDB ===

        [Fact]
        public void Merged_hours_cause_no_real_compaction()
        {
            // Small memtables make LevelDB flush often; with keys that only grow it moves the files down a level ("Moved #n")
            // instead of merging them ("Compacting n@l + m@l+1 files").
            var store = Open(writeBufferSize: 64 * 1024);

            for (int h = 0; h < 7 * 24 + 10; h++)
                store.WriteHour(_product, Commit(Week0 + h * Hour, sequence: h),
                    [Part(4, Enumerable.Range(0, 20).Select(s => (new Guid(s + 1, 0, 0, new byte[8]), (long)s, 3)).ToArray())]);

            store.Dispose();

            var productFolder = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"));
            var log = string.Join("\n", Directory.GetDirectories(productFolder).Select(ReadLog));

            Assert.Contains("Level-0 table", log);
            Assert.Empty(Regex.Matches(log, @"Compacting \d+@\d+ \+ \d+@\d+ files"));
        }

        [Fact]
        public void An_open_is_retried_on_the_transient_dbtmp_failure_and_no_other()
        {
            int calls = 0;
            var store = Open(open: (path, options) =>
            {
                if (path.EndsWith(TableBatchDatabase.HoursName) && ++calls <= 2)
                    throw new Exception($"IO error: {path}\\000001.dbtmp: Unable to remove the file to be replaced");
                return new DB(path, options);
            });

            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            Assert.Equal(3, calls);

            int failing = 0;
            var broken = Open(open: (path, options) =>
            {
                failing++;
                throw new Exception("IO error: the disk is gone");
            }, root: Path.Combine(_root, "other"));

            Assert.Throws<TableBatchStoreException>(() => broken.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]));
            Assert.Equal(1, failing);
            Assert.Equal(1, broken.Statistics.WriteFailures);
        }


        // === Helpers ===

        private TableBatchDatabase Open(long writeBufferSize = 8L * 1024 * 1024, Func<string, Options, DB> open = null, string root = null, Func<DateTime> clock = null)
        {
            var store = new TableBatchDatabase(root ?? _root, writeBufferSize, open);
            if (clock is not null)
                store.Clock = clock;
            _stores.Add(store);
            return store;
        }

        private TableBatchHourCommit Commit(long hour, long sequence = 1) => new()
        {
            HourTicks = hour,
            FirstBlockSequence = sequence,
            LastBlockSequence = sequence,
            BlocksMerged = 1,
            Watermarks = new Dictionary<Guid, long> { [_producer] = sequence },
        };

        private TableBatchHourPart[] TwoParts() => [Part(1, (_sensorA, 0, 2)), Part(4, (_sensorB, 0, 1))];

        // (sensor, first time offset within the hour, batch count): batches 10 ticks apart, 10 rows each.
        private static TableBatchHourPart Part(int period, params (Guid Sensor, long Offset, int Count)[] sensors) =>
            new(period, sensors.SelectMany(s => Enumerable.Range(0, s.Count).Select(i => Batch(s.Sensor, Week0 + s.Offset + 10 * i, i))).ToList());

        private static TableBatchToWrite Batch(Guid sensor, long first, int i) => new(sensor, first, first + 9, 10, Payload(sensor, first, i));

        private static byte[] Payload(Guid sensor, long first, int i) => [.. sensor.ToByteArray(), .. BitConverter.GetBytes(first), (byte)i];

        private static string ReadLog(string folder)
        {
            var text = new StringBuilder();

            foreach (var file in Directory.GetFiles(folder, "LOG*"))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text.AppendLine(reader.ReadToEnd());
            }

            return text.ToString();
        }
    }
}
