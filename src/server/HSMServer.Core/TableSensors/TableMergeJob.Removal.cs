using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.TableBatches;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// The removal of table sensors and root products (#1850 part 4d, ADR-0021 point 12: a removed table sensor's rows are
    /// deleted at once, as an ordinary sensor's values are, and a removed product's never-merged blocks go with it).
    /// <para>
    /// The tree records a removal in the batch store (<see cref="ITableBatchStore.MarkRemoved"/>) before it removes the
    /// entity. The pass reads the record once (<see cref="ReadRemovals"/>); its part of the move, after the recovery of the
    /// steps in flight and before the steps, within their budget, deletes what the marks name, and only for a sensor or
    /// product that is gone from the tree:
    /// <list type="bullet">
    /// <item>A table sensor: its keys in every week database of every product that holds a placement of it, from the week
    /// of the placement's first hour on, every class, one synced write per database that holds anything of it, under the
    /// write side of the read gate (<see cref="ITableBatchStore.DeleteSensorHours"/>: never a mark, never another sensor's
    /// key, the deleted-bytes counter in the same write); then the product's move in flight if it is the sensor's; then
    /// its placement; then the mark.</item>
    /// <item>A root product: its blocks, by closed arrival hour, the other products' blocks of the hour kept; then the batch
    /// store removes it (<see cref="ITableBatchStore.RemoveProduct"/>: closed, its folder renamed and deleted); then its
    /// mark and the marks of its sensors. The merge leaves a removed product alone from the pass that reads its mark on.</item>
    /// </list>
    /// Each write is idempotent: a crash after any of them is completed by a later pass, which writes nothing more once it
    /// is complete. A mark for a sensor or product that is in the tree deletes nothing: one written by an earlier run (a
    /// crash between the mark and the removal of the entity) is cleared with a warning; one written in this run is a
    /// removal the tree is still making, and waits. A mark of this run is acted on only once the tree confirmed that the
    /// entity left its database (<see cref="TableRemoval.Ready"/>): before, a crash would bring the sensor back without
    /// its rows.
    /// </para>
    /// </summary>
    public sealed partial class TableMergeJob
    {
        // The most orphan placements one warning names.
        private const int MaxOrphansLogged = 50;

        // Root products marked removed and gone from the tree, as the pass read them: the merge leaves them alone.
        private HashSet<Guid> _removedProducts = [];

        // The day (of the pass's clock) the orphan placements were last logged.
        private long _orphansLoggedDay = long.MinValue;

        // What a removal deleted in earlier passes of this run, for the line logged when it completes.
        private readonly Dictionary<Guid, (long Batches, long Rows)> _removalProgress = [];


        // The removals recorded, once a pass; null when the record cannot be read (a fault, counted and logged once an hour:
        // nothing is removed, no product is left out of the merge, and every placement of a sensor not in the tree is an
        // orphan until the record reads again).
        private IReadOnlyList<TableRemoval> ReadRemovals(DateTime utcNow)
        {
            try
            {
                var removals = Batches.GetRemovals();
                _removedProducts = removals.Where(m => m.SensorId is null && !_cache.TryGetProduct(m.ProductId, out _)).Select(m => m.ProductId).ToHashSet();
                return removals;
            }
            catch (Exception ex)
            {
                _removedProducts = [];
                MoveFault(Guid.Empty, utcNow, ex, "the record of removed table sensors and products could not be read, so nothing is removed in this pass");
                return null;
            }
        }

        // Never throws: a fault is counted (Move faults), logged once per product and hour of the pass's clock, puts the
        // product in `faulted` (no move steps of it in this pass) and leaves its mark for the next pass. Removals do not skip a
        // product that faulted earlier in the pass (its recovery, its placements): deleting a gone sensor's keys depends on
        // neither, and a recovery that faulted leaves its record for the removal to clear. A product whose store keeps failing
        // is therefore tried once per mark that names it.
        private void RunRemovals(IReadOnlyList<TableRemoval> removals, IReadOnlyList<Guid> products, MoveRun run, HashSet<Guid> faulted, DateTime utcNow, CancellationToken token)
        {
            if (removals.Count == 0)
                return;

            // Root products first: their sensors go with their folder.
            var going = new HashSet<Guid>();

            foreach (var mark in removals.Where(m => m.SensorId is null))
            {
                if (token.IsCancellationRequested || !run.CanStart)
                    return;

                if (_cache.TryGetProduct(mark.ProductId, out _))
                {
                    InTree(mark, utcNow, $"the product {mark.ProductId}");
                    continue;
                }

                // Out of the tree, its entity not yet out of the database (a removal the tree is making): it waits. Decided
                // on the mark as it is now, read after the tree check: the tree marks before it removes, so a removal made
                // again since the pass read the marks (an earlier run's mark replaced by an unconfirmed one) is seen.
                if (!IsLive(mark) || !mark.Ready)
                    continue;

                going.Add(mark.ProductId);

                try
                {
                    RemoveProductRows(mark, removals, run, utcNow, token);
                }
                catch (Exception ex)
                {
                    faulted.Add(mark.ProductId);
                    MoveFault(mark.ProductId, utcNow, ex, $"the removal of product {mark.ProductId} did not complete");
                }
            }

            // The sensors whose deletes completed in this pass: their marks are cleared together at the end (one write of the
            // record for the pass, not one per sensor), each only after its own deletes.
            var done = new List<(TableRemoval Mark, long Batches, long Rows)>();

            try
            {
                RemoveSensors(removals, products, going, done, run, faulted, utcNow, token);
            }
            finally
            {
                ClearSensorMarks(done, run, utcNow);
            }
        }

        private void RemoveSensors(IReadOnlyList<TableRemoval> removals, IReadOnlyList<Guid> products, HashSet<Guid> going,
            List<(TableRemoval Mark, long Batches, long Rows)> done, MoveRun run, HashSet<Guid> faulted, DateTime utcNow, CancellationToken token)
        {
            HashSet<Guid> tree = null;

            foreach (var mark in removals.Where(m => m.SensorId is not null))
            {
                if (token.IsCancellationRequested || !run.CanStart)
                    return;

                var id = mark.SensorId.Value;

                // Cleared with its product's mark, once the product's folder is gone.
                if (going.Contains(mark.ProductId))
                    continue;

                tree ??= products.SelectMany(p => TableSensorsOf(p)).Select(s => s.Id).ToHashSet();
                if (tree.Contains(id) || _cache.GetSensor(id) is not null)
                {
                    InTree(mark, utcNow, $"the table sensor {id}");
                    continue;
                }

                if (!IsLive(mark) || !mark.Ready)
                    continue;

                try
                {
                    if (RemoveSensorRows(mark, products, going, run, token, out var batches, out var rows))
                        done.Add((mark, batches, rows));
                }
                catch (Exception ex)
                {
                    var holders = Holders(products, id);
                    faulted.UnionWith(holders);
                    MoveFault(holders.FirstOrDefault(), utcNow, ex, $"the removal of table sensor {id} (product {mark.ProductId}) did not complete");
                }
            }
        }

        // Whether the mark is still the store's mark of its sensor or product, unchanged since the pass read it: a keyed
        // lookup (#1922), not a copy of every mark.
        private bool IsLive(TableRemoval mark) => Batches.FindRemoval(mark) == mark;

        // Clears the marks of the sensors whose deletes completed in this pass in one write of the record, then counts and
        // logs each one cleared. A mark not cleared (made again since the pass read it, or the write failed) stays for a later
        // pass, which deletes nothing more for it and clears it then.
        private void ClearSensorMarks(List<(TableRemoval Mark, long Batches, long Rows)> done, MoveRun run, DateTime utcNow)
        {
            if (done.Count == 0)
                return;

            // ClearRemovals clears only the same mark: one the store still holds for the sensor was made again since.
            HashSet<TableRemoval> kept;
            try
            {
                var all = Batches.ClearRemovals(done.Select(d => d.Mark).ToList());
                Checkpoint?.Invoke("removal-sensors-cleared");
                kept = all ? [] : done.Where(d => Batches.FindRemoval(d.Mark) is not null).Select(d => d.Mark).ToHashSet();
            }
            catch (Exception ex)
            {
                foreach (var (mark, batches, rows) in done)
                    _removalProgress[mark.SensorId.Value] = (batches, rows);

                MoveFault(Guid.Empty, utcNow, ex, $"the record of {done.Count} completed table sensor removals could not be cleared");
                return;
            }

            foreach (var (mark, batches, rows) in done)
            {
                var id = mark.SensorId.Value;

                if (kept.Contains(mark))
                {
                    _removalProgress[id] = (batches, rows);
                    continue;
                }

                _removalProgress.Remove(id);
                Stats.Add(ref Stats.RemovalsDone);
                run.Removals++;

                _logger.Info($"Table removal of sensor {id} (product {(mark.ProductId == Guid.Empty ? "not known" : mark.ProductId.ToString())}) done: " +
                             $"{batches} batches ({rows} rows) deleted, its placement and its record cleared");
            }
        }

        // The products that hold a placement of a sensor, as far as they can be read now.
        private List<Guid> Holders(IReadOnlyList<Guid> products, Guid sensor)
        {
            var holders = new List<Guid>();

            foreach (var product in products)
                try
                {
                    if (Batches.GetPlacement(product, sensor) is not null)
                        holders.Add(product);
                }
                catch (Exception)
                {
                    holders.Add(product);
                }

            return holders;
        }

        // A mark of a sensor or product that is in the tree: nothing is deleted. One written by an earlier run (a crash between
        // the mark and the removal of the entity, which then stayed) is cleared with a warning; one written in this run is a
        // removal the tree is still making: it waits for the next pass.
        private void InTree(TableRemoval mark, DateTime utcNow, string what)
        {
            if (!mark.Earlier)
                return;

            try
            {
                Batches.ClearRemovals([mark]);
                _logger.Warn($"Table removal: {what} is in the tree, so its removal recorded by an earlier run (which stopped before it removed it) " +
                             "is cleared and nothing of it is deleted");
            }
            catch (Exception ex)
            {
                MoveFault(Guid.Empty, utcNow, ex, $"the record of the removal of {what}, which is in the tree, could not be cleared");
            }
        }

        // A table sensor gone from the tree: every key of it, in every week database of every product that holds a placement
        // of it (not only the classes the placement names: what a lost in-flight record left is found too), from the week of
        // the placement's first hour on (it has no rows before it); then that product's move in flight if it is the sensor's;
        // then the placement; the mark is cleared by the caller (ClearSensorMarks, once for the pass). Answers whether its
        // deletes completed, with what it deleted in this run (false: the budget or the token stopped it; a later pass goes on,
        // the databases already done hold nothing of it and write nothing again).
        private bool RemoveSensorRows(TableRemoval mark, IReadOnlyList<Guid> products, HashSet<Guid> going, MoveRun run, CancellationToken token,
            out long batches, out long rows)
        {
            var id = mark.SensorId.Value;
            (batches, rows) = _removalProgress.GetValueOrDefault(id);

            foreach (var product in products)
            {
                if (going.Contains(product) || Batches.GetPlacement(product, id) is not { } placement)
                    continue;

                long from = StorageWeek.GetStartTicks(placement.FirstHour);

                foreach (var week in Batches.GetWeeks(product).Where(w => w.WeekStartTicks >= from))
                {
                    if (token.IsCancellationRequested || !run.CanStart)
                    {
                        _removalProgress[id] = (batches, rows);
                        return false;
                    }

                    TableBatchSensorHours gone;
                    using (_gate.Delete())
                        gone = Batches.DeleteSensorHours(product, id, week, week.WeekStartTicks, week.WeekStartTicks + WeekTicks);

                    // Every database visited, whatever this pass deleted there: a crash between an earlier delete and its note
                    // would otherwise leave a database a tenth of which is deleted off the compaction list for good.
                    if (gone.Batches > 0)
                    {
                        run.Steps++;
                        run.RemovalSteps++;
                        batches += gone.Batches;
                        rows += gone.Rows;
                        Checkpoint?.Invoke("removal-deleted");
                    }

                    NoteDeleted(product, week);
                }

                if (Batches.GetMoveInFlight(product) is { } record && record.SensorId == id)
                {
                    Batches.SetMoveInFlight(product, null);
                    Checkpoint?.Invoke("removal-move-cleared");
                }

                Batches.SetPlacement(product, id, null);
                Checkpoint?.Invoke("removal-unplaced");
            }

            return true;
        }

        // A root product gone from the tree: its blocks, hour by hour, only of hours the merge takes as closed (closed here,
        // so the listing of the hour is the whole hour: a block of another product cannot be appended to it after the
        // listing and be deleted with it); an hour not closed yet waits for a later pass. Then the batch store removes the
        // product whole, then its mark and its sensors' marks. Answers whether it completed.
        private bool RemoveProductRows(TableRemoval mark, IReadOnlyList<TableRemoval> removals, MoveRun run, DateTime utcNow, CancellationToken token)
        {
            var product = mark.ProductId;
            var (blocks, _) = _removalProgress.GetValueOrDefault(product);
            var pending = false;

            foreach (var hour in Blocks.GetBlocks(product).Select(e => e.ArrivalHourTicks).Distinct().OrderBy(h => h).ToList())
            {
                if (hour + TimeSpan.TicksPerHour + _options.MergeGrace.Ticks > utcNow.Ticks)
                {
                    pending = true;
                    break;
                }

                if (token.IsCancellationRequested || !run.CanStart)
                {
                    _removalProgress[product] = (blocks, 0);
                    return false;
                }

                Blocks.CloseHour(hour);

                var keep = new HashSet<long>();
                foreach (var other in Blocks.GetProducts())
                    if (other != product)
                        keep.UnionWith(Blocks.GetBlocks(other).Where(e => e.ArrivalHourTicks == hour).Select(e => e.Sequence));

                using (_gate.Delete())
                    blocks += Blocks.DeleteMergedHour(hour, keep, []);

                run.Steps++;
                run.RemovalSteps++;
                Checkpoint?.Invoke("removal-blocks-deleted");
            }

            if (pending || token.IsCancellationRequested)
            {
                _removalProgress[product] = (blocks, 0);
                return false;
            }

            // The store waits for the reads that hold a database of the product (a read of one of its sensors in flight sees
            // the weeks after the one it holds as absent: the sensor is gone from the tree).
            Batches.RemoveProduct(product);
            Checkpoint?.Invoke("removal-product-removed");

            // Its marks as they are now (sensors marked with it since the pass read them included).
            if (!Batches.ClearRemovals(Batches.GetRemovals().Where(m => m.ProductId == product && (m.SensorId is not null || m == mark)).ToList()))
                return false;

            Checkpoint?.Invoke("removal-product-cleared");

            _removalProgress.Remove(product);
            Stats.Add(ref Stats.RemovalsDone);
            run.Removals++;

            _logger.Info($"Table removal of product {product} done: {blocks} blocks never merged or within their delay deleted, its batches deleted whole, " +
                         "its record cleared");
            return true;
        }
    }
}
