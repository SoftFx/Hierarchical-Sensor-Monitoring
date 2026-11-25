using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using LevelDB;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.TableBatchTests
{
    // The store's primitives of the move between period classes and of the removal (#1850 parts 4c and 4d): the copy of a
    // sensor's hours, the delete of them with the deleted-bytes counter, the count from the index, the compaction, and the
    // durable files of the compaction list and of the move in flight. No job calls them yet; tests 1 to 4 of the design
    // and the crash points of each new durable write.
    public sealed partial class TableBatchDatabaseTests
    {
        private static readonly int PayloadLength = Payload(Guid.Empty, 0, 0).Length;

        // === CopySensorHours ===

        [Fact]
        public void A_copy_repeated_from_another_split_leaves_exactly_the_second_copy_in_read_order_with_the_mark_only_where_missing()
        {
            var store = Open();
            var sensorS = _sensorA;
            var sensorR = _sensorB;
            long hour = Week0 + Hour;
            var (p1, p4, p5) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4), new TableBatchWeek(Week0, 5));

            // S's three batches of the hour in P1; in P4 one batch of S and one of R in the hour region (a split the store
            // allows, written for the test: two sources of the same hour with different cuts).
            store.WriteHour(_product, Commit(hour), [Part(1, (sensorS, Hour, 3)), Part(4, (sensorS, Hour + 100, 1), (sensorR, Hour, 1))]);
            var mark = store.ReadMark(_product, p1, hour);

            // The first copy into P5, a new database: three batches, the mark of the source.
            Assert.Equal(new TableBatchSensorHours(3, 30, 3 * PayloadLength), store.CopySensorHours(_product, sensorS, p1, 5, hour, hour + Hour));
            Assert.Equal(mark, store.ReadMark(_product, p5, hour));

            // P4 gets S's three batches of P1 in its moved region beside its own: its mark (the same record) stays.
            store.CopySensorHours(_product, sensorS, p1, 4, hour, hour + Hour);
            Assert.Equal(mark, store.ReadMark(_product, p4, hour));
            var fromP4 = store.ReadSensor(_product, sensorS, 4, [hour]);
            Assert.Equal([(false, 0), (true, 0), (true, 1), (true, 2)], fromP4.Select(r => (r.Moved, r.Ordinal)));

            // The copy of P4's four batches into P5 replaces the three there: exactly the second copy, in P4's read order,
            // numbered again; the three deleted count in P5's counter.
            Assert.Equal(new TableBatchSensorHours(4, 40, 4 * PayloadLength), store.CopySensorHours(_product, sensorS, p4, 5, hour, hour + Hour));
            var fromP5 = store.ReadSensor(_product, sensorS, 5, [hour], out var failed);
            Assert.Equal(0, failed);
            Assert.Equal([0, 1, 2, 3], fromP5.Select(r => r.Ordinal));
            Assert.All(fromP5, r => Assert.True(r.Moved));
            Assert.Equal(fromP4.Select(r => r.Batch), fromP5.Select(r => r.Batch));
            Assert.Equal(fromP4.Select(r => (r.FirstTime, r.LastTime, r.RowCount)), fromP5.Select(r => (r.FirstTime, r.LastTime, r.RowCount)));
            Assert.Equal(mark, store.ReadMark(_product, p5, hour));
            Assert.Equal(3L * PayloadLength, store.GetDeletedShare(_product, p5).DeletedBytes);

            // The sources are not changed; R is not copied.
            Assert.Equal(3, store.ReadSensor(_product, sensorS, 1, [hour]).Count);
            Assert.Empty(store.ReadSensor(_product, sensorR, 5, [hour]));
        }

        [Fact]
        public void A_copy_with_nothing_of_the_sensor_in_the_source_writes_nothing_creates_no_database_and_changes_no_generation()
        {
            var store = Open();
            long hour = Week0 + Hour;
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));
            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorB, Hour, 1))]);

            (long, long, long) Generations() => (store.GetGeneration(_product), store.GetGeneration(_product, _sensorA), store.GetGeneration(_product, _sensorB));
            var before = Generations();

            // No batch of A in the source (P1 holds only B's), a source database that does not exist (P2), a product without a folder.
            Assert.Equal(default, store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour));
            Assert.Equal(default, store.CopySensorHours(_product, _sensorA, new TableBatchWeek(Week0, 2), 4, Week0, Week0 + Week));
            Assert.Equal(default, store.CopySensorHours(Guid.NewGuid(), _sensorA, p1, 4, hour, hour + Hour));

            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 4)));
            Assert.Equal([p1], store.GetWeeks(_product));
            Assert.Equal(before, Generations());

            // Nor into a database that exists and holds nothing of A.
            store.WriteHour(_product, Commit(hour + Hour, 2), [Part(4, (_sensorB, 2 * Hour, 1))]);
            before = Generations();
            Assert.Equal(default, store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour));
            Assert.Equal(before, Generations());
            Assert.Null(store.ReadMark(_product, p4, hour));
        }

        [Fact]
        public void A_copied_mark_commits_the_hour_in_a_rebuild_of_the_hours_after_the_class_it_came_from_was_dropped()
        {
            var store = Open();
            long hour = Week0 + Hour;
            var p1 = new TableBatchWeek(Week0, 1);
            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorA, Hour, 2))]);
            var rows = store.ReadSensor(_product, _sensorA, 1, [hour]).Select(r => r.Batch).ToList();

            store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour);
            store.DropWeek(_product, p1);
            store.Dispose();
            DeleteHours();

            // The only mark left is the copy's: the record names P1, which the record of drops names, so the hour is committed.
            var reopened = Open();
            Assert.True(reopened.IsHourMerged(_product, hour));
            Assert.Equal([1], reopened.GetHour(_product, hour).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(rows, reopened.ReadSensor(_product, _sensorA, 4, [hour]).Select(r => r.Batch));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_rebuild_of_lost_placements_takes_the_source_after_a_copy_with_or_without_the_switch_and_reads_the_rows_of_before_the_move(bool switched)
        {
            var store = Open();
            long h0 = Week0, h1 = Week0 + Hour, h2 = Week0 + 2 * Hour;
            var p1 = new TableBatchWeek(Week0, 1);

            // A in P1 for hours 0 and 1, then in P5 (its period changed); B in P1 throughout.
            store.WriteHour(_product, Commit(h0, 1), [Part(1, (_sensorA, 0, 2), (_sensorB, 0, 1))]);
            store.WriteHour(_product, Commit(h1, 2), [Part(1, (_sensorA, Hour, 1), (_sensorB, Hour, 1))]);
            store.WriteHour(_product, Commit(h2, 3), [Part(5, (_sensorA, 2 * Hour, 1)), Part(1, (_sensorB, 2 * Hour, 1))]);
            var placement = new TablePlacement(5, h0, [new TablePlacementSegment(h0, h2, 1)]);
            store.SetPlacement(_product, _sensorA, placement);
            var before = ReadByPlacement(store, _sensorA, [h0, h1, h2]).Select(r => r.Batch).ToList();

            store.CopySensorHours(_product, _sensorA, p1, 5, h0, h2);
            if (switched)
                store.SetPlacement(_product, _sensorA, placement.WithoutHours(h0, h2));
            Assert.Equal(before, ReadByPlacement(store, _sensorA, [h0, h1, h2]).Select(r => r.Batch));

            store.Dispose();
            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.PlacementsName), recursive: true);

            // The hour region (the source, complete until it is deleted) wins over the copy, before and after the switch.
            var reopened = Open();
            Assert.Equal(placement, reopened.GetPlacement(_product, _sensorA));
            Assert.Equal(before, ReadByPlacement(reopened, _sensorA, [h0, h1, h2]).Select(r => r.Batch));
        }

        [Theory]
        [InlineData(5, 4)] // the second copy in a higher class: the rebuild takes the first one (P4)
        [InlineData(2, 2)] // in a lower class: the rebuild takes the second one (P2)
        public void A_rebuild_of_lost_placements_with_two_moved_copies_of_an_hour_takes_one_of_them_and_reads_every_row(int secondTarget, int expected)
        {
            var store = Open();
            long hour = Week0 + Hour;
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));
            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorA, Hour, 3), (_sensorB, Hour, 1))]);
            var rows = store.ReadSensor(_product, _sensorA, 1, [hour]).Select(r => r.Batch).ToList();

            // A first move P1 -> P4 done (copied, source deleted), a second one P4 -> secondTarget copied and stopped before
            // its switch and its delete: two moved copies, each complete (a copy is one write).
            store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour);
            store.DeleteSensorHours(_product, _sensorA, p1, hour, hour + Hour);
            store.CopySensorHours(_product, _sensorA, p4, secondTarget, hour, hour + Hour);
            store.Dispose();
            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.PlacementsName), recursive: true);

            // The first copy seen (the lower class) is taken: either is every row of the hour, and the other one is the one the
            // recovery of the move deletes (the class it keeps is the placement's, one of the two its record names).
            var reopened = Open();
            Assert.Equal(new TablePlacement(expected, hour), reopened.GetPlacement(_product, _sensorA));
            Assert.Equal(rows, ReadByPlacement(reopened, _sensorA, [hour]).Select(r => r.Batch));
            Assert.Equal(new TablePlacement(1, hour), reopened.GetPlacement(_product, _sensorB));
        }

        [Fact]
        public void A_copy_is_refused_into_a_dropped_database_or_from_an_hour_without_its_mark_and_its_arguments_are_checked()
        {
            var store = Open();
            long hour = Week0 + Hour;
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));
            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorA, Hour, 1)), Part(4, (_sensorB, Hour, 1))]);
            store.DropWeek(_product, p4);

            Assert.Throws<TableBatchStoreException>(() => store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour));
            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 4)));

            Assert.Throws<ArgumentException>(() => store.CopySensorHours(_product, _sensorA, p1, 1, hour, hour + Hour));
            Assert.Throws<ArgumentException>(() => store.CopySensorHours(_product, _sensorA, p1, 5, hour, hour + Week));
            Assert.Throws<ArgumentException>(() => store.CopySensorHours(_product, _sensorA, p1, 5, hour + 1, hour + Hour));
            Assert.Throws<ArgumentException>(() => store.CopySensorHours(_product, _sensorA, new TableBatchWeek(Week0 + Hour, 1), 5, hour, hour + Hour));
            Assert.Throws<ArgumentException>(() => store.DeleteSensorHours(_product, _sensorA, p1, hour - Hour - Week, hour));

            // A source with the sensor's records and no mark of the hour (taken out by hand: the store never leaves one).
            var path = store.WeekPath(_product, Week0, 1);
            store.Dispose();
            using (var db = new DB(path, new Options()))
                db.Delete(TableBatchDatabase.HourKey(hour, TableBatchDatabase.TagMark), new WriteOptions { Sync = true });

            store = Open();
            Assert.Throws<TableBatchStoreException>(() => store.CopySensorHours(_product, _sensorA, p1, 5, hour, hour + Hour));
            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 5)));
            Assert.Single(store.ReadSensor(_product, _sensorA, 1, [hour]));
        }

        [Fact]
        public void A_copy_carries_an_index_record_whose_batch_is_missing_and_the_count_from_the_index_fetches_no_batch()
        {
            var store = Open();
            long hour = Week0 + Hour;
            var p1 = new TableBatchWeek(Week0, 1);
            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorA, Hour, 3))]);
            var path = store.WeekPath(_product, Week0, 1);
            store.Dispose();

            // The batch of ordinal 1 is gone, its index record is there.
            using (var db = new DB(path, new Options()))
                db.Delete(TableBatchDatabase.BatchKey(hour, 1), new WriteOptions { Sync = true });

            store = Open();
            Assert.Equal(new TableBatchSensorHours(3, 30, 3 * PayloadLength), store.GetSensorHourBytes(_product, _sensorA, p1, hour, hour + Hour));
            Assert.Equal(new TableBatchSensorHours(3, 30, 3 * PayloadLength), store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour));

            // The copy reads as the source does: the same two batches, the same failure, nothing healed or lost.
            var source = store.ReadSensor(_product, _sensorA, 1, [hour], out var sourceFailed);
            var copy = store.ReadSensor(_product, _sensorA, 4, [hour], out var copyFailed);
            Assert.Equal((1, 1), (sourceFailed, copyFailed));
            Assert.Equal(source.Select(r => r.Batch), copy.Select(r => r.Batch));
            Assert.Equal([0, 2], copy.Select(r => r.Ordinal));

            // Counted from the index of both regions, the hours asked only; nothing for a database or product without a folder.
            var p4 = new TableBatchWeek(Week0, 4);
            Assert.Equal(new TableBatchSensorHours(3, 30, 3 * PayloadLength), store.GetSensorHourBytes(_product, _sensorA, p4, Week0, Week0 + Week));
            Assert.Equal(default, store.GetSensorHourBytes(_product, _sensorA, p4, hour + Hour, Week0 + Week));
            Assert.Equal(default, store.GetSensorHourBytes(_product, _sensorB, p4, Week0, Week0 + Week));
            Assert.Equal(default, store.GetSensorHourBytes(_product, _sensorA, new TableBatchWeek(Week0, 9), Week0, Week0 + Week));
            Assert.Equal(default, store.GetSensorHourBytes(Guid.NewGuid(), _sensorA, p4, Week0, Week0 + Week));
        }

        // === DeleteSensorHours and the deleted-bytes counter ===

        [Fact]
        public void Deleting_a_sensors_hours_leaves_every_key_of_another_sensor_byte_for_byte_keeps_the_marks_and_counts_the_bytes_also_after_a_reopen()
        {
            var store = Open();
            var sensorS = _sensorA;
            var sensorR = _sensorB;
            long h1 = Week0 + Hour, h2 = Week0 + 2 * Hour, h3 = Week0 + 3 * Hour;
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));

            // Hour 1 in P4: S's batches are ordinals 0 and 1, R's 2 and 3. Hour 2: both moved into P4 from P1. Hour 3: S in P4
            // too, outside the hours deleted.
            store.WriteHour(_product, Commit(h1, 1), [Part(4, (sensorS, Hour, 2), (sensorR, Hour, 2))]);
            store.WriteHour(_product, Commit(h2, 2), [Part(1, (sensorS, 2 * Hour, 1), (sensorR, 2 * Hour, 1))]);
            store.CopySensorHours(_product, sensorS, p1, 4, h2, h3);
            store.CopySensorHours(_product, sensorR, p1, 4, h2, h3);
            store.WriteHour(_product, Commit(h3, 3), [Part(4, (sensorS, 3 * Hour, 1))]);

            var readR = store.ReadSensor(_product, sensorR, 4, [h1, h2]).Select(r => (r.HourTicks, r.Moved, r.Ordinal, r.Batch)).ToList();
            var path = store.WeekPath(_product, Week0, 4);
            store.Dispose();
            var before = Dump(path);

            store = Open();
            var deleted = store.DeleteSensorHours(_product, sensorS, p4, h1, h3);
            Assert.Equal(new TableBatchSensorHours(3, 30, 3 * PayloadLength), deleted);
            Assert.Equal(new TableBatchDeletedShare(3 * PayloadLength, 0), store.GetDeletedShare(_product, p4) with { FolderBytes = 0 });
            Assert.True(store.GetDeletedShare(_product, p4).FolderBytes > 0);

            Assert.Empty(store.ReadSensor(_product, sensorS, 4, [h1, h2]));
            Assert.Single(store.ReadSensor(_product, sensorS, 4, [h3]));
            Assert.Equal(readR, store.ReadSensor(_product, sensorR, 4, [h1, h2]).Select(r => (r.HourTicks, r.Moved, r.Ordinal, r.Batch)));

            // Repeated: nothing more to delete, nothing written, the counter as it was.
            Assert.Equal(default, store.DeleteSensorHours(_product, sensorS, p4, h1, h3));
            Assert.Equal(3L * PayloadLength, store.GetDeletedShare(_product, p4).DeletedBytes);
            store.Dispose();

            // On disk: exactly S's keys of hours 1 and 2 gone (its index records, the two hour-region batches its records
            // name, its moved keys), the counter added, every other key (R's, the marks, hour 3) byte for byte as it was.
            var after = Dump(path);
            var expectedGone = new HashSet<string>(before.Keys.Where(k => IsKeyOf(Convert.FromHexString(k), sensorS, h1, h3)))
            {
                Convert.ToHexString(TableBatchDatabase.BatchKey(h1, 0)),
                Convert.ToHexString(TableBatchDatabase.BatchKey(h1, 1)),
            };
            // Hour 1: two index records and the two batches they name; hour 2: one moved index record and its batch.
            Assert.Equal(6, expectedGone.Count);
            var expected = before.Where(e => !expectedGone.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value);
            expected[Convert.ToHexString(TableBatchDatabase.DeletedKey)] = BitConverter.GetBytes(3L * PayloadLength);
            Assert.Equal(expected.OrderBy(e => e.Key).Select(e => (e.Key, Convert.ToHexString(e.Value))), after.OrderBy(e => e.Key).Select(e => (e.Key, Convert.ToHexString(e.Value))));
            Assert.Contains(Convert.ToHexString(TableBatchDatabase.HourKey(h1, TableBatchDatabase.TagMark)), after.Keys);
            Assert.Contains(Convert.ToHexString(TableBatchDatabase.HourKey(h2, TableBatchDatabase.TagMark)), after.Keys);

            // After a reopen: the same counter, the same reads.
            var reopened = Open();
            Assert.Equal(3L * PayloadLength, reopened.GetDeletedShare(_product, p4).DeletedBytes);
            Assert.Empty(reopened.ReadSensor(_product, sensorS, 4, [h1, h2]));
            Assert.Equal(readR, reopened.ReadSensor(_product, sensorR, 4, [h1, h2]).Select(r => (r.HourTicks, r.Moved, r.Ordinal, r.Batch)));

            // A database without a folder, or a product without one: nothing, nothing created.
            Assert.Equal(default, reopened.DeleteSensorHours(_product, sensorS, new TableBatchWeek(Week0, 9), Week0, Week0 + Week));
            Assert.False(Directory.Exists(reopened.WeekPath(_product, Week0, 9)));
            Assert.Equal(default, reopened.DeleteSensorHours(Guid.NewGuid(), sensorS, p4, Week0, Week0 + Week));
            Assert.Equal(default, reopened.GetDeletedShare(_product, new TableBatchWeek(Week0, 9)));
        }

        // === Crash points of the new durable writes ===

        [Theory]
        [InlineData("sensor-copied")]
        [InlineData("sensor-deleted")]
        public void A_crash_right_after_a_copy_or_a_delete_keeps_it_after_a_restart_and_a_repeat_writes_nothing_more(string point)
        {
            var store = Open();
            long hour = Week0 + Hour;
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));
            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorA, Hour, 2), (_sensorB, Hour, 1))]);
            var rows = store.ReadSensor(_product, _sensorA, 1, [hour]).Select(r => r.Batch).ToList();
            if (point == "sensor-deleted")
                store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour);

            // The write is synced and then the process stops, nothing flushed: the databases replay their logs.
            store.Checkpoint = p => { if (p == point) throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() =>
            {
                if (point == "sensor-copied")
                    store.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour);
                else
                    store.DeleteSensorHours(_product, _sensorA, p1, hour, hour + Hour);
            });
            store.CloseWithoutFlush();

            var reopened = Open();
            Assert.Equal(rows, reopened.ReadSensor(_product, _sensorA, 4, [hour]).Select(r => r.Batch));
            Assert.Equal(reopened.ReadMark(_product, p4, hour), reopened.ReadMark(_product, p1, hour));
            Assert.Single(reopened.ReadSensor(_product, _sensorB, 1, [hour]));

            if (point == "sensor-copied")
            {
                // The source as it was; a repeat of the copy leaves one copy.
                Assert.Equal(rows, reopened.ReadSensor(_product, _sensorA, 1, [hour]).Select(r => r.Batch));
                reopened.CopySensorHours(_product, _sensorA, p1, 4, hour, hour + Hour);
                Assert.Equal(rows, reopened.ReadSensor(_product, _sensorA, 4, [hour]).Select(r => r.Batch));
                Assert.Equal(0, reopened.GetDeletedShare(_product, p1).DeletedBytes);
            }
            else
            {
                // The source gone with its counter; a repeat deletes and counts nothing more.
                Assert.Empty(reopened.ReadSensor(_product, _sensorA, 1, [hour]));
                Assert.Equal(2L * PayloadLength, reopened.GetDeletedShare(_product, p1).DeletedBytes);
                Assert.Equal(default, reopened.DeleteSensorHours(_product, _sensorA, p1, hour, hour + Hour));
                Assert.Equal(2L * PayloadLength, reopened.GetDeletedShare(_product, p1).DeletedBytes);
            }
        }

        // === CompactWeek ===

        // P4 of week 0 with a day of A's and B's batches of 32 KB, flushed into tables, and A's deleted.
        private (TableBatchDatabase Store, TableBatchWeek Week, long[] Hours, List<byte[]> RowsOfB) CompactionCase()
        {
            var store = Open();
            var p4 = new TableBatchWeek(Week0, 4);
            var hours = Enumerable.Range(0, 24).Select(h => Week0 + h * Hour).ToArray();
            var random = new Random(1850);

            TableBatchToWrite Big(Guid sensor, long first)
            {
                var bytes = new byte[32 * 1024];
                random.NextBytes(bytes);
                return new TableBatchToWrite(sensor, first, first + 9, 10, bytes);
            }

            foreach (var hour in hours)
                store.WriteHour(_product, Commit(hour, hour / Hour), [new TableBatchHourPart(4, [Big(_sensorA, hour), Big(_sensorB, hour)])]);

            // Closed with a flush: every batch is in the database's tables, no log left.
            store.Dispose();
            store = Open();

            var rowsOfB = store.ReadSensor(_product, _sensorB, 4, hours).Select(r => r.Batch).ToList();
            Assert.Equal(new TableBatchSensorHours(24, 240, 24 * 32 * 1024), store.DeleteSensorHours(_product, _sensorA, p4, Week0, Week0 + 24 * Hour));
            return (store, p4, hours, rowsOfB);
        }

        [Fact]
        public void A_compaction_gives_the_deleted_bytes_back_and_lowers_the_counter_to_0()
        {
            var (store, p4, hours, rowsOfB) = CompactionCase();
            var before = store.GetDeletedShare(_product, p4);
            Assert.Equal(24L * 32 * 1024, before.DeletedBytes);
            Assert.True(before.Share > 0.3, $"share {before.Share}");

            store.CompactWeek(_product, p4);

            var after = store.GetDeletedShare(_product, p4);
            Assert.Equal(0, after.DeletedBytes);
            Assert.True(after.FolderBytes < before.FolderBytes - 20 * 32 * 1024, $"{before.FolderBytes} -> {after.FolderBytes}");
            Assert.Equal(rowsOfB, store.ReadSensor(_product, _sensorB, 4, hours).Select(r => r.Batch));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 4, hours));

            // Nothing for a database or a product without a folder.
            store.CompactWeek(_product, new TableBatchWeek(Week0, 9));
            store.CompactWeek(Guid.NewGuid(), p4);
            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 9)));

            store.Dispose();
            Assert.Equal(0, Open().GetDeletedShare(_product, p4).DeletedBytes);
        }

        [Fact]
        public void A_crash_between_the_compaction_and_the_counters_write_keeps_the_counter_and_a_repeat_lowers_it()
        {
            var (store, p4, _, _) = CompactionCase();

            store.Checkpoint = p => { if (p == "compacted") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.CompactWeek(_product, p4));
            store.CloseWithoutFlush();

            // The compaction is done, the counter still says what it said: the safe side, a compaction made once more.
            var reopened = Open();
            Assert.Equal(24L * 32 * 1024, reopened.GetDeletedShare(_product, p4).DeletedBytes);
            reopened.CompactWeek(_product, p4);
            Assert.Equal(0, reopened.GetDeletedShare(_product, p4).DeletedBytes);
        }

        [Fact]
        public void A_compaction_holds_no_lock_a_read_or_a_write_of_the_product_needs_and_a_read_across_it_is_complete()
        {
            var (store, p4, hours, rowsOfB) = CompactionCase();
            var other = new TableBatchWeek(Week0, 1);

            // The compaction holds its lease and is about to compact: on another thread, a read of B, the generation (the
            // product's lock) and a write of the product (another class of the week) finish meanwhile.
            List<byte[]> readMeanwhile = null;
            bool finished = false;
            store.Checkpoint = p =>
            {
                if (p != "compacting")
                    return;

                var meanwhile = StartLong(() =>
                {
                    readMeanwhile = store.ReadSensor(_product, _sensorB, 4, hours).Select(r => r.Batch).ToList();
                    store.GetGeneration(_product, _sensorB);
                    store.WriteHour(_product, Commit(Week0 + 30 * Hour, 99), [Part(1, (_sensorB, 30 * Hour, 1))]);
                });
                finished = meanwhile.Wait(Bound);
            };

            store.CompactWeek(_product, p4);
            Assert.True(finished, "a read or a write of the product waited for the compaction");
            Assert.Equal(rowsOfB, readMeanwhile);
            Assert.True(store.IsHourMerged(_product, Week0 + 30 * Hour));
            Assert.Contains(other, store.GetWeeks(_product));

            // The other way round: a read holds the database and its iterator when a whole compaction runs; it ends with
            // every row it planned (the iterator keeps the tables it reads).
            store.Checkpoint = null;
            store.DeleteSensorHours(_product, _sensorB, p4, Week0, Week0 + 12 * Hour);
            int once = 0;
            bool compacted = false;
            store.Checkpoint = p =>
            {
                if (p == "read-holding" && Interlocked.Exchange(ref once, 1) == 0)
                    compacted = StartLong(() => store.CompactWeek(_product, p4)).Wait(Bound);
            };

            var read = store.ReadSensor(_product, _sensorB, 4, hours).Select(r => r.Batch).ToList();
            Assert.True(compacted, "the compaction waited for a read");
            Assert.Equal(rowsOfB.Skip(12), read);
            Assert.Equal(0, store.GetDeletedShare(_product, p4).DeletedBytes);
        }

        // === The files of a product: the compaction list and the move in flight ===

        [Fact]
        public void The_move_in_flight_and_the_compaction_list_are_durable_files_that_the_open_the_product_list_and_a_rebuild_of_the_placements_leave_alone()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]);
            var record = new TableMoveInFlight(_sensorA, Week0, 1, 5, Week0, Week0 + 5 * Hour);
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));

            Assert.Null(store.GetMoveInFlight(_product));
            Assert.Empty(store.GetCompactList(_product));
            store.SetMoveInFlight(_product, record);
            store.AddToCompactList(_product, p4);
            store.AddToCompactList(_product, p1);
            store.AddToCompactList(_product, p4);
            Assert.Equal(record, store.GetMoveInFlight(_product));
            Assert.Equal([p1, p4], store.GetCompactList(_product));

            // A temporary file of a write that stopped, and a file the store does not know, in the product's folder and in the
            // store's: nothing reads them.
            var folder = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"));
            File.WriteAllText(Path.Combine(folder, TableBatchDatabase.MoveName + ".tmp"), "garbage");
            File.WriteAllText(Path.Combine(folder, "notes"), "garbage");
            File.WriteAllText(Path.Combine(_root, TableBatchDatabase.FolderName, Guid.NewGuid().ToString("N")), "a file named as a product");
            store.CloseWithoutFlush();

            // Placements lost: the rebuild deletes every key of Placements, and the record is not one of them.
            Directory.Delete(Path.Combine(folder, TableBatchDatabase.PlacementsName), recursive: true);

            var reopened = Open();
            Assert.Equal([_product], reopened.GetProducts());
            Assert.Equal([p1, p4], reopened.GetWeeks(_product));
            Assert.Equal(1, reopened.Statistics.PlacementsRebuilt);
            Assert.Equal(2, reopened.GetPlacements(_product).Count);
            Assert.Equal(record, reopened.GetMoveInFlight(_product));
            Assert.Equal([p1, p4], reopened.GetCompactList(_product));

            // Cleared: the files go, and stay gone after a restart.
            reopened.SetMoveInFlight(_product, null);
            reopened.RemoveFromCompactList(_product, p1);
            reopened.RemoveFromCompactList(_product, p4);
            Assert.False(File.Exists(Path.Combine(folder, TableBatchDatabase.MoveName)));
            Assert.False(File.Exists(Path.Combine(folder, TableBatchDatabase.CompactName)));
            reopened.Dispose();

            var third = Open();
            Assert.Null(third.GetMoveInFlight(_product));
            Assert.Empty(third.GetCompactList(_product));
        }

        [Fact]
        public void A_write_of_the_move_in_flight_or_of_the_compaction_list_that_fails_is_a_store_fault_and_changes_nothing()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1))]);
            var record = new TableMoveInFlight(_sensorA, Week0, 1, 5, Week0, Week0 + Hour);
            var p1 = new TableBatchWeek(Week0, 1);
            store.SetMoveInFlight(_product, record);
            store.AddToCompactList(_product, p1);

            // The temporary files cannot be written: a folder stands where each would go.
            var folder = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, TableBatchDatabase.MoveName + ".tmp"));
            Directory.CreateDirectory(Path.Combine(folder, TableBatchDatabase.CompactName + ".tmp"));

            Assert.Throws<TableBatchStoreException>(() => store.SetMoveInFlight(_product, record with { EndHour = Week0 + 2 * Hour }));
            Assert.Throws<TableBatchStoreException>(() => store.AddToCompactList(_product, new TableBatchWeek(Week0, 4)));
            Assert.Equal(2, store.Statistics.WriteFailures);
            Assert.Equal(record, store.GetMoveInFlight(_product));
            Assert.Equal([p1], store.GetCompactList(_product));
            store.Dispose();

            var reopened = Open();
            Assert.Equal(record, reopened.GetMoveInFlight(_product));
            Assert.Equal([p1], reopened.GetCompactList(_product));

            // A record that breaks its rules is an argument error; one for a product without a folder a fault.
            Assert.Throws<ArgumentException>(() => reopened.SetMoveInFlight(_product, record with { ToClass = 1 }));
            Assert.Throws<ArgumentException>(() => reopened.SetMoveInFlight(_product, record with { EndHour = Week0 + Week + Hour }));
            Assert.Throws<TableBatchStoreException>(() => reopened.SetMoveInFlight(Guid.NewGuid(), record));
            reopened.SetMoveInFlight(Guid.NewGuid(), null);
        }

        [Fact]
        public void A_move_in_flight_that_does_not_read_is_taken_as_none_and_the_open_goes_on()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1))]);
            store.Dispose();

            var folder = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"));
            File.WriteAllText(Path.Combine(folder, TableBatchDatabase.MoveName), "2 something else\n");
            File.WriteAllText(Path.Combine(folder, TableBatchDatabase.CompactName), "not a week\n20261102_P4\n");

            var reopened = Open();
            Assert.Null(reopened.GetMoveInFlight(_product));
            Assert.Equal([new TableBatchWeek(Week0, 4)], reopened.GetCompactList(_product));
            Assert.Single(reopened.GetHours(_product));
        }

        // A key of the sensor in the hours [first, end) of a week database: its index record in the hour region, or any of
        // its keys in the region of moved batches.
        private static bool IsKeyOf(byte[] key, Guid sensor, long first, long end)
        {
            bool moved = key[0] == TableBatchDatabase.MovedRegion;
            // The hour at 0 (or 1 in the moved region), the sensor at 9 in both.
            if (key.Length < 25 || (!moved && key[8] != TableBatchDatabase.TagIndex))
                return false;

            long hour = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(moved ? 1 : 0));
            return hour >= first && hour < end && new Guid(key.AsSpan(9, 16), bigEndian: true) == sensor;
        }

        // Every key and value of a database closed by the store.
        private static Dictionary<string, byte[]> Dump(string path)
        {
            var keys = new Dictionary<string, byte[]>();
            using var db = new DB(path, new Options());
            using var iterator = db.CreateIterator();

            for (iterator.SeekToFirst(); iterator.IsValid; iterator.Next())
                keys[Convert.ToHexString(iterator.Key())] = iterator.Value();

            return keys;
        }

        // A sensor's batches of the given hours, each hour from the class its placement gives it.
        private List<TableBatchRead> ReadByPlacement(TableBatchDatabase store, Guid sensor, long[] hours) =>
            hours.SelectMany(h => store.ReadSensor(_product, sensor, store.GetPlacement(_product, sensor).ClassOf(h), [h])).ToList();
    }
}
