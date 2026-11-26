using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBatches;
using HSMServer.Core.Model;
using HSMServer.Core.TableSensors;
using HSMTableBlocks;
using Xunit;

namespace HSMServer.Core.Tests.TableSensors
{
    /// <summary>
    /// The removal of table sensors and root products (#1850 part 4d): a removed sensor's keys in every class, another
    /// sensor's keys byte for byte, a crash after every durable write, a mark of something still in the tree, a removal
    /// during a move, a product's blocks and folder, a new sensor on the path, an orphan placement, a record that does not
    /// read, a sensor removed at start without its product. Tests 7 and 21 to 26 of the design, adapted. No timing: the
    /// clock is the pass's parameter, the budget <see cref="TableMergeJob.MoveStepsPerPass"/>.
    /// </summary>
    public partial class TableMergeJobTests
    {
        // A pass in the hour after the last merged hour of the removal setups.
        private static readonly DateTime RemoveAt = Hour0.AddHours(3).AddMinutes(10);


        // === A sensor (design tests 21 and 25) ===

        [Fact]
        public async Task A_removed_sensor_loses_its_keys_in_every_class_within_the_budget_and_another_sensors_keys_stay_byte_for_byte()
        {
            var (s, r) = await RemovalSetupAsync();
            var rows = Rows(r);
            var before = await DumpWeeksAsync();

            await _server.Cache.RemoveSensorAsync(s.Id);
            Assert.Equal([new TableRemoval(_product, s.Id, false, Confirmed: true)], _server.Batches.GetRemovals());

            // One write a pass: the P5 database first (the week, then the class), then P52, the placement and the mark.
            ReleaseMoves(steps: 1);
            var first = Pass(RemoveAt);
            Assert.Null(first.Fault);
            Assert.Equal((0, 0), (first.RemovalsDone, first.MoveSteps));
            Assert.Equal((0, 2), (Bytes(s, 5), Bytes(s, 52)));
            Assert.NotNull(_server.Batches.GetPlacement(_product, s.Id));

            var second = Pass(RemoveAt.AddMinutes(1));
            Assert.Equal(1, second.RemovalsDone);
            Assert.Equal((0, 0, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));
            Assert.Null(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(1, _server.Batches.MergeStatistics.RemovalsDone);
            Assert.Equal(0, _server.Batches.MergeStatistics.OrphanPlacements);
            Assert.Equal(rows, Rows(r));

            // A third pass writes nothing.
            var generations = (_server.Batches.GetGeneration(_product, s.Id), _server.Batches.GetGeneration(_product, r.Id), _server.Batches.GetGeneration(_product));
            Assert.Equal(0, Pass(RemoveAt.AddMinutes(2)).RemovalsDone);
            Assert.Equal(generations, (_server.Batches.GetGeneration(_product, s.Id), _server.Batches.GetGeneration(_product, r.Id), _server.Batches.GetGeneration(_product)));

            // Every key that went is s's (an index or moved key with its id, a batch of the hour region one of its own index
            // records named); every other key is there with the same bytes; the marks stay; only the counter is new.
            var after = await DumpWeeksAsync();
            Assert.Equal(before.Keys.Order(), after.Keys.Order());
            var id = TableBatchDatabase.IndexKey(0, s.Id, 0, 0)[9..25];
            var deleted = 0;

            foreach (var (database, keys) in before)
            {
                var ordinals = keys.Keys.Select(Convert.FromHexString).Where(k => k.Length == 37 && k[8] == 0x00 && k.AsSpan(9, 16).SequenceEqual(id))
                                   .Select(k => (Convert.ToHexString(k[..8]), Convert.ToHexString(k[33..37]))).ToHashSet();

                foreach (var (hex, value) in keys)
                {
                    var key = Convert.FromHexString(hex);
                    var ofS = (key.Length == 37 && key[8] == 0x00 && key.AsSpan(9, 16).SequenceEqual(id)) ||
                              (key[0] == 0xFF && key.Length > 25 && key.AsSpan(9, 16).SequenceEqual(id)) ||
                              (key.Length == 13 && key[8] == 0x01 && ordinals.Contains((Convert.ToHexString(key[..8]), Convert.ToHexString(key[9..13]))));

                    if (ofS)
                    {
                        Assert.False(after[database].ContainsKey(hex), $"{database}: a key of s is left");
                        deleted++;
                    }
                    else
                        Assert.Equal(value, after[database][hex]);
                }

                Assert.All(after[database].Keys, hex => Assert.True(keys.ContainsKey(hex) || hex == Convert.ToHexString(TableBatchDatabase.DeletedKey), $"{database}: a new key {hex}"));
            }

            // Three batches of s (two in P52, one in P5): an index record and a batch each.
            Assert.Equal(6, deleted);
        }

        [Theory]
        [InlineData("removal-deleted", 1)]
        [InlineData("removal-deleted", 2)]
        [InlineData("removal-unplaced", 1)]
        [InlineData("removals-writing", 1)]
        [InlineData("removal-cleared", 1)]
        public async Task A_crash_after_any_durable_write_of_a_sensors_removal_is_completed_by_the_next_pass_which_then_writes_nothing(string point, int occurrence)
        {
            var (s, r) = await RemovalSetupAsync();
            var rows = Rows(r);
            await _server.Cache.RemoveSensorAsync(s.Id);
            ReleaseMoves();

            CrashAt(point, occurrence);
            CrashedPass(RemoveAt);
            await RestartAsync(crash: true);

            // Nothing of r changed; what of s is gone stays gone, the mark stays until its own write.
            Assert.Equal(rows, Rows(r));
            Assert.Equal((point, occurrence) switch { ("removal-deleted", 1) => (0L, 2L), _ => (0L, 0L) }, (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(point is "removal-deleted", _server.Batches.GetPlacement(_product, s.Id) is not null);
            Assert.Equal(point is not "removal-cleared", _server.Batches.GetRemovals().Count == 1);

            var rerun = Pass(RemoveAt.AddMinutes(1));
            Assert.Null(rerun.Fault);
            Assert.Equal(point is "removal-cleared" ? 0 : 1, rerun.RemovalsDone);
            Assert.Equal((0, 0, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));
            Assert.Null(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(rows, Rows(r));

            var generations = (_server.Batches.GetGeneration(_product, s.Id), _server.Batches.GetGeneration(_product));
            var again = Pass(RemoveAt.AddMinutes(2));
            Assert.Equal((0, null), (again.RemovalsDone, again.Fault));
            Assert.Equal(generations, (_server.Batches.GetGeneration(_product, s.Id), _server.Batches.GetGeneration(_product)));
        }

        [Fact]
        public async Task A_removed_sensors_table_is_refused_and_a_new_sensor_registered_on_its_path_before_the_pass_keeps_its_rows_and_placement()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(2, "s", "r");

            await _server.Cache.RemoveSensorAsync(s.Id);

            // A collector still sending the removed sensor's table: refused, nothing stored.
            _now = Hour0.AddHours(2).AddMinutes(5);
            var refused = Put(Block(("s", [(Hour0.AddHours(2).AddMinutes(1), 9.0, 9)])));
            Assert.Equal(TableBlockPutOutcome.AllRefused, refused.Outcome);
            Assert.Equal(TableRefusalReason.UnknownSensor, Assert.Single(refused.Refusals).Reason);

            // A new sensor on the path, with rows merged before the pass that removes the old one.
            var renewed = await RegisterAsync("s", keep: KeepP52);
            _now = Hour0.AddHours(2).AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddHours(2).AddMinutes(2), 7.0, 7)]), ("r", [(Hour0.AddHours(2).AddMinutes(3), 8.0, 8)]))));

            var report = Pass(Hour0.AddHours(3).AddMinutes(3));
            Assert.Null(report.Fault);
            Assert.Equal((1, 1), (report.HoursMerged, report.RemovalsDone));

            Assert.Equal(new List<(long, double?, long?)> { (Hour0.AddHours(2).AddMinutes(2).Ticks, 7.0, 7L) }, Rows(renewed));
            Assert.NotNull(_server.Batches.GetPlacement(_product, renewed.Id));
            Assert.Equal((0, 1, 3), (Bytes(s, 52), Bytes(renewed, 52), Bytes(r, 52)));
            Assert.Null(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Empty(_server.Batches.GetRemovals());
        }


        // === A mark of what is still in the tree (design test 22) ===

        [Fact]
        public async Task A_mark_of_a_sensor_or_product_still_in_the_tree_deletes_nothing_waits_in_its_run_and_is_cleared_after_a_restart()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            PutHours(2, "s");
            var rows = Rows(s);
            var placement = _server.Batches.GetPlacement(_product, s.Id);

            // Marked in this run while still in the tree: a removal the tree is making. Nothing deleted, the mark waits, and
            // the product is merged as before.
            _server.Batches.MarkRemoved([(_product, s.Id)], [_product]);
            _now = Hour0.AddHours(2).AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddHours(2).AddMinutes(1), 5.0, 5)]))));
            var report = Pass(Hour0.AddHours(3).AddMinutes(3));
            Assert.Equal((1, 0), (report.HoursMerged, report.RemovalsDone));
            Assert.Equal(2, _server.Batches.GetRemovals().Count);
            rows = Rows(s);
            Assert.Equal(3, rows.Count);

            // The run stopped before the entities left the tree: the marks are of an earlier run, and are cleared.
            await RestartAsync(crash: true);
            Assert.All(_server.Batches.GetRemovals(), m => Assert.True(m.Earlier));

            report = Pass(Hour0.AddHours(3).AddMinutes(20));
            Assert.Equal((0, null), (report.RemovalsDone, report.Fault));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(rows, Rows(s));
            Assert.Equal(3, Bytes(s, 52));
            Assert.Equal(placement, _server.Batches.GetPlacement(_product, s.Id));
            Assert.Contains(_product, _server.Batches.GetProducts());
        }

        [Fact]
        public async Task A_mark_of_this_run_is_acted_on_only_once_the_tree_confirms_its_entity_left_the_database()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(2, "s", "r");

            // s out of the tree with its mark written and not confirmed: what a pass sees between the tree's removal of the
            // sensor from its cache and of its entity from the database (a crash there brings the sensor back at the restart).
            // Made here by a removal whose mark could not be written, then the mark written alone.
            _server.Batches.Checkpoint = p => { if (p == "removals-writing") throw new IOException("disk full"); };
            await _server.Cache.RemoveSensorAsync(s.Id);
            _server.Batches.Checkpoint = null;
            _server.Batches.MarkRemoved([(_product, s.Id)], []);
            Assert.False(_server.Batches.GetRemovals().Single().Ready);

            var report = Pass(RemoveAt);
            Assert.Equal((0, null), (report.RemovalsDone, report.Fault));
            Assert.Equal(2, Bytes(s, 52));
            Assert.NotNull(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal(0, _server.Batches.MergeStatistics.OrphanPlacements);

            _server.Batches.ConfirmRemoved([s.Id], []);
            Assert.Equal(1, Pass(RemoveAt.AddMinutes(1)).RemovalsDone);
            Assert.Equal((0, 2), (Bytes(s, 52), Bytes(r, 52)));
            Assert.Empty(_server.Batches.GetRemovals());
        }


        // === A removal during a move (design test 23) ===

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_sensor_removed_while_a_step_of_its_move_is_in_flight_loses_both_copies_and_the_record(bool recoveryFaults)
        {
            var (s, r) = await CopySetupAsync();
            var rows = Rows(r);

            // The step stopped after its copy: the rows in P52 and in P5, the record in flight.
            Crash("move-copied");
            CrashedPass(MoveAt);
            await RestartAsync(crash: true);
            Assert.Equal((3, 3), (Bytes(s, 5), Bytes(s, 52)));
            Assert.Equal(s.Id, _server.Batches.GetMoveInFlight(_product).SensorId);

            await _server.Cache.RemoveSensorAsync(s.Id);

            // With the recovery faulting after it deleted the copy, its record is left for the removal to clear.
            if (recoveryFaults)
                Crash("move-recovery-deleted");

            var report = Pass(MoveAt.AddMinutes(1));
            _server.Job.Checkpoint = null;
            _server.Batches.Checkpoint = null;

            Assert.Null(report.Fault);
            Assert.Equal(1, report.RemovalsDone);
            Assert.Equal((0, 0, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));
            Assert.Null(_server.Batches.GetMoveInFlight(_product));
            Assert.Null(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(rows, Rows(r));
        }


        // === A root product (design tests 7 and 24) ===

        [Fact]
        public async Task A_removed_products_blocks_and_folder_go_while_another_products_blocks_of_the_same_hours_stay_and_the_merge_never_writes_it_again()
        {
            var (victim, r) = await ProductRemovalSetupAsync();

            // A block of the victim in the open hour (stored before its removal reached the tree).
            _now = Hour0.AddHours(2).AddMinutes(10);
            AssertStored(PutTo(victim, Block(("v", [(Hour0.AddHours(2).AddMinutes(1), 3.0, 3)]))));

            var folder = ProductFolder(victim);
            var kept = _server.Blocks.GetBlocks(_product).Select(e => e.Sequence).ToList();
            Assert.True(Directory.Exists(folder));

            await _server.Cache.RemoveProductAsync(victim);
            var marks = _server.Batches.GetRemovals();
            Assert.Equal(2, marks.Count);
            Assert.All(marks, m => Assert.Equal((victim, true), (m.ProductId, m.Ready)));
            Assert.Single(marks, m => m.SensorId is null);

            // Hour 1 is merged for r alone (the victim's not merged), the victim's blocks of hours 0 and 1 are deleted and
            // r's kept; the open hour 2 waits, so the product's folder and its mark stay for now.
            var first = Pass(Hour0.AddHours(2).AddMinutes(20));
            Assert.Null(first.Fault);
            Assert.Equal((1, 0), (first.HoursMerged, first.RemovalsDone));
            Assert.False(_server.Batches.IsHourMerged(victim, Hour0.AddHours(1).Ticks));
            Assert.Equal([Hour0.AddHours(2).Ticks], _server.Blocks.GetBlocks(victim).Select(e => e.ArrivalHourTicks));
            Assert.Equal(kept, _server.Blocks.GetBlocks(_product).Select(e => e.Sequence));
            Assert.True(Directory.Exists(folder));
            Assert.Equal(2, _server.Batches.GetRemovals().Count);
            Assert.Equal(0, _server.Batches.MergeStatistics.LagHours);

            // Hour 2 closed: its block goes, then the folder, then the mark.
            var second = Pass(Hour0.AddHours(3).AddMinutes(10));
            Assert.Equal((1, null), (second.RemovalsDone, second.Fault));
            Assert.Empty(_server.Blocks.GetBlocks(victim));
            Assert.False(Directory.Exists(folder));
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(folder), $"{victim:N}*"));
            Assert.DoesNotContain(victim, _server.Batches.GetProducts());
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(kept, _server.Blocks.GetBlocks(_product).Select(e => e.Sequence));
            Assert.Equal(new List<(long, double?, long?)> { (Hour0.AddMinutes(2).Ticks, 2.0, 2L), (Hour0.AddHours(1).AddMinutes(2).Ticks, 4.0, 4L) }, Rows(r));

            // A put for it is refused; later passes never write its folder again.
            Assert.Equal(TableBlockPutOutcome.AllRefused, PutTo(victim, Block(("v", [(Hour0.AddHours(3).AddMinutes(11), 5.0, 5)]))).Outcome);
            Assert.Null(Pass(Hour0.AddHours(4).AddMinutes(10)).Fault);
            Assert.False(Directory.Exists(folder));
        }

        [Theory]
        [InlineData("removal-blocks-deleted")]
        [InlineData("product-renamed")]
        [InlineData("removal-product-removed")]
        [InlineData("removal-cleared")]
        public async Task A_crash_after_any_durable_write_of_a_products_removal_is_completed_by_the_next_pass_which_then_writes_nothing(string point)
        {
            var (victim, r) = await ProductRemovalSetupAsync();
            var folder = ProductFolder(victim);
            var kept = _server.Blocks.GetBlocks(_product).Select(e => e.Sequence).ToList();
            await _server.Cache.RemoveProductAsync(victim);

            CrashAt(point, 1);
            CrashedPass(RemoveAt);
            await RestartAsync(crash: true);
            Assert.Equal(point is not "removal-cleared", _server.Batches.GetRemovals().Count > 0);

            var rerun = Pass(RemoveAt.AddMinutes(1));
            Assert.Null(rerun.Fault);
            Assert.Equal(point is "removal-cleared" ? 0 : 1, rerun.RemovalsDone);
            Assert.Empty(_server.Blocks.GetBlocks(victim));
            Assert.False(Directory.Exists(folder));
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(folder), $"{victim:N}*"));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(kept, _server.Blocks.GetBlocks(_product).Select(e => e.Sequence));
            var rows = Rows(r);

            var again = Pass(RemoveAt.AddMinutes(2));
            Assert.Equal((0, null), (again.RemovalsDone, again.Fault));
            Assert.False(Directory.Exists(folder));
            Assert.Equal(rows, Rows(r));
        }


        // === What is never deleted by itself (design test 26, the record that does not read) ===

        [Fact]
        public async Task A_placement_without_its_sensor_and_without_a_mark_is_kept_counted_and_still_holds_its_week_database()
        {
            var s = await RegisterAsync("s", keep: KeepP1);
            var r = await RegisterAsync("r", keep: KeepP1);
            PutHours(1, "s", "r");

            // s kept for a year, its hour still in P1 (the move held): its placement holds the P1 database by its target.
            HoldMoves();
            await KeepAsync(s, KeepP52);
            Pass(Hour0.AddHours(1).AddMinutes(10));
            Assert.Single(_server.Batches.GetPlacement(_product, s.Id).Segments);

            // The record of the removal cannot be written: the removal goes on, the rows stay.
            _server.Batches.Checkpoint = p => { if (p == "removals-writing") throw new IOException("disk full"); };
            await _server.Cache.RemoveSensorAsync(s.Id);
            _server.Batches.Checkpoint = null;
            Assert.Null(_server.Cache.GetSensor(s.Id));
            Assert.Empty(_server.Batches.GetRemovals());

            ReleaseMoves();
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(20)).Fault);
            Assert.Equal(1, _server.Batches.MergeStatistics.OrphanPlacements);
            Assert.Equal(1, Bytes(s, 1));

            // P1 has passed for the week: the database stays, held by the orphan's stored target; nothing of it is deleted.
            var report = Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc));
            Assert.Equal(0, report.WeeksDropped);
            Assert.Contains(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Equal(1, Bytes(s, 1));
            Assert.NotNull(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal(1, _server.Batches.MergeStatistics.OrphanPlacements);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveWeeksPending);
        }

        [Fact]
        public async Task A_record_of_removals_that_cannot_be_read_deletes_nothing_is_counted_and_the_removal_runs_once_it_reads()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(2, "s", "r");
            await _server.Cache.RemoveSensorAsync(s.Id);
            await RestartAsync(crash: true);

            _server.Batches.ReadRemovedLines = _ => throw new IOException("the file is held by another process");
            var faults = _server.Batches.MergeStatistics.MoveFaults;

            // Nothing is removed, the merge goes on, the fault is counted (and logged once an hour), s's placement counts as
            // an orphan while no mark is known.
            _now = Hour0.AddHours(2).AddMinutes(10);
            AssertStored(Put(Block(("r", [(Hour0.AddHours(2).AddMinutes(1), 5.0, 5)]))));
            var report = Pass(Hour0.AddHours(3).AddMinutes(3));
            Assert.Null(report.Fault);
            Assert.Equal((1, 0), (report.HoursMerged, report.RemovalsDone));
            Assert.True(_server.Batches.MergeStatistics.MoveFaults > faults);
            Assert.Equal(2, Bytes(s, 52));
            Assert.NotNull(_server.Batches.GetPlacement(_product, s.Id));
            Assert.Equal(1, _server.Batches.MergeStatistics.OrphanPlacements);

            // It reads again: the removal is made.
            _server.Batches.ReadRemovedLines = File.ReadAllLines;
            report = Pass(Hour0.AddHours(3).AddMinutes(4));
            Assert.Equal(1, report.RemovalsDone);
            Assert.Equal((0, 3), (Bytes(s, 52), Bytes(r, 52)));
            Assert.Equal(0, _server.Batches.MergeStatistics.OrphanPlacements);
        }


        // === The removal at start (a sensor whose parent is missing) ===

        [Fact]
        public async Task A_table_sensor_removed_at_start_for_want_of_its_parent_is_marked_without_its_product_and_its_rows_are_found_and_deleted()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(2, "s", "r");
            var rows = Rows(r);

            // Another node holding a table sensor, whose entity goes missing: at the next start its sensor has no parent.
            var lost = await RegisterAsync("lost/q", keep: KeepP52);
            _now = Hour0.AddHours(2).AddMinutes(10);
            AssertStored(Put(Block(("lost/q", [(Hour0.AddHours(2).AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(3).AddMinutes(3)).HoursMerged);
            Assert.Equal(1, Bytes(lost, 52));

            _server.Database.RemoveProduct(lost.Parent.Id.ToString());
            await RestartAsync(crash: false);

            Assert.Null(_server.Cache.GetSensor(lost.Id));
            Assert.Equal([new TableRemoval(Guid.Empty, lost.Id, false, Confirmed: true)], _server.Batches.GetRemovals());

            var report = Pass(RemoveAt);
            Assert.Equal(1, report.RemovalsDone);
            Assert.Equal((0, 2, 2), (Bytes(lost, 52), Bytes(s, 52), Bytes(r, 52)));
            Assert.Null(_server.Batches.GetPlacement(_product, lost.Id));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(rows, Rows(r));
        }


        // === The tree's side, and marks that change during a pass (the review of the removal) ===

        [Fact]
        public async Task The_tree_records_a_removal_while_the_sensor_is_still_in_the_tree_and_its_entity_in_the_database()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            PutHours(1, "s");

            (bool InTree, bool InDatabase)? atMark = null;
            _server.Batches.Checkpoint = p =>
            {
                if (p == "removals-writing")
                    atMark = (_server.Cache.GetSensor(s.Id) is not null, _server.Database.EntityExists(s.Id));
            };

            await _server.Cache.RemoveSensorAsync(s.Id);
            _server.Batches.Checkpoint = null;

            Assert.Equal((true, true), atMark);
            Assert.False(_server.Database.EntityExists(s.Id));
            Assert.True(Assert.Single(_server.Batches.GetRemovals()).Ready);
        }

        [Fact]
        public async Task A_removed_node_marks_every_table_sensor_below_it_in_one_write_and_their_rows_go_while_its_siblings_stay()
        {
            var a = await RegisterAsync("node/a", keep: KeepP52);
            var b = await RegisterAsync("node/deep/b", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(1, "node/a", "node/deep/b", "r");
            var rows = Rows(r);

            var writes = 0;
            _server.Batches.Checkpoint = p => { if (p == "removals-writing") writes++; };
            await _server.Cache.RemoveProductAsync(a.Parent.Id);
            _server.Batches.Checkpoint = null;

            Assert.Equal(1, writes);
            Assert.Equal(new[] { a.Id, b.Id }.Order(), _server.Batches.GetRemovals().Select(m => m.SensorId.Value).Order());
            Assert.All(_server.Batches.GetRemovals(), m => Assert.Equal((_product, true), (m.ProductId, m.Ready)));

            Assert.Equal(2, Pass(RemoveAt).RemovalsDone);
            Assert.Equal((0, 0, 1), (Bytes(a, 52), Bytes(b, 52), Bytes(r, 52)));
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.Equal(rows, Rows(r));
            Assert.Contains(_product, _server.Batches.GetProducts());
        }

        [Fact]
        public async Task A_pass_that_completes_many_sensor_removals_writes_the_record_of_removals_once()
        {
            const int count = 8;
            var paths = Enumerable.Range(0, count).Select(i => $"node/t{i}").ToArray();
            var sensors = new List<BaseSensorModel>();
            foreach (var path in paths)
                sensors.Add(await RegisterAsync(path, keep: KeepP52));
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(1, [.. paths, "r"]);
            var rows = Rows(r);

            await _server.Cache.RemoveProductAsync(sensors[0].Parent.Id);
            Assert.Equal(count, _server.Batches.GetRemovals().Count);

            // One write of the record for the pass, not one per sensor (#1922).
            var writes = 0;
            _server.Batches.Checkpoint = p => { if (p == "removals-writing") writes++; };
            var report = Pass(RemoveAt);
            _server.Batches.Checkpoint = null;

            Assert.Equal((count, null), (report.RemovalsDone, report.Fault));
            Assert.Equal(1, writes);
            Assert.Empty(_server.Batches.GetRemovals());
            Assert.All(sensors, s => Assert.Equal(0, Bytes(s, 52)));
            Assert.Equal(rows, Rows(r));
        }

        [Fact]
        public async Task A_mark_replaced_during_the_pass_by_one_not_confirmed_deletes_nothing_on_the_mark_the_pass_read()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            PutHours(2, "s");

            // A mark of an earlier run while s is in the tree (a crash between the mark and the removal of the entity).
            _server.Batches.MarkRemoved([(_product, s.Id)], []);
            await RestartAsync(crash: true);
            Assert.True(Assert.Single(_server.Batches.GetRemovals()).Earlier);

            // During the pass, after it read the marks: s leaves the tree and is marked again, not confirmed (here its first
            // mark write fails, so the mark is written alone: the window between the tree's cache and its database).
            _server.Job.Checkpoint = p =>
            {
                if (p != "reconciled")
                    return;

                _server.Job.Checkpoint = null;
                _server.Batches.Checkpoint = q => { if (q == "removals-writing") throw new IOException("disk full"); };
                Task.Run(() => _server.Cache.RemoveSensorAsync(s.Id)).GetAwaiter().GetResult();
                _server.Batches.Checkpoint = null;
                _server.Batches.MarkRemoved([(_product, s.Id)], []);
            };

            var report = Pass(RemoveAt);
            Assert.Equal((0, null), (report.RemovalsDone, report.Fault));
            Assert.Equal(2, Bytes(s, 52));
            Assert.False(Assert.Single(_server.Batches.GetRemovals()).Ready);

            _server.Batches.ConfirmRemoved([s.Id], []);
            Assert.Equal(1, Pass(RemoveAt.AddMinutes(1)).RemovalsDone);
            Assert.Equal(0, Bytes(s, 52));
        }

        [Fact]
        public async Task A_sensor_removed_during_a_pass_is_not_counted_as_an_orphan_and_the_next_pass_removes_it()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            PutHours(1, "s");

            _server.Job.Checkpoint = p =>
            {
                if (p != "reconciled")
                    return;

                _server.Job.Checkpoint = null;
                Task.Run(() => _server.Cache.RemoveSensorAsync(s.Id)).GetAwaiter().GetResult();
            };

            Assert.Equal(0, Pass(RemoveAt).RemovalsDone);
            Assert.Equal(0, _server.Batches.MergeStatistics.OrphanPlacements);
            Assert.Equal(1, Bytes(s, 52));

            Assert.Equal(1, Pass(RemoveAt.AddMinutes(1)).RemovalsDone);
            Assert.Equal(0, Bytes(s, 52));
        }

        [Fact]
        public async Task A_crash_between_a_removals_delete_and_its_note_still_puts_a_tenth_deleted_database_on_the_compaction_list()
        {
            // s with 500 rows in hour 0 and r with one, in P52: s is far more than a tenth of the database.
            var s = await RegisterAsync("s", keep: KeepP52);
            await RegisterAsync("r", keep: KeepP52);
            var random = new Random(1850);
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", Enumerable.Range(0, 500).Select(i => (Hour0.AddSeconds(i), (double?)random.NextDouble(), (long?)random.Next())).ToArray()),
                ("r", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            await _server.Cache.RemoveSensorAsync(s.Id);
            CrashAt("removal-deleted", 1);
            CrashedPass(RemoveAt);
            await RestartAsync(crash: true);
            Assert.Empty(_server.Batches.GetCompactList(_product));

            Assert.Equal(1, Pass(RemoveAt.AddMinutes(1)).RemovalsDone);
            Assert.Equal(0, Bytes(s, 52));
            Assert.True(_server.Batches.GetDeletedShare(_product, new TableBatchWeek(WeekStart, 52)).Share >= TableMergeJob.CompactShare);
            Assert.Contains(new TableBatchWeek(WeekStart, 52), _server.Batches.GetCompactList(_product));
        }


        // === Helpers ===

        // s and r kept for 52 weeks, a row of each in hours 0 and 1 (merged into P52); s shortened to five weeks with the
        // move held, so hour 2 is merged into P5 for s: s has rows in two classes of the week, r in one.
        private async Task<(BaseSensorModel S, BaseSensorModel R)> RemovalSetupAsync()
        {
            var s = await RegisterAsync("s", keep: KeepP52);
            var r = await RegisterAsync("r", keep: KeepP52);
            PutHours(2, "s", "r");

            HoldMoves();
            await KeepAsync(s, KeepP5);
            Pass(Hour0.AddHours(2).AddMinutes(5));

            _now = Hour0.AddHours(2).AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddHours(2).AddMinutes(1), 20.0, 2)]), ("r", [(Hour0.AddHours(2).AddMinutes(2), 21.0, 2)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(3).AddMinutes(3)).HoursMerged);

            Assert.Equal((1, 2, 3), (Bytes(s, 5), Bytes(s, 52), Bytes(r, 52)));
            return (s, r);
        }

        // Another root product with a table sensor v, and r in the test's product: hour 0 holds blocks of both and is merged,
        // hour 1 holds blocks of both and is not merged yet.
        private async Task<(Guid Victim, BaseSensorModel R)> ProductRemovalSetupAsync()
        {
            var victim = (await _server.Cache.AddProductAsync($"v{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            await RegisterAsync("v", product: victim);
            var r = await RegisterAsync("r");

            for (int h = 0; h < 2; h++)
            {
                var hour = Hour0.AddHours(h);
                _now = hour.AddMinutes(10);
                AssertStored(PutTo(victim, Block(("v", [(hour.AddMinutes(1), 1.0 + 2 * h, 1 + 2 * h)]))));
                AssertStored(Put(Block(("r", [(hour.AddMinutes(2), 2.0 + 2 * h, 2 + 2 * h)]))));
            }

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.True(_server.Batches.IsHourMerged(victim, Hour0.Ticks));
            return (victim, r);
        }

        private TableBlockPutResult PutTo(Guid product, byte[] block) => _server.Service.Put(product, _producer, ++_producerSequence, block);

        private string ProductFolder(Guid product) =>
            Path.Combine(_server.Database.DatabaseSettings.PathToTableBlocks, TableBatchDatabase.FolderName, product.ToString("N"));

        // The steps (and removals) let go again, a given number a pass or by the time budget.
        private void ReleaseMoves(int? steps = null)
        {
            _movesHeld = false;
            _server.Job.MoveStepsPerPass = steps;
        }

        // A crash at the given occurrence of a point of the job or of the batch store.
        private void CrashAt(string point, int occurrence)
        {
            var seen = 0;
            void Throw(string p)
            {
                if (p == point && ++seen == occurrence)
                    throw new IOException($"crash at {p} ({occurrence})");
            }

            _server.Job.Checkpoint = Throw;
            _server.Batches.Checkpoint = Throw;
        }

        // Every key and value of every week database of the test's product, by database name, read with the store closed (a
        // clean restart).
        private async Task<Dictionary<string, Dictionary<string, byte[]>>> DumpWeeksAsync()
        {
            Dictionary<string, Dictionary<string, byte[]>> dump = null;

            await RestartAsync(crash: false, beforeOpen: settings =>
            {
                dump = [];
                var folder = Path.Combine(settings.PathToTableBlocks, TableBatchDatabase.FolderName, _product.ToString("N"));

                foreach (var path in Directory.GetDirectories(folder).Where(d => TableBatchDatabase.TryParseWeek(Path.GetFileName(d), out _)))
                {
                    var keys = new Dictionary<string, byte[]>();
                    using (var db = new LevelDB.DB(path, new LevelDB.Options()))
                    using (var iterator = db.CreateIterator())
                        for (iterator.SeekToFirst(); iterator.IsValid; iterator.Next())
                            keys[Convert.ToHexString(iterator.Key())] = iterator.Value();

                    dump[Path.GetFileName(path)] = keys;
                }
            });

            return dump;
        }
    }
}
