using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.TableSensors;
using HSMTableBlocks;
using Xunit;

namespace HSMServer.Core.Tests.TableSensors
{
    /// <summary>
    /// The move between period classes (#1850 part 4c): copy and expired steps, every durable write as a crash point, the
    /// recovery with its in-flight record and without it, a period changed again, the limits of a step (hours not
    /// committed, a dropped target, the disk), reads during a step, the budget, and the compaction of the sources. No
    /// timing: the clock is the pass's parameter, the budget is <see cref="TableMergeJob.MoveStepsPerPass"/>.
    /// </summary>
    public partial class TableMergeJobTests
    {
        private static readonly TimeIntervalModel KeepP52 = new(TimeSpan.FromDays(364).Ticks);
        private static readonly TimeIntervalModel KeepP5 = new(TimeSpan.FromDays(35).Ticks);
        private static readonly TimeIntervalModel KeepP1 = new(TimeSpan.FromDays(7).Ticks);

        // After the reconciliation of the period change, in the hour after the rows: the time of the pass that moves them.
        private static readonly DateTime MoveAt = Hour0.AddHours(3).AddMinutes(10);


        // === A copy step, an expired step, a lengthened period ===

        [Fact]
        public async Task A_shortened_period_copies_switches_and_deletes_and_every_read_between_the_writes_returns_the_same_rows()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);
            var points = new List<string>();

            _server.Job.Checkpoint = p =>
            {
                if (!p.StartsWith("move-"))
                    return;

                points.Add(p);
                Assert.Equal(before, All(s, r));
            };

            var report = Pass(MoveAt);
            _server.Job.Checkpoint = null;

            Assert.Null(report.Fault);
            Assert.Equal(["move-recorded", "move-copied", "move-switched", "move-source-deleted", "move-done"], points);
            Assert.Equal(1, report.MoveSteps);
            Assert.True(report.MovedBytes > 0);
            Assert.Equal(before, All(s, r));

            // Every row of s is in P5 now, none in P52; r's rows are where they were; nothing is left in flight.
            Assert.Equal(new TablePlacement(5, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal((3, 0, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal(3, _server.Batches.MergeStatistics.RowsMoved);
            Assert.Equal(report.MovedBytes, _server.Batches.MergeStatistics.MovedBytes);
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveStepsPending);

            // A second pass has nothing to do.
            Assert.Equal(0, Pass(MoveAt.AddMinutes(1)).MoveSteps);
        }

        [Fact]
        public async Task A_week_larger_than_a_step_is_moved_in_several_steps_each_one_copy_of_at_most_the_bound()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);
            var recorded = new List<TableMoveInFlight>();

            // A bound below one hour's batches: every hour is a step of its own (at least one hour a step).
            _server.Job.StepBytesLimit = 1;
            _server.Job.Checkpoint = p =>
            {
                if (p == "move-recorded")
                    recorded.Add(_server.Batches.GetMoveInFlight(_product));
            };

            var report = Pass(MoveAt);
            _server.Job.Checkpoint = null;

            Assert.Null(report.Fault);
            Assert.Equal(3, report.MoveSteps);
            Assert.Equal([0, 1, 2], recorded.Select(m => (int)((m.FirstHour - Hour0.Ticks) / TimeSpan.TicksPerHour)));
            Assert.All(recorded, m => Assert.Equal(TimeSpan.TicksPerHour, m.EndHour - m.FirstHour));
            Assert.Equal((3, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(before, All(s, r));
        }

        [Fact]
        public async Task A_move_returns_the_same_rows_in_the_same_order_when_batches_of_an_hour_are_not_in_time_order_by_ordinal_and_tie_across_batches()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(1, "s", "r");

            // Hour 1, written as a merged hour: s's two batches out of time order by ordinal (the first one's rows are later),
            // with a time in both, so equal times are ordered by the batches' ranks.
            long h1 = Hour0.AddHours(1).Ticks;
            TableBatchToWrite Batch(BaseSensorModel sensor, params (long Time, double Bid, long Size)[] rows) =>
                new(sensor.Id, rows.Min(x => x.Time), rows.Max(x => x.Time), rows.Length, HSMDatabase.SealedHistory.TableBatchCodec.Encode(new HSMDatabase.SealedHistory.TableBatch(Quotes.Id, [
                    HSMDatabase.SealedHistory.TableBatchColumn.Time(rows.Select(x => x.Time).ToArray()),
                    new HSMDatabase.SealedHistory.TableBatchColumn(TableColumnType.Double, null, rows.Select(x => x.Bid).ToArray(), null),
                    new HSMDatabase.SealedHistory.TableBatchColumn(TableColumnType.Int64, rows.Select(x => x.Size).ToArray(), null, null)])));

            _now = Hour0.AddHours(2).AddMinutes(3);
            _server.Batches.WriteHour(_product, new TableBatchHourCommit
            {
                HourTicks = h1,
                FirstBlockSequence = 1000,
                LastBlockSequence = 1000,
                BlocksMerged = 1,
                Watermarks = new Dictionary<Guid, long>(_server.Batches.GetWatermarks(_product)),
            }, [new TableBatchHourPart(52, [
                Batch(s, (h1 + 30 * TimeSpan.TicksPerMinute, 1.0, 1), (h1 + 31 * TimeSpan.TicksPerMinute, 2.0, 2)),
                Batch(s, (h1 + 10 * TimeSpan.TicksPerMinute, 3.0, 3), (h1 + 30 * TimeSpan.TicksPerMinute, 4.0, 4)),
                Batch(r, (h1 + 5 * TimeSpan.TicksPerMinute, 5.0, 5)),
            ])]);

            // Hour 2 through the merge: 5,000 rows of s in two batches, many equal times across the cut, out of order in the blocks.
            _now = Hour0.AddHours(2).AddMinutes(10);
            var rows = Enumerable.Range(0, 5000).Select(i => (Hour0.AddHours(2).AddSeconds((4999 - i) / 50), (double?)i, (long?)i)).ToArray();
            AssertStored(Put(Block(("s", rows), ("r", [(Hour0.AddHours(2).AddMinutes(1), 9.0, 9)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(3).AddMinutes(3)).HoursMerged);

            var before = All(s, r);
            Assert.Equal(2, _server.Batches.ReadSensor(_product, s.Id, 52, [Hour0.AddHours(2).Ticks]).Count);

            await KeepAsync(s, KeepP5);
            Assert.Equal(1, Pass(Hour0.AddHours(3).AddMinutes(10)).MoveSteps);

            Assert.Equal((5, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(before, All(s, r));
        }

        [Fact]
        public async Task A_period_shortened_into_expiry_deletes_the_rows_instead_of_copying_them()
        {
            var (s, r, at) = await ExpiredSetupAsync();
            var rows = Rows(r);

            var report = Pass(at);

            Assert.Null(report.Fault);
            Assert.Equal(2, report.MoveExpiredRows);
            Assert.Equal(2, _server.Batches.MergeStatistics.RowsExpiredByMove);
            Assert.Equal(0, _server.Batches.MergeStatistics.RowsMoved);
            Assert.Empty(Rows(s));
            Assert.Equal(rows, Rows(r));
            Assert.Equal((0, 0, 2), (Bytes(s, 0), Bytes(s, 1), Bytes(r, 0)));
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
        }

        [Fact]
        public async Task A_period_lengthened_between_the_selection_and_the_delete_of_an_expired_step_deletes_nothing()
        {
            var (s, _, at) = await ExpiredSetupAsync();
            var rows = Rows(s);

            var changed = false;
            _server.Job.Checkpoint = p =>
            {
                if (p != "move-expiring" || changed)
                    return;

                changed = true;
                Assert.True(Task.Run(() => _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = s.Id, KeepHistory = TimeIntervalModel.None })).GetAwaiter().GetResult().IsOk);
            };

            var report = Pass(at);
            _server.Job.Checkpoint = null;

            Assert.True(changed);
            Assert.Equal(0, report.MoveExpiredRows);
            Assert.Equal(rows, Rows(s));
            Assert.Equal(1, _server.Batches.GetPlacement(_product, s.Id).Target);

            // The next pass records the change back: the rows are where the sensor's period wants them.
            Pass(at.AddMinutes(1));
            Assert.Equal(new TablePlacement(0, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal(rows, Rows(s));
        }

        [Fact]
        public async Task A_lengthened_period_is_moved_so_retention_drops_the_old_class_at_its_own_time_and_the_rows_stay_readable()
        {
            var s = await RegisterAsync("s", keep: KeepP1);
            var r = await RegisterAsync("r", keep: KeepP1);
            PutHours(1, "s", "r");
            var rows = Rows(s);

            await KeepAsync(s, KeepP52);
            Assert.Equal(1, Pass(Hour0.AddHours(2)).MoveSteps);
            Assert.Equal((1, 0), (Bytes(s, 52), Bytes(s, 1)));
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);

            // P1 passes for the week: its database goes (nothing holds it for s any more), s is read in P52.
            var report = Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc));
            Assert.Equal(1, report.WeeksDropped);
            Assert.DoesNotContain(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveWeeksPending);
            Assert.Equal(rows, Rows(s));
            Assert.Empty(Rows(r));
        }


        // === The crash matrix ===

        [Theory]
        [InlineData("move-recorded")]
        [InlineData("move-copied")]
        [InlineData("move-switched")]
        [InlineData("move-source-deleted")]
        [InlineData("move-done")]
        [InlineData("expired-deleted")]
        [InlineData("expired-switched")]
        public async Task A_crash_after_any_durable_write_of_a_step_reads_the_same_and_a_rerun_completes_it_and_then_writes_nothing(string point)
        {
            var expired = point.StartsWith("expired");
            BaseSensorModel s, r;
            DateTime at;

            if (expired)
                (s, r, at) = await ExpiredSetupAsync();
            else
                ((s, r), at) = (await CopySetupAsync(), MoveAt);

            var before = All(s, r);
            // An expired step's rows may be gone once its delete is written: what a read returns after it.
            var after = expired ? new List<(long, double?, long?)> { (-1L, null, null) }.Concat(Rows(r)).Append((-1L, null, null)).ToList() : before;

            Crash(point);
            CrashedPass(at);
            await RestartAsync(crash: true);

            Assert.Equal(expired ? after : before, All(s, r));
            Assert.Equal(!expired && point != "move-done", _server.Batches.GetMoveInFlight(_product) is not null);

            var rerun = Pass(at.AddMinutes(1));
            Assert.Null(rerun.Fault);
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal(after, All(s, r));

            if (!expired)
                Assert.Equal((3, 0, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));

            // A second rerun writes nothing: no batch, no placement, no delete (each would change the sensor's generation).
            var generation = _server.Batches.GetGeneration(_product, s.Id);
            var written = _server.Batches.Statistics.BatchesWritten;
            var again = Pass(at.AddMinutes(2));
            Assert.Equal((0, null), (again.MoveSteps, again.Fault));
            Assert.Equal(generation, _server.Batches.GetGeneration(_product, s.Id));
            Assert.Equal(written, _server.Batches.Statistics.BatchesWritten);
            Assert.Equal(after, All(s, r));
        }

        [Theory]
        [InlineData("move-recorded", false)]
        [InlineData("move-copied", false)]
        [InlineData("move-switched", false)]
        [InlineData("move-source-deleted", false)]
        [InlineData("move-recorded", true)]
        [InlineData("move-copied", true)]
        [InlineData("move-switched", true)]
        [InlineData("move-source-deleted", true)]
        public async Task A_crash_whose_in_flight_record_is_lost_or_unreadable_reads_every_row_once_and_a_later_step_cleans_what_it_left(string point, bool unreadable)
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);
            var path = MovePath();

            Crash(point);
            CrashedPass(MoveAt);
            await RestartAsync(crash: true, beforeOpen: _ =>
            {
                if (unreadable)
                    File.WriteAllText(path, "2 not a record\n");
                else
                    File.Delete(path);
            });

            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal(before, All(s, r));

            // The rerun goes by the placement alone: a copy left in P5 (not switched) is deleted before the copy is made
            // again; a source left in P52 after the switch stays, never read.
            Assert.Null(Pass(MoveAt.AddMinutes(1)).Fault);
            Assert.Equal(before, All(s, r));
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
            Assert.Equal(point == "move-copied" ? 1 : 0, _server.Batches.MergeStatistics.MoveLeftoversCleaned);
            Assert.Equal((3, point == "move-switched" ? 3 : 0), (Bytes(s, 5), Bytes(s, 52)));

            // The period back to P52 (the sequence the store's refusal guards): the step into P52 meets the source left there,
            // deletes it first, and every row is read once.
            await KeepAsync(s, KeepP52);
            Assert.Null(Pass(MoveAt.AddMinutes(2)).Fault);
            Assert.Equal(before, All(s, r));
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
            Assert.Equal((0, 3), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(point is "move-copied" or "move-switched" ? 1 : 0, _server.Batches.MergeStatistics.MoveLeftoversCleaned);
        }

        [Fact]
        public async Task A_stale_in_flight_record_of_a_finished_step_deletes_nothing_in_the_target_and_every_row_is_read_once()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);

            TableMoveInFlight record = null;
            _server.Job.Checkpoint = p =>
            {
                if (p == "move-recorded")
                    record = _server.Batches.GetMoveInFlight(_product);
            };

            Assert.Null(Pass(MoveAt).Fault);
            _server.Job.Checkpoint = null;
            Assert.NotNull(record);
            Assert.Null(_server.Batches.GetMoveInFlight(_product));

            // The record of the finished step comes back with its old content (P52 -> P5): its source is gone, its target
            // holds the only copy. Recovery never copies from a class the placement does not name: nothing is deleted there.
            _server.Batches.SetMoveInFlight(_product, record);
            await RestartAsync(crash: true);
            Assert.Equal(record, _server.Batches.GetMoveInFlight(_product));

            Assert.Null(Pass(MoveAt.AddMinutes(1)).Fault);
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal((3, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(before, All(s, r));
        }


        // === A period changed again ===

        [Fact]
        public async Task A_period_changed_again_after_the_copy_and_before_the_switch_deletes_the_copy_and_moves_the_rows_to_the_newest_class()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);

            Crash("move-copied");
            CrashedPass(MoveAt);
            await RestartAsync(crash: true);

            // P52 -> P5 copied, not switched; then P5 -> P1.
            await KeepAsync(s, KeepP1);
            Assert.Null(Pass(MoveAt.AddMinutes(1)).Fault);

            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal((3, 0, 0), (Bytes(s, 1), Bytes(s, 5), Bytes(s, 52)));
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal(before, All(s, r));
        }

        [Fact]
        public async Task A_period_changed_back_after_the_switch_finishes_the_delete_of_the_source_and_moves_the_rows_back()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);

            Crash("move-switched");
            CrashedPass(MoveAt);
            await RestartAsync(crash: true);

            // P52 -> P5 switched, the source not deleted; then back to P52.
            await KeepAsync(s, KeepP52);
            Assert.Null(Pass(MoveAt.AddMinutes(1)).Fault);

            Assert.Equal(new TablePlacement(52, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal((0, 3), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveLeftoversCleaned);
            Assert.Equal(before, All(s, r));
        }


        // === What a step never takes ===

        [Fact]
        public async Task A_step_stops_before_an_hour_that_did_not_verify_and_resumes_after_it_is_merged()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(1, "s", "r");

            // Hour 1 does not verify: it stays on its blocks.
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(61), 1.5, 1)]), ("r", [(Hour0.AddMinutes(62), 1.6, 1)]))));
            _server.Job.AfterDecode = (sensor, batch) =>
            {
                if (sensor == s.Id)
                    batch.Columns[1].DoubleValues[0] += 0.5;
            };
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            _now = Hour0.AddMinutes(130);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(121), 2.0, 2)]), ("r", [(Hour0.AddMinutes(122), 2.1, 2)]))));
            Pass(Hour0.AddHours(3).AddMinutes(3));
            var before = All(s, r);

            await KeepAsync(s, KeepP5);
            Assert.Null(Pass(MoveAt).Fault);

            // Only hour 0 moved: hour 1 is not committed, and nothing at or after it is in a step.
            Assert.Equal(new TablePlacement(5, Hour0.Ticks, [new TablePlacementSegment(Hour0.AddHours(1).Ticks, Hour0.AddHours(3).Ticks, 52)]),
                _server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal((1, 1), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(before, All(s, r));

            // The stall is visible: the sensor-week held at hour 1 counts as blocked, and stays so in the next passes.
            var stats = _server.Batches.MergeStatistics;
            for (int i = 1; i <= 2; i++)
            {
                Assert.Equal(0, Pass(MoveAt.AddMinutes(i)).MoveSteps);
                Assert.Equal((1L, 1L, 0L), (stats.MoveStepsPending, stats.MoveStepsBlocked, stats.LagHours));
            }

            // After a restart hour 1 is merged into the class that holds it, and the move goes on.
            await RestartAsync(crash: false);
            Assert.Null(Pass(MoveAt.AddMinutes(10)).Fault);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
            Assert.Equal((0, 0), (_server.Batches.MergeStatistics.MoveStepsPending, _server.Batches.MergeStatistics.MoveStepsBlocked));
            Assert.Equal((3, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(before, All(s, r));
        }

        [Fact]
        public async Task A_copy_into_a_database_retention_dropped_with_the_clock_set_back_is_refused_and_the_rows_stay_in_the_source()
        {
            var s = await RegisterAsync("s", keep: KeepP5);
            await RegisterAsync("r", keep: KeepP1);
            PutHours(1, "s", "r");
            var rows = Rows(s);

            Assert.Equal(1, Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc)).WeeksDropped);
            Assert.True(_server.Batches.IsWeekDropped(_product, new TableBatchWeek(WeekStart, 1)));

            // s shortened to P1 with the clock set back before P1 has passed for the week: its target database is gone.
            await KeepAsync(s, KeepP1);
            var back = new DateTime(WeekStart + Week + TimeSpan.TicksPerDay, DateTimeKind.Utc);
            var report = Pass(back);

            Assert.Null(report.Fault);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveStepsRefused);
            Assert.Equal(5, _server.Batches.GetPlacement(_product, s.Id).ClassOf(Hour0.Ticks));
            Assert.Equal(1, Bytes(s, 5));
            Assert.Contains(new TableBatchWeek(WeekStart, 5), _server.Batches.GetWeeks(_product));
            Assert.Equal(rows, Rows(s));

            // Known before the copy: no record was written. Not tried again by the next passes (counted once), and shown in
            // the gauge of steps refused now.
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveStepsRefusedNow);
            Pass(back.AddMinutes(1));
            Pass(back.AddMinutes(2));
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveStepsRefused);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveStepsRefusedNow);

            // A change of the period makes another step: the sensor kept for 52 weeks moves its rows there.
            await KeepAsync(s, KeepP52);
            Assert.Null(Pass(back.AddMinutes(3)).Fault);
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveStepsRefusedNow);
            Assert.Equal((1, 0), (Bytes(s, 52), Bytes(s, 5)));
            Assert.Equal(rows, Rows(s));
        }

        [Fact]
        public async Task A_copy_the_store_refuses_is_counted_once_left_as_it_is_and_not_tried_again_until_the_step_changes()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);

            // Hour 1's mark is gone from the source: the copy of the week is refused (TableBatchRefusedException), after the
            // record was written (the job cannot know it without the copy's reading).
            var week52 = _server.Batches.WeekPath(_product, WeekStart, 52);
            await RestartAsync(crash: false, beforeOpen: _ =>
            {
                using var db = new global::LevelDB.DB(week52, new global::LevelDB.Options());
                db.Delete(TableBatchDatabase.HourKey(Hour0.AddHours(1).Ticks, TableBatchDatabase.TagMark), new global::LevelDB.WriteOptions { Sync = true });
            });

            var stats = _server.Batches.MergeStatistics;
            var report = Pass(MoveAt);
            Assert.Null(report.Fault);
            Assert.Equal((1, 0, 1), (stats.MoveStepsRefused, stats.MoveFaults, stats.MoveStepsRefusedNow));
            Assert.Equal(52, _server.Batches.GetPlacement(_product, s.Id).ClassOf(Hour0.Ticks));
            Assert.Equal(before, All(s, r));

            // The next passes: the record is cleared by the recovery (it deleted nothing), the step is not tried again (no
            // record written, nothing read, nothing counted again).
            var recorded = 0;
            _server.Job.Checkpoint = p =>
            {
                if (p == "move-recorded")
                    recorded++;
            };
            Pass(MoveAt.AddMinutes(1));
            Pass(MoveAt.AddMinutes(2));
            _server.Job.Checkpoint = null;
            Assert.Equal(0, recorded);
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Equal((1, 0, 1), (stats.MoveStepsRefused, stats.MoveFaults, stats.MoveStepsRefusedNow));
            Assert.Equal(before, All(s, r));
        }

        [Fact]
        public async Task A_refused_step_is_not_tried_again_after_a_pass_that_skipped_its_product()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);

            var week52 = _server.Batches.WeekPath(_product, WeekStart, 52);
            await RestartAsync(crash: false, beforeOpen: _ =>
            {
                using var db = new global::LevelDB.DB(week52, new global::LevelDB.Options());
                db.Delete(TableBatchDatabase.HourKey(Hour0.AddHours(1).Ticks, TableBatchDatabase.TagMark), new global::LevelDB.WriteOptions { Sync = true });
            });

            var stats = _server.Batches.MergeStatistics;
            Pass(MoveAt);
            Assert.Equal((1, 1), (stats.MoveStepsRefused, stats.MoveStepsRefusedNow));

            // The next pass skips the product: its recovery cannot clear the record the refused step left.
            _server.Batches.Checkpoint = p =>
            {
                if (p == "move-writing")
                    throw new IOException("the disk refused the write");
            };
            Pass(MoveAt.AddMinutes(1));
            _server.Batches.Checkpoint = null;
            Assert.Equal(1, stats.MoveFaults);

            // The product is back: the refused step is still remembered, so it is not tried again.
            var recorded = 0;
            _server.Job.Checkpoint = p =>
            {
                if (p == "move-recorded")
                    recorded++;
            };
            Pass(MoveAt.AddMinutes(2));
            Pass(MoveAt.AddMinutes(3));
            _server.Job.Checkpoint = null;

            Assert.Equal(0, recorded);
            Assert.Equal((1, 1), (stats.MoveStepsRefused, stats.MoveStepsRefusedNow));
            Assert.Equal(before, All(s, r));
        }

        [Fact]
        public async Task With_too_little_free_disk_no_copy_step_runs_while_expired_steps_do_and_the_next_pass_with_disk_copies()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var e = await RegisterAsync("e", keep: TimeIntervalModel.None);
            PutHours(1, "s", "e");
            var rows = Rows(s);

            await RestartAsync(crash: false, options: new TableBlockOptions { MinFreeDiskMb = 1 });
            _server.Job.FreeBytes = () => 0;

            await KeepAsync(s, KeepP5);
            await KeepAsync(e, KeepP1);
            var at = new DateTime(WeekStart + 3 * Week, DateTimeKind.Utc);
            var report = Pass(at);

            Assert.Null(report.Fault);
            Assert.Equal(1, report.MoveExpiredRows);
            Assert.Empty(Rows(e));
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveStepsRefused);
            Assert.Equal(52, _server.Batches.GetPlacement(_product, s.Id).ClassOf(Hour0.Ticks));
            Assert.Equal(rows, Rows(s));

            _server.Job.FreeBytes = () => long.MaxValue / 4;
            Assert.Null(Pass(at.AddMinutes(1)).Fault);
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
            Assert.Equal((1, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(rows, Rows(s));
        }

        [Fact]
        public async Task A_moved_hour_merged_again_after_its_record_was_lost_holds_one_copy_of_every_row()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);
            Assert.Equal(1, Pass(MoveAt).MoveSteps);

            // Hours lost; hour 0 is not rebuilt (its mark in P52, a class its record names, is gone); its blocks are kept.
            var week52 = _server.Batches.WeekPath(_product, WeekStart, 52);
            await RestartAsync(crash: false, beforeOpen: settings =>
            {
                Directory.Delete(Path.Combine(settings.PathToTableBlocks, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName), recursive: true);
                using var db = new global::LevelDB.DB(week52, new global::LevelDB.Options());
                db.Delete(TableBatchDatabase.HourKey(Hour0.Ticks, TableBatchDatabase.TagMark), new global::LevelDB.WriteOptions { Sync = true });
            });

            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            Assert.Equal(before, All(s, r));

            // The merge writes hour 0 again, into the class that holds it for each sensor now: every row once.
            Assert.Equal(1, Pass(MoveAt.AddMinutes(1)).HoursMerged);
            Assert.Equal(before, All(s, r));
            Assert.Equal((3, 0, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));
        }


        // === Reads during a step ===

        [Fact]
        public async Task A_chart_of_another_sensor_never_plans_again_for_a_step_and_a_chart_of_the_moved_sensor_plans_again_once()
        {
            var (s, r) = await CopySetupAsync();
            var plans = new List<int>();
            var replans = new List<long>();
            TableMergePassReport report = null;
            _server.Service.BeforeChartReplan = replans.Add;
            _server.Service.AfterChartPlan = plan =>
            {
                plans.Add(plan);
                if (plan == 1)
                    report = Pass(MoveAt);
            };

            var chart = _server.Service.Reduce(r.Id, From, To, [1, 2], 100).Value;
            Assert.Equal(1, report.MoveSteps);
            Assert.Equal([1], plans);
            Assert.Empty(replans);
            Assert.Equal((3L, false), (chart.RowCount, chart.IsIncomplete));

            // The moved sensor, its period back: its chart across the step plans again once and answers every row.
            await KeepAsync(s, KeepP52);
            plans.Clear();
            chart = _server.Service.Reduce(s.Id, From, To, [1, 2], 100).Value;
            Assert.Equal(1, report.MoveSteps);
            Assert.Equal([1, 2], plans);
            Assert.Single(replans);
            Assert.Equal((3L, false), (chart.RowCount, chart.IsIncomplete));
            Assert.Equal((0, 3), (Bytes(s, 5), Bytes(s, 52)));
        }

        [Fact]
        public async Task A_full_read_planned_before_the_switch_and_read_after_it_is_complete_because_the_delete_of_the_source_waits_for_it()
        {
            var (s, _) = await CopySetupAsync();
            var expected = Rows(s);
            var gate = _server.Service.HourGate;
            Task<TableMergePassReport> pass = null;
            var stale = new List<bool>();
            TablePlacement whileWaiting = null;

            // The read holds the read side of the gate from before its plan to its end. The step runs meanwhile on the
            // job's thread up to the delete of the source, which waits for the gate.
            _server.Service.PlanTaken = isStale =>
            {
                stale.Add(isStale);
                if (stale.Count != 1)
                    return;

                _now = MoveAt;
                pass = Task.Run(() => _server.Job.RunPass(MoveAt));
                Assert.True(SpinWait.SpinUntil(() => gate.WaitingDeletes == 1 || pass.IsCompleted, TimeSpan.FromMinutes(2)));
                Assert.False(pass.IsCompleted);
                whileWaiting = _server.Batches.GetPlacement(_product, s.Id);
            };

            var rows = _server.Service.Read(s.Id, From, To).Value;
            _server.Service.PlanTaken = null;

            // Switched before the read went on: it read the class it planned, which still held the rows.
            Assert.Empty(whileWaiting.Segments);
            Assert.Equal([false], stale);
            Assert.Equal(expected, Snapshot(rows));
            Assert.Equal(0, rows.BatchesFailed);

            var report = await pass;
            Assert.Null(report.Fault);
            Assert.Equal(1, report.MoveSteps);
            Assert.Equal((3, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(expected, Rows(s));
        }

        [Fact]
        public async Task Rows_moved_into_a_class_the_hours_record_names_are_read_and_charted_outside_that_classs_own_range()
        {
            var s = await RegisterAsync("s", keep: KeepP1);
            var r = await RegisterAsync("r", keep: KeepP5);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(1), 1.0, 1)]), ("r", [(Hour0.AddMinutes(5), 2.0, 2)]))));
            // A late block in hour 1: s's row of hour 0's time, r's row of hour 1.
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(2), 3.0, 3)]), ("r", [(Hour0.AddMinutes(65), 4.0, 4)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));
            Pass(Hour0.AddHours(2).AddMinutes(3));

            var record = _server.Batches.GetHour(_product, Hour0.AddHours(1).Ticks);
            Assert.Equal([1, 5], record.Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(Hour0.AddMinutes(65).Ticks, record.Classes[1].MinTime);

            var range = (From: Hour0, To: Hour0.AddMinutes(30));
            var before = Snapshot(_server.Service.Read(s.Id, range.From, range.To).Value);
            Assert.Equal(2, before.Count);

            await KeepAsync(s, KeepP5);
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(10)).MoveSteps);
            Assert.Equal((2, 0), (Bytes(s, 5), Bytes(s, 1)));

            var read = _server.Service.Read(s.Id, range.From, range.To).Value;
            Assert.Equal(before, Snapshot(read));
            Assert.Equal(0, read.BatchesFailed);

            var chart = _server.Service.Reduce(s.Id, range.From, range.To, [1, 2], 10).Value;
            Assert.Equal((2L, 0), (chart.RowCount, chart.BatchesFailed));
            Assert.Equal(2, Rows(r).Count);
        }


        // === The budget ===

        [Fact]
        public async Task Steps_go_oldest_week_first_then_by_product_one_a_pass_with_a_budget_of_one_step()
        {
            // Three products; the one with the highest id has its rows a week earlier. The order is the oldest week first
            // (the work goes one source database at a time, which bounds the tombstones held before its compaction), then the
            // product's id within a week; not a turn per product.
            var products = new List<Guid> { _product };
            for (int i = 0; i < 2; i++)
                products.Add((await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id);
            products.Sort();
            var older = products[^1];

            var sensors = new Dictionary<Guid, BaseSensorModel>();
            foreach (var product in products)
                sensors[product] = await RegisterAsync("s", product, keep: KeepP52);

            DateTime HourOf(Guid product) => product == older ? Hour0.AddDays(-7) : Hour0;

            // The older hour first (the clock never goes back), then this hour of the two others, each hour merged by one pass.
            foreach (var group in products.GroupBy(HourOf).OrderBy(g => g.Key))
            {
                _now = group.Key.AddMinutes(10);
                foreach (var product in group)
                    AssertStored(_server.Service.Put(product, _producer, ++_producerSequence, Block(("s", [(group.Key.AddMinutes(1), 1.0, 1)]))));
                Assert.Equal(1, Pass(group.Key.AddHours(1).AddMinutes(3)).HoursMerged);
            }

            foreach (var sensor in sensors.Values)
                await KeepAsync(sensor, KeepP5);

            _server.Job.MoveStepsPerPass = 1;
            bool Moved(Guid product) => _server.Batches.GetPlacement(product, sensors[product].Id).ClassOf(HourOf(product).Ticks) == 5;

            // Pass 1: the older week, although its product has the highest id.
            Assert.Equal(1, Pass(Hour0.AddHours(2)).MoveSteps);
            Assert.Equal([false, false, true], products.Select(Moved));

            // Then this week's steps by product id (the older product's step of this week holds no rows).
            for (int i = 0; i < products.Count; i++)
            {
                Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(1 + i)).MoveSteps);
                Assert.Equal(_server.Batches.GetPlacement(products[i], sensors[products[i]].Id).Segments.Count == 0, true);
                for (int j = i + 1; j < products.Count; j++)
                    Assert.NotEmpty(_server.Batches.GetPlacement(products[j], sensors[products[j]].Id).Segments);
            }

            Assert.Equal(0, Pass(Hour0.AddHours(2).AddMinutes(10)).MoveSteps);
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveStepsPending);
            foreach (var (product, sensor) in sensors)
                Assert.Single(_server.Service.Read(sensor.Id, HourOf(product).AddHours(-1), HourOf(product).AddHours(1)).Value.Time);
        }

        [Fact]
        public async Task A_products_copy_steps_wait_while_its_own_merge_lags_and_another_products_do_not()
        {
            // Product A (s, a copy step; e, an expired step) and product B (b, a copy step).
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            var s = await RegisterAsync("s", keep: KeepP52);
            var e = await RegisterAsync("e", keep: TimeIntervalModel.None);
            var b = await RegisterAsync("b", other, keep: KeepP52);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(1), 1.0, 1)]), ("e", [(Hour0.AddMinutes(2), 2.0, 2)]))));
            AssertStored(_server.Service.Put(other, _producer, ++_producerSequence, Block(("b", [(Hour0.AddMinutes(1), 3.0, 3)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            await KeepAsync(s, KeepP5);
            await KeepAsync(e, KeepP1);
            await KeepAsync(b, KeepP5);

            // An hour of product A due for the merge keeps faulting: A's merge lags, B's does not.
            var at = new DateTime(WeekStart + 3 * Week, DateTimeKind.Utc);
            _now = at.AddMinutes(-50);
            AssertStored(Put(Block(("s", [(at.AddMinutes(-55), 9.0, 9)]))));
            _server.Job.Checkpoint = p =>
            {
                if (p == "encoded")
                    throw new IOException("the merge faults");
            };

            var report = Pass(at.AddMinutes(3));
            var stats = _server.Batches.MergeStatistics;
            Assert.Equal(1, stats.LagHours);
            Assert.Equal(1, report.MoveExpiredRows);
            Assert.Empty(Rows(e));
            Assert.Equal(52, _server.Batches.GetPlacement(_product, s.Id).ClassOf(Hour0.Ticks));
            Assert.Empty(_server.Batches.GetPlacement(other, b.Id).Segments);
            Assert.True(stats.MoveStepsBlocked > 0);
            Assert.Equal(stats.MoveStepsBlocked, stats.MoveStepsPending);

            // Caught up: A's copy steps run, the late hour too (merged into the class that holds it, then moved).
            _server.Job.Checkpoint = null;
            report = Pass(at.AddMinutes(4));
            Assert.Equal(1, report.HoursMerged);
            Assert.Equal(0, stats.LagHours);
            Assert.Empty(_server.Batches.GetPlacement(_product, s.Id).Segments);
            Assert.Equal((1, 0), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal((0, 0), (stats.MoveStepsPending, stats.MoveStepsBlocked));
        }

        [Fact]
        public async Task The_steps_of_a_product_whose_recovery_faults_every_pass_count_as_pending_and_blocked()
        {
            var (s, r) = await CopySetupAsync();
            var before = All(s, r);

            CrashedPass(MoveAt, "move-recorded");
            await RestartAsync(crash: true);

            // The record cannot be cleared: the recovery faults in every pass, and no step of the product runs.
            _server.Batches.Checkpoint = p =>
            {
                if (p == "move-writing")
                    throw new IOException("the disk refused the write");
            };

            var stats = _server.Batches.MergeStatistics;
            for (int i = 1; i <= 2; i++)
            {
                Assert.Null(Pass(MoveAt.AddMinutes(i)).Fault);
                Assert.Equal((i, 1L, 1L), ((int)stats.MoveFaults, stats.MoveStepsPending, stats.MoveStepsBlocked));
            }

            _server.Batches.Checkpoint = null;
            Pass(MoveAt.AddMinutes(3));
            Assert.Equal((0, 0), (stats.MoveStepsPending, stats.MoveStepsBlocked));
            Assert.Equal(before, All(s, r));
        }

        // === Compaction ===

        [Fact]
        public async Task A_database_a_tenth_of_which_is_deleted_is_compacted_once_but_never_while_it_is_the_newest_week()
        {
            var (s, r, next) = await CompactionSetupAsync();
            var week = new TableBatchWeek(WeekStart, 52);
            var rows = All(s, r);

            // Moved in the newest week: listed, not compacted.
            Assert.Contains(week, _server.Batches.GetCompactList(_product));
            Assert.True(_server.Batches.GetDeletedShare(_product, week).Share >= TableMergeJob.CompactShare);
            Assert.Equal(0, _server.Batches.MergeStatistics.DatabasesCompacted);

            // An hour of the next week committed: the week is not the newest any more, and it is compacted in that pass.
            var report = CommitNextWeek(next);
            Assert.Equal(1, report.Compactions);
            Assert.Equal(1, _server.Batches.MergeStatistics.DatabasesCompacted);
            Assert.Empty(_server.Batches.GetCompactList(_product));
            Assert.Equal(0, _server.Batches.GetDeletedShare(_product, week).DeletedBytes);
            Assert.Equal(rows, All(s, r));

            Assert.Equal(0, Pass(next.AddHours(1).AddMinutes(10)).Compactions);
        }

        [Fact]
        public async Task A_compaction_whose_share_does_not_go_down_is_not_made_again_every_pass()
        {
            var (_, _, next) = await CompactionSetupAsync();
            var week = new TableBatchWeek(WeekStart, 52);

            int compactions = 0;
            _server.Batches.Checkpoint = p =>
            {
                if (p == "compacting")
                    compactions++;
                if (p == "compacted" && compactions == 1)
                    throw new IOException("the counter could not be written");
            };

            var report = CommitNextWeek(next);
            Assert.Null(report.Fault);
            Assert.Equal(1, compactions);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveFaults);
            Assert.Contains(week, _server.Batches.GetCompactList(_product));

            Assert.Equal(0, Pass(next.AddHours(1).AddMinutes(10)).Compactions);
            Assert.Equal(0, Pass(next.AddHours(2)).Compactions);
            Assert.Equal(1, compactions);
            _server.Batches.Checkpoint = null;
        }

        [Fact]
        public async Task An_entry_of_the_compaction_list_whose_database_is_gone_is_removed()
        {
            await RegisterAsync("s", keep: KeepP52);
            PutHours(1, "s");

            _server.Batches.AddToCompactList(_product, new TableBatchWeek(WeekStart - Week, 9));
            Assert.Single(_server.Batches.GetCompactList(_product));

            Assert.Equal(0, Pass(Hour0.AddHours(2)).Compactions);
            Assert.Empty(_server.Batches.GetCompactList(_product));
        }


        // === Helpers ===

        // s and r kept for 52 weeks, a row of each in hours 0 to 2 (merged), then s shortened to five weeks.
        private async Task<(BaseSensorModel S, BaseSensorModel R)> CopySetupAsync()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(3, "s", "r");
            await KeepAsync(s, KeepP5);
            return (s, r);
        }

        // s and r kept for ever, a row of each in hours 0 and 1, then s shortened to one week: three weeks later the week
        // has expired in s's new class.
        private async Task<(BaseSensorModel S, BaseSensorModel R, DateTime At)> ExpiredSetupAsync()
        {
            var s = await RegisterAsync("s", keep: TimeIntervalModel.None);
            var r = await RegisterAsync("r", keep: TimeIntervalModel.None);
            PutHours(2, "s", "r");
            await KeepAsync(s, KeepP1);
            return (s, r, new DateTime(WeekStart + 3 * Week, DateTimeKind.Utc));
        }

        // s with 500 rows an hour and r with one, kept for 52 weeks, hours 0 to 2; s shortened to five weeks and moved in
        // the same (newest) week. Returns the first hour of the next week.
        private async Task<(BaseSensorModel S, BaseSensorModel R, DateTime Next)> CompactionSetupAsync()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            var random = new Random(1850);

            for (int h = 0; h < 3; h++)
            {
                var hour = Hour0.AddHours(h);
                _now = hour.AddMinutes(10);
                var many = Enumerable.Range(0, 500).Select(i => (hour.AddSeconds(i), (double?)random.NextDouble(), (long?)random.Next())).ToArray();
                AssertStored(Put(Block(("s", many), ("r", [(hour.AddMinutes(1), 1.0, 1)]))));
                Pass(hour.AddHours(1).AddMinutes(3));
            }

            await KeepAsync(s, KeepP5);
            var report = Pass(MoveAt);
            Assert.Equal((1, 0), (report.MoveSteps, report.Compactions));

            return (s, r, new DateTime(WeekStart + Week, DateTimeKind.Utc).AddHours(9));
        }

        private TableMergePassReport CommitNextWeek(DateTime next)
        {
            _now = next.AddMinutes(10);
            AssertStored(Put(Block(("r", [(next.AddMinutes(1), 5.0, 5)]))));
            var report = Pass(next.AddHours(1).AddMinutes(3));
            Assert.Equal(1, report.HoursMerged);
            return report;
        }

        // One block an hour for hours 0 to hours - 1, a row of each named sensor, each hour merged by a pass.
        private void PutHours(int hours, params string[] names)
        {
            for (int h = 0; h < hours; h++)
            {
                var hour = Hour0.AddHours(h);
                _now = hour.AddMinutes(10);
                AssertStored(Put(Block(names.Select((name, i) => (name, new[] { (hour.AddMinutes(1 + i), (double?)(h * 10 + i), (long?)h) })).ToArray())));
                Assert.Equal(1, Pass(hour.AddHours(1).AddMinutes(3)).HoursMerged);
            }
        }

        // A pass in which the move faulted (a crash at the point, when given): counted in Move faults, not the pass's fault.
        private void CrashedPass(DateTime at, string point = null)
        {
            if (point is not null)
                Crash(point);

            var faults = _server.Batches.MergeStatistics.MoveFaults;
            var report = Pass(at);
            Assert.Null(report.Fault);
            Assert.True(_server.Batches.MergeStatistics.MoveFaults > faults);
            Assert.True(_crashThrown, $"the pass faulted before its crash point {_crashPoint} was reached");
        }

        private async Task KeepAsync(BaseSensorModel sensor, TimeIntervalModel keep) =>
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = keep })).IsOk);

        // The sensor's batches in the database of the week of Hour0 and the class.
        private long Bytes(BaseSensorModel sensor, int periodWeeks) =>
            _server.Batches.GetSensorHourBytes(_product, sensor.Id, new TableBatchWeek(WeekStart, periodWeeks), WeekStart, WeekStart + Week).Batches;

        private string MovePath() =>
            Path.Combine(_server.Database.DatabaseSettings.PathToTableBlocks, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.MoveName);
    }
}
