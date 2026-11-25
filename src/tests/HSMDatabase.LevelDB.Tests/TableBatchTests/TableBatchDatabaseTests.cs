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

            // The merge runs the hour again: the parts already written are written again, the hour is committed once.
            reopened.WriteHour(_product, Commit(Week0), TwoParts());
            Assert.True(reopened.IsHourMerged(_product, Week0));
            Assert.Equal(2, reopened.ReadSensor(_product, _sensorA, 1, [Week0]).Count);
            Assert.Equal(1, reopened.ReadSensor(_product, _sensorB, 4, [Week0]).Count);
            Assert.Equal([1, 4], reopened.GetHours(_product).Single().Classes.Select(c => c.PeriodWeeks));
        }

        // === A repeat that differs from the crashed run ===

        // The first run writes its parts and crashes before the commit.
        private TableBatchDatabase CrashBeforeCommit(TableBatchHourCommit commit, TableBatchHourPart[] parts)
        {
            var store = Open();
            store.Checkpoint = p =>
            {
                if (p == "before-commit")
                    throw new CheckpointException(p);
            };

            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, commit, parts));
            store.Dispose();
            return Open();
        }

        // Hours removed: the next open rebuilds it from the marks.
        private TableBatchDatabase RebuildHours(TableBatchDatabase store)
        {
            store.Dispose();
            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName), recursive: true);
            return Open();
        }

        [Fact]
        public void A_repeat_with_another_batch_split_rewrites_the_part()
        {
            var store = CrashBeforeCommit(Commit(Week0), [Part(4, (_sensorA, 0, 2))]);

            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 3))]);

            Assert.Equal([0, 1, 2], store.ReadSensor(_product, _sensorA, 4, [Week0]).Select(r => r.Ordinal));
            Assert.Equal(3, store.GetHours(_product).Single().Classes.Single().Batches);
            Assert.Equal(1, store.Statistics.PartsRewritten);
            Assert.Equal(3, RebuildHours(store).ReadSensor(_product, _sensorA, 4, [Week0]).Count);
        }

        [Fact]
        public void A_repeat_with_another_list_of_unreadable_blocks_rewrites_the_part_and_a_rebuild_restores_the_committed_record()
        {
            // The first run could not read block 42 and wrote one batch; the repeat read it and writes two.
            var store = CrashBeforeCommit(Commit(Week0) with { UnreadableBlocks = [42] }, [Part(4, (_sensorA, 0, 1))]);

            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 2))]);

            Assert.Equal(2, store.ReadSensor(_product, _sensorA, 4, [Week0]).Count);
            Assert.Empty(store.GetHour(_product, Week0).UnreadableBlocks);
            Assert.Equal(1, store.Statistics.PartsRewritten);

            var rebuilt = RebuildHours(store);
            Assert.Empty(rebuilt.GetHour(_product, Week0).UnreadableBlocks);
            Assert.Equal(2, rebuilt.ReadSensor(_product, _sensorA, 4, [Week0]).Count);
        }

        [Fact]
        public void A_sensor_moved_to_another_class_between_the_runs_leaves_no_part_in_the_old_class_and_its_rows_are_read_once()
        {
            // The first run: sensor A in class 1, sensor B in class 4. The repeat: A in class 4 too.
            var store = CrashBeforeCommit(Commit(Week0), [Part(1, (_sensorA, 0, 2)), Part(4, (_sensorB, 100, 1))]);

            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 2), (_sensorB, 100, 1))]);

            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.Equal(2, store.ReadSensor(_product, _sensorA, 4, [Week0]).Count);
            Assert.Single(store.ReadSensor(_product, _sensorB, 4, [Week0]));
            Assert.Equal([4], store.GetHour(_product, Week0).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(2, store.Statistics.PartsRewritten); // P1 deleted, P4 written again

            // The old class's database holds nothing of the hour, its mark included: a rebuild restores the committed record.
            var rebuilt = RebuildHours(store);
            Assert.Equal([4], rebuilt.GetHour(_product, Week0).Classes.Select(c => c.PeriodWeeks));
            Assert.Empty(rebuilt.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.Equal(2, rebuilt.ReadSensor(_product, _sensorA, 4, [Week0]).Count);
        }

        [Fact]
        public void A_rebuild_does_not_commit_an_hour_whose_marks_disagree()
        {
            // The first run writes both parts with one block unreadable and crashes before the commit.
            var store = CrashBeforeCommit(Commit(Week0) with { UnreadableBlocks = [42] }, TwoParts());

            // The repeat read the block: it rewrites the P1 part and crashes before the P4 part.
            store.Checkpoint = p => { if (p == "part-written") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), TwoParts()));
            store.Checkpoint = null;
            Assert.False(store.IsHourMerged(_product, Week0));

            // Every class the records name has a mark, but the marks are of two runs: the hour is not merged.
            var rebuilt = RebuildHours(store);
            Assert.False(rebuilt.IsHourMerged(_product, Week0));
            Assert.Equal(0, rebuilt.Statistics.HoursRebuilt);

            // The merge repeats it and commits the record of the repeat.
            rebuilt.WriteHour(_product, Commit(Week0), TwoParts());
            Assert.Empty(rebuilt.GetHour(_product, Week0).UnreadableBlocks);
            Assert.Equal(2, rebuilt.Statistics.PartsRewritten);
            Assert.Equal([1, 4], RebuildHours(rebuilt).GetHours(_product).Single().Classes.Select(c => c.PeriodWeeks));
        }

        [Fact]
        public void A_repeat_equal_to_the_crashed_run_writes_its_parts_again_and_they_are_read_once()
        {
            var store = CrashBeforeCommit(Commit(Week0), TwoParts());

            store.WriteHour(_product, Commit(Week0), TwoParts());

            Assert.Equal(3, store.Statistics.BatchesWritten);
            Assert.Equal(2, store.Statistics.PartsRewritten);
            Assert.True(store.IsHourMerged(_product, Week0));
            Assert.Equal(2, store.ReadSensor(_product, _sensorA, 1, [Week0]).Count);
            Assert.Single(store.ReadSensor(_product, _sensorB, 4, [Week0]));
        }

        [Fact]
        public void Two_sensors_that_swapped_classes_between_the_runs_are_each_stored_once_in_their_new_class()
        {
            // One row each at the same time: the parts of the two runs have the same counts and ranges per class.
            var store = Open();
            store.Checkpoint = p => { if (p == "part-written") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]));
            store.Checkpoint = null;

            // A is in P4 now, B in P1.
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorB, 0, 1)), Part(4, (_sensorA, 0, 1))]);

            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.Single(store.ReadSensor(_product, _sensorA, 4, [Week0]));
            Assert.Single(store.ReadSensor(_product, _sensorB, 1, [Week0]));
            Assert.Empty(store.ReadSensor(_product, _sensorB, 4, [Week0]));
        }

        [Fact]
        public void A_rebuild_does_not_commit_parts_of_two_runs_with_the_same_metadata_and_another_commit_time()
        {
            var time = new DateTime(2026, 11, 2, 1, 7, 0, DateTimeKind.Utc);
            var store = Open(clock: () => time);
            store.Checkpoint = p => { if (p == "before-commit") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), TwoParts()));

            // The repeat, a minute later, writes the P1 part again and stops before the P4 part.
            time = time.AddMinutes(1);
            store.Checkpoint = p => { if (p == "part-written") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), TwoParts()));
            store.Checkpoint = null;

            var rebuilt = RebuildHours(store);
            Assert.False(rebuilt.IsHourMerged(_product, Week0));
            Assert.Equal(0, rebuilt.Statistics.HoursRebuilt);
        }

        [Theory]
        [InlineData("part-written")]  // the part of the new record written, the old class's part not removed yet
        [InlineData("stale-removed")] // the old class's part removed, the commit not written
        public void A_repeat_that_moved_a_sensor_and_crashed_leaves_no_rebuilt_hour_without_its_rows(string point)
        {
            // The first run: A in P1, B in P4; it crashes before the commit.
            var store = CrashBeforeCommit(Commit(Week0), [Part(1, (_sensorA, 0, 2)), Part(4, (_sensorB, 100, 1))]);

            // B moved to P1: the repeat's record names P1 only, and the repeat crashes.
            store.Checkpoint = p => { if (p == point) throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 2), (_sensorB, 100, 1))]));
            store.Checkpoint = null;

            // Retention drops the P4 database and the merged hours are lost: the rebuild commits the hour only with B's rows.
            store.DropWeek(_product, new TableBatchWeek(Week0, 4));
            var rebuilt = RebuildHours(store);

            Assert.True(rebuilt.IsHourMerged(_product, Week0));
            Assert.Equal([1], rebuilt.GetHour(_product, Week0).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(2, rebuilt.ReadSensor(_product, _sensorA, 1, [Week0]).Count);
            Assert.Single(rebuilt.ReadSensor(_product, _sensorB, 1, [Week0]));
        }

        [Fact]
        public void The_database_a_repeat_removed_a_part_from_is_flushed_when_the_next_week_is_written()
        {
            var store = CrashBeforeCommit(Commit(Week0), [Part(1, (_sensorA, 0, 2)), Part(4, (_sensorB, 100, 1))]);
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 2), (_sensorB, 100, 1))]);

            var week0 = store.WeekPath(_product, Week0, 1);
            Assert.Contains(Directory.GetFiles(week0, "*.log"), f => new FileInfo(f).Length > 0);

            store.WriteHour(_product, Commit(Week0 + Week), [Part(4, (_sensorA, 0, 1))]);

            Assert.All(Directory.GetFiles(week0, "*.log"), f => Assert.Equal(0, new FileInfo(f).Length));
        }

        [Fact]
        public void A_repeat_that_crashed_before_removing_the_old_class_part_is_not_rebuilt_while_that_part_is_there()
        {
            var store = CrashBeforeCommit(Commit(Week0), [Part(1, (_sensorA, 0, 2)), Part(4, (_sensorB, 100, 1))]);

            store.Checkpoint = p => { if (p == "part-written") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 2), (_sensorB, 100, 1))]));
            store.Checkpoint = null;

            // The P1 mark is the repeat's, the P4 mark the first run's: not committed, the hour stays on its blocks.
            Assert.False(RebuildHours(store).IsHourMerged(_product, Week0));
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

            // The highest per producer over every commit: a producer the newest commit does not name keeps its watermark.
            Assert.Equal(new Dictionary<Guid, long> { [_producer] = 9, [other] = 7 }, reopened.GetWatermarks(_product));
            Assert.Equal([_product], reopened.GetProducts());
        }

        [Fact]
        public void A_producer_of_an_hour_committed_after_a_later_one_keeps_its_watermark_also_after_a_reopen_and_a_rebuild()
        {
            var store = Open();
            var late = Guid.NewGuid();

            // The later hour commits first; the earlier one, merged afterwards, is the only one that names producer X.
            store.WriteHour(_product, Commit(Week0 + Hour, sequence: 9), [Part(4, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(Week0, sequence: 5) with { Watermarks = new Dictionary<Guid, long> { [_producer] = 5, [late] = 3 } },
                [Part(4, (_sensorA, 0, 1))]);

            var expected = new Dictionary<Guid, long> { [_producer] = 9, [late] = 3 };
            Assert.Equal(expected, store.GetWatermarks(_product));
            store.Dispose();

            var reopened = Open();
            Assert.Equal(expected, reopened.GetWatermarks(_product));
            reopened.Dispose();

            DamageHours();
            var rebuilt = Open();
            Assert.Equal(expected, rebuilt.GetWatermarks(_product));
            Assert.Equal(2, rebuilt.Statistics.HoursRebuilt);
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
        public void A_dropped_week_is_gone_from_disk_and_its_hours_stay_committed_until_they_are_forgotten()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Hour), [Part(1, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Week), [Part(1, (_sensorA, 0, 1))]);
            Assert.Single(store.ReadSensor(_product, _sensorA, 1, [Week0]));

            store.DropWeek(_product, new TableBatchWeek(Week0, 1));

            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 1)));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0, Week0 + Hour]));

            // A database of week 0 is left (P4): both its hours are listed with the classes of their records, the dropped
            // one included (rows moved out of it would be within its range).
            var hours = store.GetHours(_product);
            Assert.Equal([Week0, Week0 + Hour, Week0 + Week], hours.Select(h => h.HourTicks));
            Assert.Equal([1, 4], hours[0].Classes.Select(c => c.PeriodWeeks));
            Assert.True(store.IsHourMerged(_product, Week0 + Hour));
            Assert.Empty(store.GetHoursWithoutDatabases(_product));

            // The last database of week 0 goes: its hours are not listed, and stay committed (their blocks may still be
            // held) until they are forgotten; forgetting skips an hour whose week has a database.
            store.DropWeek(_product, new TableBatchWeek(Week0, 4));
            Assert.Equal([Week0 + Week], store.GetHours(_product).Select(h => h.HourTicks));
            Assert.True(store.IsHourMerged(_product, Week0) && store.IsHourMerged(_product, Week0 + Hour));
            Assert.Equal([Week0, Week0 + Hour], store.GetHoursWithoutDatabases(_product));
            store.Dispose();

            var reopened = Open();
            Assert.True(reopened.IsHourMerged(_product, Week0 + Hour));
            Assert.Equal([Week0, Week0 + Hour], reopened.GetHoursWithoutDatabases(_product));

            reopened.ForgetHours(_product, [Week0, Week0 + Week]);
            Assert.False(reopened.IsHourMerged(_product, Week0));
            Assert.True(reopened.IsHourMerged(_product, Week0 + Hour));
            Assert.True(reopened.IsHourMerged(_product, Week0 + Week));
            reopened.Dispose();

            var again = Open();
            Assert.Equal([Week0 + Hour], again.GetHoursWithoutDatabases(_product));
            Assert.Equal([Week0 + Week], again.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal([new TableBatchWeek(Week0 + Week, 1)], again.GetWeeks(_product));
        }

        [Fact]
        public void A_rebuild_after_a_class_was_dropped_restores_the_hour_with_the_classes_left()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), TwoParts());
            store.DropWeek(_product, new TableBatchWeek(Week0, 1));
            store.Dispose();

            // The mark in the P4 database still names P1, whose database retention dropped.
            DamageHours();

            var reopened = Open();
            var hour = Assert.Single(reopened.GetHours(_product));
            Assert.Equal([1, 4], hour.Classes.Select(c => c.PeriodWeeks));
            Assert.True(reopened.IsHourMerged(_product, Week0));
            Assert.Single(reopened.ReadSensor(_product, _sensorB, 4, [Week0]));
            Assert.Equal(1, reopened.Statistics.HoursRebuilt);
        }

        [Fact]
        public void A_rebuild_still_refuses_an_hour_whose_part_was_never_written_when_other_classes_were_dropped()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), TwoParts());

            // The hour of the next week stops after its P1 part: its P4 database was never created.
            store.Checkpoint = p => { if (p == "part-written") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0 + Week, sequence: 2), TwoParts()));
            store.Checkpoint = null;

            store.DropWeek(_product, new TableBatchWeek(Week0, 1));
            store.Dispose();
            DamageHours();

            var reopened = Open();
            Assert.Equal([Week0], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.False(reopened.IsHourMerged(_product, Week0 + Week));
            Assert.Equal(1L, reopened.GetWatermarks(_product)[_producer]);
        }

        [Fact]
        public void A_drop_that_waits_for_a_read_does_not_close_again_the_database_the_read_evicted()
        {
            var store = Open();
            store.MaxOpenDatabases = 1;
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Week), [Part(4, (_sensorA, 0, 1))]);
            Assert.Equal(1, store.OpenDatabases); // the newest week, pinned

            using var holding = new ManualResetEventSlim();
            using var dropWaiting = new ManualResetEventSlim();
            var gate = new ManualResetEventSlim(); // not disposed: a thread of a failed test may still wait on it
            store.Checkpoint = p =>
            {
                if (p == "read-holding")
                {
                    holding.Set();
                    gate.Wait();
                }
                else if (p == "drop-waiting")
                    dropWaiting.Set();
            };

            // The read opens week 0 above the bound and holds it; its release closes it (the bound), while the drop waits.
            var read = StartLong(() => store.ReadSensor(_product, _sensorA, 4, [Week0]));
            Assert.True(holding.Wait(Bound), "the read holds week 0");
            var drop = StartLong(() => store.DropWeek(_product, new TableBatchWeek(Week0, 4)));
            Assert.True(dropWaiting.Wait(Bound), "the drop waits for the read");
            gate.Set();

            Assert.True(read.Wait(Bound));
            Assert.Single(read.Result);
            Assert.True(drop.Wait(Bound));
            store.Checkpoint = null;

            Assert.False(Directory.Exists(store.WeekPath(_product, Week0, 4)));
            Assert.Equal([new TableBatchWeek(Week0 + Week, 4)], store.GetWeeks(_product));
            Assert.Equal(1, store.OpenDatabases);
            Assert.Equal(store.Statistics.DatabasesOpened - 1, store.Statistics.DatabasesClosed);
        }

        [Fact]
        public void A_read_during_a_drop_finds_nothing_and_does_not_open_the_week_again()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), TwoParts());
            var path = store.WeekPath(_product, Week0, 1);

            IReadOnlyList<TableBatchRead> during = null;
            int openDuring = -1;
            store.Checkpoint = p =>
            {
                if (p == "drop-deleting")
                {
                    during = store.ReadSensor(_product, _sensorA, 1, [Week0]);
                    openDuring = store.OpenDatabases;
                }
            };

            store.DropWeek(_product, new TableBatchWeek(Week0, 1));
            store.Checkpoint = null;

            Assert.Empty(during);
            Assert.Equal(1, openDuring); // the P4 database only
            Assert.False(Directory.Exists(path));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.False(Directory.Exists(path));
        }

        [Fact]
        public void A_drop_whose_delete_fails_is_a_store_fault_and_the_week_stays_dropped_until_its_folder_goes()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), TwoParts());
            var path = store.WeekPath(_product, Week0, 1);

            // Half deleted, then the delete fails.
            store.DeleteFolder = p =>
            {
                File.Delete(Path.Combine(p, "CURRENT"));
                throw new IOException("delete failure injected by the test");
            };

            Assert.Throws<TableBatchStoreException>(() => store.DropWeek(_product, new TableBatchWeek(Week0, 1)));

            // Dropped in the lists, the folder left on disk; a read finds nothing there, a write into it is a fault.
            Assert.True(Directory.Exists(path));
            Assert.Equal([new TableBatchWeek(Week0, 4)], store.GetWeeks(_product));
            Assert.Equal([1, 4], store.GetHours(_product).Single().Classes.Select(c => c.PeriodWeeks));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.Equal(1, store.OpenDatabases);
            Assert.Throws<TableBatchStoreException>(() => store.WriteHour(_product, Commit(Week0 + Hour), [Part(1, (_sensorA, 0, 1))]));
            Assert.False(store.IsHourMerged(_product, Week0 + Hour));
            store.Dispose();

            // The next open of the product deletes the folder.
            var reopened = Open();
            Assert.Equal([new TableBatchWeek(Week0, 4)], reopened.GetWeeks(_product));
            Assert.False(Directory.Exists(path));
            Assert.Equal([1, 4], reopened.GetHours(_product).Single().Classes.Select(c => c.PeriodWeeks));
        }

        [Fact]
        public void Forgetting_hours_that_fails_is_a_counted_store_fault_and_the_hours_stay_committed()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1))]);
            store.DropWeek(_product, new TableBatchWeek(Week0, 1));

            store.Checkpoint = p => { if (p == "forget-hours") throw new IOException("write failure injected by the test"); };
            Assert.Throws<TableBatchStoreException>(() => store.ForgetHours(_product, [Week0]));
            store.Checkpoint = null;

            Assert.Equal(1, store.Statistics.WriteFailures);
            Assert.True(store.IsHourMerged(_product, Week0));

            store.ForgetHours(_product, [Week0]);
            Assert.False(store.IsHourMerged(_product, Week0));
            store.Dispose();
            Assert.False(Open().IsHourMerged(_product, Week0));
        }

        [Fact]
        public void A_crash_during_the_scan_of_a_rebuild_is_not_trusted_and_the_next_open_rebuilds_again()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), TwoParts());
            store.WriteHour(_product, Commit(Week0 + Hour, sequence: 2), TwoParts());
            store.Dispose();
            DeleteHours();

            // The rebuild stops in its scan: Hours exists, without a single hour and without its complete key.
            var crashed = Open();
            crashed.Checkpoint = p => { if (p == "rebuild-scanning") throw new CheckpointException(p); };
            Assert.Throws<TableBatchStoreException>(() => crashed.GetHours(_product));
            crashed.Dispose();
            Assert.True(Directory.Exists(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName)));

            // The next open finds an Hours without the key and rebuilds both hours.
            var reopened = Open();
            Assert.Equal([Week0, Week0 + Hour], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal(2L, reopened.GetWatermarks(_product)[_producer]);
            Assert.Equal(2, reopened.Statistics.HoursRebuilt);
            reopened.Dispose();

            // And that rebuilt Hours is trusted from then on: nothing is rebuilt again.
            var third = Open();
            Assert.Equal(2, third.GetHours(_product).Count);
            Assert.Equal(0, third.Statistics.HoursRebuilt);
        }

        [Fact]
        public void A_crash_right_after_the_hours_database_is_created_leaves_nothing_trusted_and_the_next_open_rebuilds()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), TwoParts());
            store.WriteHour(_product, Commit(Week0 + Hour, sequence: 2), TwoParts());
            store.Dispose();
            DeleteHours();

            // The open creates the folder of Hours and stops before anything is written into it: a valid, empty database.
            var crashed = Open();
            crashed.Checkpoint = p => { if (p == "hours-opened") throw new CheckpointException(p); };
            Assert.Throws<TableBatchStoreException>(() => crashed.GetHours(_product));
            crashed.CloseWithoutFlush();
            Assert.True(Directory.Exists(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName)));

            // It has no complete key, so the next open does not take its emptiness for the truth: both hours are rebuilt.
            var reopened = Open();
            Assert.Equal([Week0, Week0 + Hour], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal(2L, reopened.GetWatermarks(_product)[_producer]);
            Assert.Equal(2, reopened.Statistics.HoursRebuilt);
            reopened.Dispose();

            // Rebuilt once: with its key it is the truth.
            var third = Open();
            Assert.Equal(2, third.GetHours(_product).Count);
            Assert.Equal(0, third.Statistics.HoursRebuilt);
        }

        [Fact]
        public void A_crash_right_after_the_placements_database_is_created_leaves_nothing_trusted_and_the_next_open_rebuilds()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, 1), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]);
            store.Dispose();

            var placementsPath = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.PlacementsName);
            Directory.Delete(placementsPath, recursive: true);

            var crashed = Open();
            crashed.Checkpoint = p => { if (p == "placements-opened") throw new CheckpointException(p); };
            Assert.Throws<TableBatchStoreException>(() => crashed.GetPlacements(_product));
            crashed.CloseWithoutFlush();
            Assert.True(Directory.Exists(placementsPath));

            var reopened = Open();
            Assert.Equal(1, reopened.GetPlacement(_product, _sensorA).Target);
            Assert.Equal(4, reopened.GetPlacement(_product, _sensorB).Target);
            Assert.Equal(1, reopened.Statistics.PlacementsRebuilt);
            reopened.Dispose();

            var third = Open();
            Assert.Equal(2, third.GetPlacements(_product).Count);
            Assert.Equal(0, third.Statistics.PlacementsRebuilt);
        }

        [Fact]
        public void A_dispose_during_a_rebuild_of_the_hours_leaves_nothing_trusted_and_the_hours_are_back_after_a_reopen()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), TwoParts());
            store.WriteHour(_product, Commit(Week0 + Hour, sequence: 2), TwoParts());
            store.Dispose();
            DeleteHours();

            // An orderly shutdown while the product opens: the open has created Hours and is about to scan the week
            // databases when the dispose marks the store closed (it then waits for the open, which it says at
            // "dispose-open-busy"). The scan's first week database is refused, and the open fails with nothing written.
            var stopping = Open();
            using var disposeArrived = new ManualResetEventSlim();
            Task dispose = null;
            var arrived = false;
            stopping.Checkpoint = p =>
            {
                if (p == "rebuild-scanning")
                {
                    dispose = StartLong(stopping.Dispose);
                    arrived = disposeArrived.Wait(Bound);
                }
                else if (p == "dispose-open-busy")
                    disposeArrived.Set();
            };

            Assert.Throws<TableBatchStoreException>(() => stopping.GetHours(_product));
            Assert.True(arrived, "the dispose never found the slot held by the open");
            Assert.True(dispose.Wait(Bound));
            Assert.True(Directory.Exists(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName)));

            // After the restart the folder of Hours exists, and the hours are back all the same.
            var reopened = Open();
            Assert.Equal([Week0, Week0 + Hour], reopened.GetHours(_product).Select(h => h.HourTicks));
            Assert.Equal(2, reopened.Statistics.HoursRebuilt);
        }

        [Fact]
        public void A_new_product_is_marked_complete_at_its_creation_and_its_empty_hours_are_the_truth()
        {
            // A first hour whose commit was not written: its part is on disk, Hours is empty and has its complete key.
            var store = Open();
            store.Checkpoint = p => { if (p == "before-commit") throw new CheckpointException(p); };
            Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0), TwoParts()));
            store.CloseWithoutFlush();

            var reopened = Open();
            Assert.Empty(reopened.GetHours(_product));
            Assert.False(reopened.IsHourMerged(_product, Week0));
            Assert.Equal(0, reopened.Statistics.HoursRebuilt);
            Assert.Equal(0, reopened.Statistics.PlacementsRebuilt);
        }

        [Fact]
        public void A_read_counts_the_batches_it_could_not_read()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 2))]);
            store.WriteMoved(_product, new TableBatchWeek(Week0, 4), Week0 + Hour, Mark(Week0 + Hour), [Batch(_sensorA, Week0 + Hour + 5, 0)]);

            store.BatchMissing = (hour, ordinal) => ordinal == 1 || hour == Week0 + Hour;
            var read = store.ReadSensor(_product, _sensorA, 4, [Week0, Week0 + Hour], out var failed);
            store.BatchMissing = null;

            Assert.Equal([0], read.Select(r => r.Ordinal));
            Assert.Equal(2, failed);
            Assert.Equal(2, store.Statistics.ReadFailures);
        }

        [Fact]
        public void A_read_with_a_range_skips_a_record_out_of_it_before_its_batch_so_only_a_missing_batch_in_the_range_fails()
        {
            // A's batches of hour 0 in P4: [+0, +9], [+10, +19], [+20, +29]; and a moved batch of hour 1, [+5, +14].
            var store = Open();
            long hour1 = Week0 + Hour;
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 3))]);
            store.WriteMoved(_product, new TableBatchWeek(Week0, 4), hour1, Mark(hour1), [Batch(_sensorA, hour1 + 5, 0)]);

            // Every batch survives as its index record only. A range that meets three of the four records counts those three
            // (#1850 part 4c): the first, out of it, is skipped before its batch is fetched.
            store.BatchMissing = (_, _) => true;
            var read = store.ReadSensor(_product, _sensorA, 4, [Week0, hour1], Week0 + 10, hour1 + 6, out var failed);
            Assert.Empty(read);
            Assert.Equal((3, 3L), (failed, store.Statistics.ReadFailures));

            // [from, to): a record whose last time is the first tick of the range meets it; one that starts at its end does not.
            read = store.ReadSensor(_product, _sensorA, 4, [Week0, hour1], Week0 + 29, hour1 + 5, out failed);
            Assert.Equal((0, 1), (read.Count, failed));
            store.BatchMissing = null;

            read = store.ReadSensor(_product, _sensorA, 4, [Week0, hour1], Week0 + 19, hour1 + 5, out failed);
            Assert.Equal([(Week0, 1, false), (Week0, 2, false)], read.Select(r => (r.HourTicks, r.Ordinal, r.Moved)));
            Assert.Equal(0, failed);

            read = store.ReadSensor(_product, _sensorA, 4, [Week0, hour1], hour1 + 14, hour1 + 15, out failed);
            Assert.Equal([(hour1, 0, true)], read.Select(r => (r.HourTicks, r.Ordinal, r.Moved)));

            // Without a range, every record.
            Assert.Equal(4, store.ReadSensor(_product, _sensorA, 4, [Week0, hour1], out failed).Count);
            Assert.Equal(4L, store.Statistics.ReadFailures);
        }

        [Fact]
        public void A_sensors_generation_changes_with_its_own_placement_and_moved_batches_and_with_every_write_of_its_product()
        {
            var store = Open();
            var sensorC = Guid.NewGuid();
            var (p1, p4) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4));
            store.WriteHour(_product, Commit(Week0), [Part(1, (_sensorA, 0, 1), (_sensorB, 0, 1))]);

            var readings = new List<(long Product, long A, long B, long C)>();
            (long Product, long A, long B, long C) Read()
            {
                var reading = (store.GetGeneration(_product), store.GetGeneration(_product, _sensorA), store.GetGeneration(_product, _sensorB),
                    store.GetGeneration(_product, sensorC));
                readings.Add(reading);
                return reading;
            }

            // A write of one sensor changes that sensor's generation and the product's, and no other sensor's (#1850 part 4c).
            void OnlyA(Action write)
            {
                var before = Read();
                write();
                var after = Read();
                Assert.True(after.A != before.A && after.Product != before.Product, "the sensor's and the product's generation change");
                Assert.Equal((before.B, before.C), (after.B, after.C));
            }

            // A write of the product changes every sensor's generation, one never named included.
            void Everyone(Action write)
            {
                var before = Read();
                write();
                var after = Read();
                Assert.True(after.Product != before.Product && after.A != before.A && after.B != before.B && after.C != before.C,
                    $"every generation changes: {before} -> {after}");
            }

            OnlyA(() => store.SetPlacement(_product, _sensorA, new TablePlacement(4, Week0, [new TablePlacementSegment(Week0, Week0 + Hour, 1)])));
            // Moved batches of A, into a week database the write creates.
            OnlyA(() => store.WriteMoved(_product, p4, Week0, store.ReadMark(_product, p1, Week0), [Batch(_sensorA, Week0, 0)]));
            Assert.Contains(p4, store.GetWeeks(_product));
            OnlyA(() => store.SetPlacement(_product, _sensorA, new TablePlacement(4, Week0)));
            OnlyA(() => store.SetPlacement(_product, _sensorA, null));

            // A commit; a week database created by the write of an hour that does not reach its commit; a drop; hours forgotten.
            Everyone(() => store.WriteHour(_product, Commit(Week0 + Hour, 2), [Part(1, (_sensorB, Hour, 1))]));
            store.Checkpoint = point =>
            {
                if (point == "part-written")
                    throw new CheckpointException(point);
            };
            Everyone(() => Assert.Throws<CheckpointException>(() => store.WriteHour(_product, Commit(Week0 + 2 * Hour, 3), [Part(5, (_sensorB, 2 * Hour, 1))])));
            store.Checkpoint = null;
            Assert.False(store.IsHourMerged(_product, Week0 + 2 * Hour));
            Everyone(() => store.WriteHour(_product, Commit(Week0 + Week, 4), [Part(1, (_sensorB, Week, 1))]));
            Everyone(() => store.DropWeek(_product, p4));
            store.DropWeek(_product, new TableBatchWeek(Week0 + Week, 1));
            Everyone(() => store.ForgetHours(_product, [Week0 + Week]));

            // Never back, for any reader (the readings are in the order taken).
            for (int i = 1; i < readings.Count; i++)
                Assert.True(readings[i].Product >= readings[i - 1].Product && readings[i].A >= readings[i - 1].A &&
                            readings[i].B >= readings[i - 1].B && readings[i].C >= readings[i - 1].C, $"reading {i} went back");

            // A product without a folder: 0.
            Assert.Equal(0, store.GetGeneration(Guid.NewGuid(), _sensorA));
        }

        // === Placements (#1850 part 4b) ===

        [Fact]
        public void A_placement_is_stored_durably_and_read_back_after_a_restart()
        {
            var store = Open();
            var placement = new TablePlacement(5, Week0 + Hour, [new TablePlacementSegment(Week0 + Hour, Week0 + 3 * Hour, 1)]);
            store.SetPlacement(_product, _sensorA, placement);
            store.SetPlacement(_product, _sensorB, new TablePlacement(0, Week0));
            store.Dispose();

            var reopened = Open();
            Assert.Equal(placement, reopened.GetPlacement(_product, _sensorA));
            Assert.Equal(Week0 + Hour, reopened.GetPlacement(_product, _sensorA).FirstHour);
            Assert.Equal(1, reopened.GetPlacement(_product, _sensorA).ClassOf(Week0 + 2 * Hour));
            Assert.Equal(5, reopened.GetPlacement(_product, _sensorA).ClassOf(Week0 + 3 * Hour));
            Assert.Equal(2, reopened.GetPlacements(_product).Count);
            Assert.Null(reopened.GetPlacement(_product, Guid.NewGuid()));

            reopened.SetPlacement(_product, _sensorB, null);
            reopened.Dispose();
            Assert.Null(Open().GetPlacement(_product, _sensorB));
            Assert.Equal(0, _stores[^1].Statistics.PlacementsRebuilt);
        }

        [Fact]
        public void Lost_placements_are_rebuilt_from_the_indexes_of_the_week_databases()
        {
            var store = Open();
            // A in P1 for hours 0 and 1, then (its period changed) in P5 from hour 2; B in P5 throughout; C moved into P5 for
            // hour 1 (the region of moved batches) and in P5 from hour 2.
            store.WriteHour(_product, Commit(Week0, 1), [Part(1, (_sensorA, 0, 1)), Part(5, (_sensorB, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + Hour, 2), [Part(1, (_sensorA, 0, 1)), Part(5, (_sensorB, 0, 1))]);
            store.WriteHour(_product, Commit(Week0 + 2 * Hour, 3), [Part(5, (_sensorA, 0, 1), (_sensorB, 0, 1))]);
            var sensorC = Guid.NewGuid();
            store.WriteMoved(_product, new TableBatchWeek(Week0, 5), Week0 + Hour, store.ReadMark(_product, new TableBatchWeek(Week0, 1), Week0 + Hour),
                [Batch(sensorC, Week0 + Hour + 5, 0)]);
            store.Dispose();

            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.PlacementsName), recursive: true);

            var reopened = Open();
            // Each sensor's first hour is its first hour with a batch, and no segment starts before it.
            Assert.Equal(new TablePlacement(5, Week0, [new TablePlacementSegment(Week0, Week0 + 2 * Hour, 1)]), reopened.GetPlacement(_product, _sensorA));
            Assert.Equal(new TablePlacement(5, Week0), reopened.GetPlacement(_product, _sensorB));
            Assert.Equal(new TablePlacement(5, Week0 + Hour), reopened.GetPlacement(_product, sensorC));
            Assert.Equal(1, reopened.Statistics.PlacementsRebuilt);
            reopened.Dispose();

            // Rebuilt once: from then on it is the truth.
            var again = Open();
            Assert.Equal(3, again.GetPlacements(_product).Count);
            Assert.Equal(0, again.Statistics.PlacementsRebuilt);
        }

        [Fact]
        public void Damaged_placements_are_set_aside_and_rebuilt_and_so_is_a_rebuild_that_stopped_in_its_scan()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, 1), [Part(1, (_sensorA, 0, 1)), Part(4, (_sensorB, 0, 1))]);
            store.Dispose();

            var placementsPath = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.PlacementsName);
            using (var db = new DB(placementsPath, new Options()))
                db.Put(_sensorA.ToByteArray(), [9, 9], new WriteOptions());

            var reopened = Open();
            reopened.Checkpoint = p => { if (p == "placements-scanning") throw new CheckpointException(p); };
            Assert.Throws<TableBatchStoreException>(() => reopened.GetPlacements(_product));
            reopened.Dispose();
            Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(placementsPath), TableBatchDatabase.PlacementsName + ".damaged-*"));

            var again = Open();
            Assert.Equal(new TablePlacement(1, Week0), again.GetPlacement(_product, _sensorA));
            Assert.Equal(new TablePlacement(4, Week0), again.GetPlacement(_product, _sensorB));
            Assert.Equal(1, again.Statistics.PlacementsRebuilt);
        }

        [Fact]
        public void A_change_of_period_keeps_every_earlier_hour_in_its_class_and_a_change_back_drops_the_segment()
        {
            const long first = 10 * Hour, c1 = 100 * Hour, c2 = 200 * Hour, c3 = 300 * Hour;

            // The segment of the old class starts at the sensor's first hour, not at the start of time: the sensor has no
            // rows before it, and a segment is what makes retention keep a week database.
            var p = new TablePlacement(5, first).Retarget(1, c1);
            Assert.Equal([new TablePlacementSegment(first, c1, 5)], p.Segments);
            Assert.Equal((5, 1), (p.ClassOf(c1 - Hour), p.ClassOf(c1)));
            Assert.Equal(first, p.FirstHour);

            p = p.Retarget(53, c2);
            Assert.Equal([new TablePlacementSegment(first, c1, 5), new TablePlacementSegment(c1, c2, 1)], p.Segments);
            Assert.Equal((5, 1, 53), (p.ClassOf(first), p.ClassOf(c1), p.ClassOf(c2)));

            // A segment before the first hour is refused, and so is a first hour the store did not write.
            Assert.Throws<ArgumentException>(() => new TablePlacement(1, first, [new TablePlacementSegment(first - Hour, c1, 5)]));
            Assert.Throws<FormatException>(() => TableBatchDatabase.DecodePlacement([1, 5, 0, 0, 0, 0, 0]));
            Assert.Equal(p, TableBatchDatabase.DecodePlacement(TableBatchDatabase.EncodePlacement(p)));

            // Back to 5: the hours in 5 are where the sensor's hours belong again; those in 53 become a segment.
            p = p.Retarget(5, c3);
            Assert.Equal([new TablePlacementSegment(c1, c2, 1), new TablePlacementSegment(c2, c3, 53)], p.Segments);
            Assert.Equal((5, 1, 53, 5), (p.ClassOf(first), p.ClassOf(c1), p.ClassOf(c2), p.ClassOf(c3)));

            Assert.Same(p, p.Retarget(5, c3 + Hour));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Retarget(1, c3 - Hour));

            // A move takes hours out of the segments.
            Assert.Equal([new TablePlacementSegment(c1, c1 + Hour, 1), new TablePlacementSegment(c2 + Hour, c3, 53)],
                p.WithoutHours(c1 + Hour, c2 + Hour).Segments);
        }

        [Fact]
        public void A_part_written_again_also_deletes_the_moved_batches_of_its_hour_in_that_database()
        {
            var store = Open();
            var sensorC = Guid.NewGuid();

            // A move had written C's batches of hours 1 and 2 into P4, each with the mark of its hour (the move leaves it
            // itself: no run of the merge wrote into P4). Hour 1 is not committed here (as after a loss of Hours that did not
            // rebuild it), so the merge writes it again.
            var p4 = new TableBatchWeek(Week0, 4);
            store.WriteMoved(_product, p4, Week0 + Hour, Mark(Week0 + Hour), [Batch(sensorC, Week0 + Hour + 5, 0)]);
            store.WriteMoved(_product, p4, Week0 + 2 * Hour, Mark(Week0 + 2 * Hour), [Batch(sensorC, Week0 + 2 * Hour + 5, 0)]);
            Assert.Equal(Mark(Week0 + Hour), store.ReadMark(_product, p4, Week0 + Hour));
            Assert.Single(store.ReadSensor(_product, sensorC, 4, [Week0 + Hour]));

            // The repeat writes the part: everything P4 held of hour 1 is replaced, the moved batches of hour 1
            // included; another hour's moved batches stay.
            store.WriteHour(_product, Commit(Week0 + Hour), [Part(4, (_sensorA, 0, 1), (sensorC, 0, 1))]);
            var read = store.ReadSensor(_product, sensorC, 4, [Week0 + Hour, Week0 + 2 * Hour]);
            Assert.Equal([(Week0 + Hour, false), (Week0 + 2 * Hour, true)], read.Select(r => (r.HourTicks, r.Moved)));
            Assert.Equal(1, store.Statistics.PartsRewritten);
        }

        [Fact]
        public void Writing_an_hour_clears_every_key_of_it_in_every_database_of_its_week_with_or_without_its_mark()
        {
            var store = Open();
            var sensorC = Guid.NewGuid();
            var p4 = new TableBatchWeek(Week0, 4);
            long hour1 = Week0 + Hour, hour2 = Week0 + 2 * Hour;

            // Moved batches of C for hours 1 and 2 in P4, whose marks are then gone (taken out of the database by hand: the
            // store itself never leaves moved batches without the mark of their hour). Neither hour is committed.
            store.WriteMoved(_product, p4, hour1, Mark(hour1), [Batch(sensorC, hour1 + 5, 0)]);
            store.WriteMoved(_product, p4, hour2, Mark(hour2), [Batch(sensorC, hour2 + 5, 0)]);
            var path = store.WeekPath(_product, Week0, 4);
            store.Dispose();

            using (var db = new DB(path, new Options()))
            {
                db.Delete(TableBatchDatabase.HourKey(hour1, TableBatchDatabase.TagMark), new WriteOptions { Sync = true });
                db.Delete(TableBatchDatabase.HourKey(hour2, TableBatchDatabase.TagMark), new WriteOptions { Sync = true });
            }

            store = Open();
            Assert.Null(store.ReadMark(_product, p4, hour1));
            Assert.Single(store.ReadSensor(_product, sensorC, 4, [hour1]));

            // Hour 1 is written with C in P4, the database of the copy: the copy goes although no mark pointed at it.
            store.WriteHour(_product, Commit(hour1, 1), [Part(4, (_sensorA, 0, 1), (sensorC, 0, 1))]);
            Assert.Equal([false], store.ReadSensor(_product, sensorC, 4, [hour1]).Select(r => r.Moved));

            // Hour 2 is written with C in another class: the copy in P4, which the record does not name, goes too.
            store.WriteHour(_product, Commit(hour2, 2), [Part(5, (_sensorA, 0, 1), (sensorC, 0, 1))]);
            Assert.Empty(store.ReadSensor(_product, sensorC, 4, [hour2]));
            Assert.Equal([false], store.ReadSensor(_product, sensorC, 5, [hour2]).Select(r => r.Moved));
            Assert.Equal(2, store.Statistics.PartsRewritten);
        }

        [Fact]
        public void Moved_batches_always_come_with_the_mark_of_their_hour_and_a_rebuild_takes_the_hour_with_its_own_classes()
        {
            var store = Open();
            var sensorC = Guid.NewGuid();
            long hour = Week0 + Hour;
            var (p1, p4, p5) = (new TableBatchWeek(Week0, 1), new TableBatchWeek(Week0, 4), new TableBatchWeek(Week0, 5));

            store.WriteHour(_product, Commit(hour), [Part(1, (_sensorA, 0, 1), (sensorC, 0, 1)), Part(5, (_sensorB, 0, 1))]);
            var mark = store.ReadMark(_product, p1, hour);
            Assert.NotNull(mark);
            Assert.Equal(mark, store.ReadMark(_product, p5, hour));

            // Into a database without a mark of the hour (a new one): the mark is written with the batches, the same bytes.
            store.WriteMoved(_product, p4, hour, mark, [Batch(sensorC, hour + 5, 0)]);
            Assert.Equal(mark, store.ReadMark(_product, p4, hour));

            // Into a database that has the hour's mark: it stays as it is.
            store.WriteMoved(_product, p5, hour, mark, [Batch(sensorC, hour + 5, 0)]);
            Assert.Equal(mark, store.ReadMark(_product, p5, hour));

            // Bytes that are not the record of that hour are refused before anything is written.
            Assert.Throws<ArgumentException>(() => store.WriteMoved(_product, p4, hour + Hour, mark, [Batch(sensorC, hour + Hour + 5, 0)]));
            Assert.Throws<ArgumentException>(() => store.WriteMoved(_product, p4, hour + Hour, [1, 2, 3], [Batch(sensorC, hour + Hour + 5, 0)]));
            Assert.Null(store.ReadMark(_product, p4, hour + Hour));
            Assert.Empty(store.ReadSensor(_product, sensorC, 4, [hour + Hour]));

            // Hours lost: the marks of the hour agree in every database, the one moved into included, so it is rebuilt,
            // with the classes of its record.
            store.Dispose();
            DeleteHours();
            var reopened = Open();
            Assert.Equal([1, 5], Assert.Single(reopened.GetHours(_product)).Classes.Select(c => c.PeriodWeeks));
        }

        // === Dispose ===

        [Fact]
        public void A_dispose_waits_for_a_read_that_holds_a_database_and_refuses_what_starts_after_it()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 2))]);

            using var holding = new ManualResetEventSlim();
            using var disposeWaiting = new ManualResetEventSlim();
            var gate = new ManualResetEventSlim(); // not disposed: a thread of a failed test may still wait on it
            store.Checkpoint = p =>
            {
                if (p == "read-holding")
                {
                    holding.Set();
                    gate.Wait();
                }
                else if (p == "dispose-waiting")
                    disposeWaiting.Set();
            };

            var read = StartLong(() => store.ReadSensor(_product, _sensorA, 4, [Week0]));
            Assert.True(holding.Wait(Bound), "the read holds the week");
            var dispose = StartLong(store.Dispose);
            Assert.True(disposeWaiting.Wait(Bound), "the dispose waits for the read");

            Assert.False(dispose.IsCompleted);
            Assert.Throws<TableBatchStoreException>(() => store.GetHours(_product));
            Assert.Throws<TableBatchStoreException>(() => store.ReadSensor(_product, _sensorA, 4, [Week0]));

            gate.Set();
            Assert.True(read.Wait(Bound));
            Assert.Equal(2, read.Result.Count);
            Assert.True(dispose.Wait(Bound));
            Assert.Equal(0, store.OpenDatabases);
        }

        [Fact]
        public void A_dispose_waits_for_a_write_in_flight_and_the_write_commits()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0, sequence: 1), [Part(4, (_sensorA, 0, 1))]);

            using var writing = new ManualResetEventSlim();
            using var disposeWaiting = new ManualResetEventSlim();
            var gate = new ManualResetEventSlim(); // not disposed: a thread of a failed test may still wait on it
            store.Checkpoint = p =>
            {
                if (p == "before-commit")
                {
                    writing.Set();
                    gate.Wait();
                }
                else if (p == "dispose-product")
                    disposeWaiting.Set();
            };

            var write = StartLong(() => store.WriteHour(_product, Commit(Week0 + Hour, sequence: 2), [Part(4, (_sensorA, 0, 1))]));
            Assert.True(writing.Wait(Bound), "the write is in flight");
            var dispose = StartLong(store.Dispose);
            Assert.True(disposeWaiting.Wait(Bound), "the dispose waits for the product's lock");

            Assert.False(dispose.IsCompleted);
            Assert.Throws<TableBatchStoreException>(() => store.WriteHour(_product, Commit(Week0 + 2 * Hour, sequence: 3), [Part(4, (_sensorA, 0, 1))]));

            gate.Set();
            Assert.True(write.Wait(Bound));
            Assert.True(dispose.Wait(Bound));

            var reopened = Open();
            Assert.True(reopened.IsHourMerged(_product, Week0 + Hour));
            Assert.False(reopened.IsHourMerged(_product, Week0 + 2 * Hour));
            Assert.Equal(2L, reopened.GetWatermarks(_product)[_producer]);
        }

        // === Faults of the hour write ===

        [Fact]
        public void A_period_beyond_the_record_is_an_argument_error_and_not_a_store_fault()
        {
            var store = Open();

            Assert.Throws<ArgumentException>(() => store.WriteHour(_product, Commit(Week0), [Part(ushort.MaxValue + 1, (_sensorA, 0, 1))]));
            Assert.Equal(0, store.Statistics.WriteFailures);
            Assert.False(store.IsHourMerged(_product, Week0));
        }

        [Fact]
        public void A_failed_flush_of_the_previous_week_is_a_counted_store_fault_and_the_week_is_flushed_at_its_close()
        {
            // Two databases in the previous week: the first flush fails, the second is not reached; both leases are let go.
            var store = Open();
            store.WriteHour(_product, Commit(Week0), TwoParts());

            store.Checkpoint = p => { if (p == "flush") throw new IOException("flush failure injected by the test"); };
            Assert.Throws<TableBatchStoreException>(() => store.WriteHour(_product, Commit(Week0 + Week), [Part(4, (_sensorA, 0, 1))]));
            Assert.Equal(1, store.Statistics.WriteFailures);
            Assert.False(store.IsHourMerged(_product, Week0 + Week));

            store.Checkpoint = null;
            store.WriteHour(_product, Commit(Week0 + Week), [Part(4, (_sensorA, 0, 1))]);
            Assert.True(store.IsHourMerged(_product, Week0 + Week));

            // The failed week was unpinned; it is flushed when it is closed.
            Assert.True(StartLong(store.Dispose).Wait(Bound), "the dispose waits for no lease left behind");
            Assert.Equal(3, store.Statistics.WeeksFlushed);
            Assert.All(Directory.GetFiles(store.WeekPath(_product, Week0, 4), "*.log"), f => Assert.Equal(0, new FileInfo(f).Length));
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
            store.WriteMoved(_product, new TableBatchWeek(Week0, 4), Week0 + Hour, Mark(Week0 + Hour), [Batch(_sensorA, Week0 + Hour + 7, 0), Batch(_sensorA, Week0 + Hour + 17, 1)]);

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


        // === Opening a product (#1850 part 4: outside the store-wide lock) ===

        [Fact]
        public void Two_calls_for_the_same_product_get_one_open_and_another_product_is_not_held_up_meanwhile()
        {
            var other = Guid.NewGuid();
            var first = Open();
            first.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            first.WriteHour(other, Commit(Week0), [Part(4, (_sensorB, 0, 1))]);
            first.Dispose();

            var hoursOpens = new Dictionary<string, int>();
            var store = Open(open: (path, options) =>
            {
                if (path.EndsWith(TableBatchDatabase.HoursName))
                    lock (hoursOpens)
                        hoursOpens[path] = hoursOpens.GetValueOrDefault(path) + 1;
                return new DB(path, options);
            });

            // The order of events is forced by the store's own checkpoints: the first open (the product's) stays inside its
            // open, under the slot's gate, until the test releases it (no bound: nothing proceeds because a wait expired);
            // the second caller says that it found the slot held ("product-busy") right before it waits for it. Every
            // bounded wait below is an assertion: its expiry fails the test and changes nothing else.
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var secondArrived = new ManualResetEventSlim();
            int openings = 0;
            store.Checkpoint = p =>
            {
                if (p == "product-opening" && Interlocked.Increment(ref openings) == 1)
                {
                    entered.Set();
                    release.Wait();
                }
                else if (p == "product-busy")
                    secondArrived.Set();
            };

            try
            {
                var opener = StartLong(() => store.GetHours(_product));
                Assert.True(entered.Wait(Bound), "the open never reached its checkpoint");

                var waiter = StartLong(() => store.GetHours(_product));
                Assert.True(secondArrived.Wait(Bound), "the second caller never found the slot held");

                // While the product opens, another product opens and the store-wide calls answer.
                var meanwhile = StartLong(() => (store.GetHours(other).Count, store.IsOpened, store.WeekDatabases));
                Assert.True(meanwhile.Wait(Bound), "a call on another product waited for the open of the first one");
                Assert.Equal((1, true, 1), meanwhile.Result);

                // Neither call on the product has returned: the opener is held at its checkpoint, the second waits for it.
                Assert.False(opener.IsCompleted);
                Assert.False(waiter.IsCompleted);

                release.Set();
                Assert.True(opener.Wait(Bound) && waiter.Wait(Bound), "the calls on the product did not end after the release");

                Assert.Equal(Week0, Assert.Single(opener.Result).HourTicks);
                Assert.Equal(Week0, Assert.Single(waiter.Result).HourTicks);
                Assert.Equal(1, hoursOpens.Single(p => p.Key.Contains(_product.ToString("N"))).Value);
                Assert.Equal(2, openings);
            }
            finally
            {
                // A test that failed must not leave the opener held.
                release.Set();
            }
        }

        [Fact]
        public void An_open_that_failed_is_retried_by_the_next_call()
        {
            var first = Open();
            first.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            first.Dispose();

            int calls = 0;
            var store = Open(open: (path, options) =>
            {
                if (path.EndsWith(TableBatchDatabase.HoursName) && ++calls == 1)
                    throw new Exception("IO error: the disk is not there for a moment");
                return new DB(path, options);
            });

            Assert.Throws<TableBatchStoreException>(() => store.GetHours(_product));
            Assert.False(store.IsOpened);

            // Not an exception kept for the product until a restart: the next call opens it.
            Assert.Single(store.GetHours(_product));
            Assert.True(store.IsHourMerged(_product, Week0));
            Assert.Equal(2, calls);
        }

        [Fact]
        public void An_open_whose_rebuild_fails_at_a_later_week_leaves_no_week_database_open()
        {
            var first = Open();
            first.WriteHour(_product, Commit(Week0, 1), [Part(4, (_sensorA, 0, 1))]);
            first.WriteHour(_product, Commit(Week0 + 7 * 24 * Hour, 2), [Part(4, (_sensorA, 0, 1))]);
            first.Dispose();
            DeleteHours();

            // The rebuild of Hours scans the first week and fails to open the second.
            var secondWeek = TableBatchDatabase.WeekName(Week0 + 7 * 24 * Hour, 4);
            var fail = true;
            var store = Open(open: (path, options) =>
            {
                if (fail && path.EndsWith(secondWeek))
                    throw new Exception("IO error: the disk is not there for a moment");
                return new DB(path, options);
            });

            Assert.Throws<TableBatchStoreException>(() => store.GetHours(_product));
            Assert.False(store.IsOpened);
            Assert.Equal(0, store.OpenDatabases);

            // The next call opens the product again and rebuilds both hours.
            fail = false;
            Assert.Equal([Week0, Week0 + 7 * 24 * Hour], store.GetHours(_product).Select(h => h.HourTicks));
        }

        [Fact]
        public void A_dispose_during_an_open_waits_for_it_and_leaves_nothing_of_it_open()
        {
            var first = Open();
            first.WriteHour(_product, Commit(Week0), [Part(4, (_sensorA, 0, 1))]);
            first.Dispose();

            // As above: the open is held at its checkpoint until released (no bound), and the dispose says that it found the
            // slot held by the open ("dispose-open-busy") right before it waits for it; by then it has marked the store closed.
            var store = Open();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var disposeArrived = new ManualResetEventSlim();
            store.Checkpoint = p =>
            {
                if (p == "product-opening")
                {
                    entered.Set();
                    release.Wait();
                }
                else if (p == "dispose-open-busy")
                    disposeArrived.Set();
            };

            try
            {
                var opener = StartLong(() => Record.Exception(() => store.GetHours(_product)));
                Assert.True(entered.Wait(Bound), "the open never reached its checkpoint");

                var disposer = StartLong(store.Dispose);
                Assert.True(disposeArrived.Wait(Bound), "the dispose never found the slot held by the open");
                Assert.False(disposer.IsCompleted);

                release.Set();
                Assert.True(opener.Wait(Bound) && disposer.Wait(Bound), "the open and the dispose did not end after the release");
                Assert.IsType<TableBatchStoreException>(opener.Result);
            }
            finally
            {
                release.Set();
            }

            // The hours database the open had opened was closed: another store opens the same folder.
            Assert.Single(Open().GetHours(_product));
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

        // The bound of a wait that is an assertion: its expiry fails the test (a thread that never reaches its point).
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

        private static Task<T> StartLong<T>(Func<T> action) => Task.Factory.StartNew(action, TaskCreationOptions.LongRunning);

        private static Task StartLong(Action action) => Task.Factory.StartNew(action, TaskCreationOptions.LongRunning);

        private void DeleteHours() =>
            Directory.Delete(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName), recursive: true);

        // A record in the hours database that is not one: the next open puts it aside and rebuilds it from the marks.
        private void DamageHours()
        {
            var hoursPath = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName);
            using var db = new DB(hoursPath, new Options());
            db.Put([0, 0, 0, 0, 0, 0, 0, 1], [9, 9, 9], new WriteOptions());
        }

        private TableBatchHourCommit Commit(long hour, long sequence = 1) => new()
        {
            HourTicks = hour,
            FirstBlockSequence = sequence,
            LastBlockSequence = sequence,
            BlocksMerged = 1,
            Watermarks = new Dictionary<Guid, long> { [_producer] = sequence },
        };

        // The record of an hour as a commit writes it, for a move of an hour this store did not commit.
        private byte[] Mark(long hour, long sequence = 1) => TableBatchDatabase.EncodeCommit(Commit(hour, sequence), [], Week0);

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
