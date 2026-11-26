using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.TableBatches;
using HSMServer.Core.Model;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// The move between period classes (#1850 part 4c, ADR-0021 point 12): after a table sensor's period changed, its rows
    /// merged under the old period (the segments of its placement) are brought into the databases of its target class, in
    /// the background, step by step, by the merge pass after the merge and before retention.
    /// <para>
    /// The invariant: for every sensor and committed hour, the database of the class its placement names
    /// (<see cref="TablePlacement.ClassOf"/>) holds all the sensor's rows of that hour (or they have expired by the one
    /// rule); another class may hold a copy (being made, or a source not yet deleted) that is never read. The placement is
    /// the only authority, for reads, for retention and for the move's own recovery; the product's in-flight record
    /// (<see cref="ITableBatchStore.GetMoveInFlight"/>) only says which other class may hold a copy.
    /// </para>
    /// <para>
    /// A step is one sensor, a run of committed hours of one segment inside one week, at most <see cref="MoveStepBytes"/>
    /// of batches. A copy step: the in-flight record (C1), the copy into the region of moved batches of the target's
    /// database of the week (C2), the switch of the placement (C3), the delete of the sensor's rows of those hours in every
    /// other database of the week under the write side of the read gate (C4), the record cleared (C5). An expired step
    /// (the target class has passed for the week, by the one rule applied to the placement the switch writes): the delete
    /// in every database of the week under the gate (E1), then the switch (E2); nothing copied.
    /// </para>
    /// </summary>
    public sealed partial class TableMergeJob
    {
        /// <summary>The most bytes of batches one copy step copies (at least one hour).</summary>
        internal const long MoveStepBytes = 64L * 1024 * 1024;

        /// <summary>The share of a week database deleted at which it is compacted (the owner's rule, and the only one).</summary>
        internal const double CompactShare = 0.1;

        private const long WeekTicks = 7 * TimeSpan.TicksPerDay;

        /// <summary>
        /// Steps per pass, in place of the time budget (<see cref="TableBlockOptions.MoveSecondsPerPass"/>), so that no test
        /// depends on timing. Null: the time budget. The byte budget applies either way. 0 holds every step off (the
        /// recovery of a step in flight and the compactions still run): for tests of what holds while rows wait for the move.
        /// </summary>
        internal int? MoveStepsPerPass { get; set; }

        /// <summary>The most bytes of a copy step: <see cref="MoveStepBytes"/>. Test seam (a step cut into hours without 64 MB of rows).</summary>
        internal long StepBytesLimit { get; set; } = MoveStepBytes;

        // The hour of the pass's clock in which a product's move fault, and its refused step, were last logged.
        private readonly Dictionary<Guid, long> _moveFaultHour = [];
        private readonly Dictionary<Guid, long> _moveRefusedHour = [];

        // The sensors whose move was logged as started and not yet as ended (this process).
        private readonly HashSet<(Guid Product, Guid Sensor)> _movesStarted = [];

        // Week databases whose deleted share stayed at or above the threshold after a compaction (the counter not lowered:
        // a fault after the compaction): the deleted bytes then. Not compacted again in this process until a tenth of the
        // database more has been deleted since, so a counter that does not go down never makes every pass compact.
        private readonly Dictionary<(Guid Product, TableBatchWeek Week), long> _notShrinking = [];


        private sealed record MoveStep(Guid Product, TableSensorModel Sensor, TableBatchWeek From, int ToClass, long FirstHour, long EndHour, bool Expired);

        private enum StepOutcome { Done, Skipped, Refused, DiskShort, Stopped }

        /// <summary>What the move did in one pass and what is left of its budget.</summary>
        private sealed class MoveRun(int? stepLimit, long milliseconds, long bytes)
        {
            public readonly Stopwatch Watch = Stopwatch.StartNew();
            public readonly long ByteLimit = bytes;

            public int Steps;
            public long Bytes;
            public long MovedBytes;
            public long ExpiredRows;
            public int Compactions;

            // At least one step a pass when there is work; then while the budget lasts. A step limit of 0 (tests) holds the
            // steps off.
            public bool CanStart => stepLimit != 0 && (Steps == 0 || (Bytes < ByteLimit && (stepLimit is int n ? Steps < n : Watch.ElapsedMilliseconds < milliseconds)));
        }


        // === The pass's part ===

        // In flight first (every product), then the expired steps, then (only while the merge has no lag) the copy steps,
        // store-wide, oldest week first, then product, then sensor. Never throws: a fault of a product is counted, logged
        // once per product and hour of the pass's clock, and stops that product's steps for this pass; its record stays.
        private MoveRun RunMoves(DateTime utcNow, Unreconciled unreconciled, bool mergeCaughtUp, CancellationToken token, ref string fault)
        {
            var run = new MoveRun(MoveStepsPerPass, _options.MoveSecondsPerPass * 1000L, _options.MoveMegabytesPerPass * 1024L * 1024);

            if (unreconciled.All || token.IsCancellationRequested)
                return run;

            IReadOnlyList<Guid> products;
            try
            {
                products = Batches.GetProducts();
            }
            catch (Exception ex)
            {
                MoveFault(Guid.Empty, utcNow, ex, "the products of the batch store could not be listed", ref fault);
                return run;
            }

            var faulted = new HashSet<Guid>();

            // 1. The step each product has in flight.
            foreach (var product in products)
            {
                if (token.IsCancellationRequested)
                    return run;

                try
                {
                    RecoverMove(product);
                }
                catch (Exception ex)
                {
                    faulted.Add(product);
                    MoveFault(product, utcNow, ex, $"the move in flight of product {product} was not recovered", ref fault);
                }
            }

            // 2. The removal of table sensors and products (#1850 part 4d, the next pull request) goes here: after the
            //    recovery of the moves in flight, before the steps.

            // 3. The steps.
            var steps = SelectSteps(products, unreconciled, faulted, utcNow, ref fault);
            int done = 0;
            var diskShort = false;

            foreach (var step in steps.Where(s => s.Expired).Concat(mergeCaughtUp ? steps.Where(s => !s.Expired) : []))
            {
                if (faulted.Contains(step.Product) || (diskShort && !step.Expired))
                    continue;

                if (token.IsCancellationRequested || !run.CanStart)
                    break;

                try
                {
                    var outcome = step.Expired ? ExpireStep(step, run, utcNow, token) : CopyStep(step, run, utcNow, token);

                    if (outcome == StepOutcome.Stopped)
                        break;

                    if (outcome == StepOutcome.Done)
                        done++;
                    else if (outcome == StepOutcome.DiskShort)
                        diskShort = true;

                    if (outcome is StepOutcome.Done or StepOutcome.Skipped)
                        LogMoveEnd(step);
                }
                catch (Exception ex)
                {
                    faulted.Add(step.Product);
                    MoveFault(step.Product, utcNow, ex, $"a step of the move of sensor [{step.Sensor.Id}][{step.Sensor.FullPath}] " +
                                                        $"({Name(step.From)} -> P{step.ToClass}, {Hour(step.FirstHour)} to {Hour(step.EndHour)}) did not complete", ref fault);
                }
            }

            Stats.Set(ref Stats.MoveStepsPending, Math.Max(0, steps.Count - done));
            return run;
        }

        // The sensor-weeks to move: per eligible sensor (in the tree, its product reconciled in this pass, its setting's class
        // equal to its placement's target), per segment and week, the hours before the product's oldest arrival hour with
        // blocks that is not committed and before the current hour of the pass's clock: so no hour of a step can be written
        // by a run of the merge any more.
        private List<MoveStep> SelectSteps(IReadOnlyList<Guid> products, Unreconciled unreconciled, HashSet<Guid> faulted, DateTime utcNow, ref string fault)
        {
            var steps = new List<MoveStep>();
            long now = utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour;

            foreach (var product in products)
            {
                if (unreconciled.Contains(product) || faulted.Contains(product))
                    continue;

                try
                {
                    var placements = Batches.GetPlacements(product);
                    if (placements.Values.All(p => p.Segments.Count == 0))
                        continue;

                    long limit = FirstUncommittedHour(product, now);

                    foreach (var sensor in TableSensorsOf(product))
                    {
                        if (!placements.TryGetValue(sensor.Id, out var placement) || placement.Segments.Count == 0 ||
                            TablePeriodClass.Of(sensor, utcNow) != placement.Target)
                            continue;

                        foreach (var segment in placement.Segments)
                        {
                            long end = Math.Min(segment.EndHour, limit);

                            for (long first = segment.FirstHour; first < end;)
                            {
                                long week = StorageWeek.GetStartTicks(first);
                                long stop = Math.Min(end, week + WeekTicks);

                                steps.Add(new MoveStep(product, sensor, new TableBatchWeek(week, segment.PeriodWeeks), placement.Target, first, stop,
                                    TableBatchRetention.IsExpired(week, placement.Target, utcNow)));
                                first = stop;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    faulted.Add(product);
                    MoveFault(product, utcNow, ex, $"the steps of the move of product {product} could not be listed", ref fault);
                }
            }

            return steps.OrderBy(s => s.From.WeekStartTicks).ThenBy(s => s.Product).ThenBy(s => s.Sensor.Id).ThenBy(s => s.FirstHour).ToList();
        }


        // === The recovery of a step in flight ===

        // THE RULE: recovery never copies. It keeps, for every hour of the record, the class the sensor's placement names now
        // and deletes the sensor's rows of that hour in the record's other class; when the placement names neither class of
        // the record (or the sensor has none), it deletes nothing, logs an Error and clears the record. A copy is made only by
        // a step, from the class the placement names as holding the hours: a copy repeated from a class the placement does
        // not name (a stale record, a step whose source was already deleted) could replace the only copy with nothing.
        // A step left at C1 or C2 (the hours still in the source's class) loses its copy and is made again by a later step;
        // one left at C3 or C4 (the hours in the target) gets its source deleted. Nothing here depends on the record being
        // there: a record lost or unreadable is taken as none by the store, and what it named is cleaned up when a later step
        // meets it (the leftovers in a target are deleted before a copy into it, and C4 deletes in every class but the target).
        private void RecoverMove(Guid product)
        {
            var record = Batches.GetMoveInFlight(product);
            if (record is null)
                return;

            var placement = Batches.GetPlacement(product, record.SensorId);
            var hours = HoursOf(record.FirstHour, record.EndHour);
            var classes = placement is null ? null : hours.Select(placement.ClassOf).ToList();

            if (classes is null || classes.Any(c => c != record.FromClass && c != record.ToClass))
            {
                Stats.Add(ref Stats.MoveFaults);
                _logger.Error($"Table move: the step in flight of sensor {record.SensorId} of product {product} ({Name(new TableBatchWeek(record.WeekStartTicks, record.FromClass))} -> " +
                              $"P{record.ToClass}, {Hour(record.FirstHour)} to {Hour(record.EndHour)}) names neither class its placement now gives those hours " +
                              $"({(placement is null ? "no placement" : string.Join(", ", classes.Distinct().Select(c => $"P{c}")))}): nothing is deleted, the record is cleared");
                Batches.SetMoveInFlight(product, null);
                return;
            }

            long deleted = 0;

            for (int i = 0; i < hours.Count;)
            {
                int j = i;
                while (j < hours.Count && classes[j] == classes[i])
                    j++;

                var other = new TableBatchWeek(record.WeekStartTicks, classes[i] == record.FromClass ? record.ToClass : record.FromClass);
                TableBatchSensorHours gone;

                using (_gate.Delete())
                    gone = Batches.DeleteSensorHours(product, record.SensorId, other, hours[i], hours[j - 1] + TimeSpan.TicksPerHour);

                Checkpoint?.Invoke("move-recovery-deleted");

                if (gone.Batches > 0)
                    NoteDeleted(product, other);

                deleted += gone.Batches;
                i = j;
            }

            Batches.SetMoveInFlight(product, null);

            var aborted = placement.Target != record.ToClass;
            var what = $"Table move: the step in flight of sensor {record.SensorId} of product {product} ({Name(new TableBatchWeek(record.WeekStartTicks, record.FromClass))} -> " +
                       $"P{record.ToClass}, {Hour(record.FirstHour)} to {Hour(record.EndHour)}) recovered: {deleted} batches deleted in the class its placement does not name" +
                       (aborted ? $"; the period changed again (target P{placement.Target}), the step is aborted" : string.Empty);

            if (aborted || deleted > 0)
                _logger.Warn(what);
            else
                _logger.Info(what);
        }


        // === A copy step ===

        // The hours of the step, cut at MoveStepBytes; each cut is a step of the budget.
        private StepOutcome CopyStep(MoveStep step, MoveRun run, DateTime utcNow, CancellationToken token)
        {
            var outcome = StepOutcome.Skipped;

            for (long first = step.FirstHour; first < step.EndHour;)
            {
                if (token.IsCancellationRequested)
                    return StepOutcome.Stopped;

                if (first > step.FirstHour && !run.CanStart)
                    return StepOutcome.Stopped;

                long end = CutAt(step, first);
                outcome = CopyHours(step, first, end, run, utcNow, token);

                if (outcome is not (StepOutcome.Done or StepOutcome.Skipped))
                    return outcome;

                first = end;
            }

            return outcome;
        }

        // The end of the next cut from first: the whole rest of the step when it holds at most MoveStepBytes, otherwise
        // as many whole hours as fit (at least one).
        private long CutAt(MoveStep step, long first)
        {
            if (Batches.GetSensorHourBytes(step.Product, step.Sensor.Id, step.From, first, step.EndHour).Bytes <= StepBytesLimit)
                return step.EndHour;

            long bytes = 0, end = first;
            while (end < step.EndHour)
            {
                bytes += Batches.GetSensorHourBytes(step.Product, step.Sensor.Id, step.From, end, end + TimeSpan.TicksPerHour).Bytes;
                if (bytes > StepBytesLimit && end > first)
                    break;

                end += TimeSpan.TicksPerHour;
            }

            return end;
        }

        private StepOutcome CopyHours(MoveStep step, long first, long end, MoveRun run, DateTime utcNow, CancellationToken token)
        {
            var (product, id) = (step.Product, step.Sensor.Id);
            var target = new TableBatchWeek(step.From.WeekStartTicks, step.ToClass);

            // The placement of the moment: the hours are still in the step's class, and the target is still the sensor's.
            var placement = Batches.GetPlacement(product, id);
            if (!InClass(placement, step, first, end, step.From.PeriodWeeks) || _cache.GetSensor(id) is not TableSensorModel sensor ||
                TablePeriodClass.Of(sensor, utcNow) != step.ToClass)
                return StepOutcome.Skipped;

            // A target retention dropped while its class had not passed (the clock set back): nothing can be written there.
            if (Batches.IsWeekDropped(product, target))
                return Refused(step, utcNow, $"the target database {Name(target)} was dropped by retention");

            LogMoveStart(step, placement);
            run.Steps++;

            // Leftovers in the target: the placement names the step's class for these hours, not the target, so whatever the
            // target holds of the sensor there is not read (a copy or a source a lost record left). Deleted first: a copy
            // refuses a target whose hour region holds them.
            var leftover = Batches.GetSensorHourBytes(product, id, target, first, end);
            if (leftover.Batches > 0)
            {
                TableBatchSensorHours gone;
                using (_gate.Delete())
                    gone = Batches.DeleteSensorHours(product, id, target, first, end);

                Checkpoint?.Invoke("move-leftovers-deleted");
                NoteDeleted(product, target);
                Stats.Add(ref Stats.MoveLeftoversCleaned);
                _logger.Warn($"Table move: {gone.Batches} batches ({gone.Rows} rows) of sensor [{id}][{step.Sensor.FullPath}] left in {Name(target)} of product {product} " +
                             $"for {Hour(first)} to {Hour(end)} by a move whose record was lost were deleted before the copy (the placement names {Name(step.From)})");

                if (token.IsCancellationRequested)
                    return StepOutcome.Stopped;
            }

            var source = Batches.GetSensorHourBytes(product, id, step.From, first, end);
            TableBatchSensorHours copied = default;

            if (source.Batches > 0)
            {
                if (!HasDisk(source.Bytes))
                {
                    run.Steps--;
                    return Refused(step, utcNow, $"less than TableSensors.MinFreeDiskMb ({_options.MinFreeDiskMb} MB) plus the step's {source.Bytes / 1048576.0:F1} MB is free",
                        StepOutcome.DiskShort);
                }

                // C1
                Batches.SetMoveInFlight(product, new TableMoveInFlight(id, step.From.WeekStartTicks, step.From.PeriodWeeks, step.ToClass, first, end));
                Checkpoint?.Invoke("move-recorded");
                if (token.IsCancellationRequested)
                    return StepOutcome.Stopped;

                // C2
                try
                {
                    copied = Batches.CopySensorHours(product, id, step.From, step.ToClass, first, end);
                }
                catch (TableBatchRefusedException ex)
                {
                    // A decision about this step, not a fault: the record stays (the next recovery deletes nothing the
                    // placement names), the placement stays, and the step is tried again by a later pass.
                    run.Steps--;
                    return Refused(step, utcNow, ex.Message);
                }

                Checkpoint?.Invoke("move-copied");
                if (token.IsCancellationRequested)
                    return StepOutcome.Stopped;
            }

            // C3: the switch, on the placement read again (nothing but this thread writes placements, but the rule is that
            // every write is justified by the placement of the moment).
            placement = Batches.GetPlacement(product, id);
            if (!InClass(placement, step, first, end, step.From.PeriodWeeks))
                throw new InvalidOperationException($"the placement of the sensor changed during the step ({Describe(placement)}); nothing is switched or deleted");

            Batches.SetPlacement(product, id, placement.WithoutHours(first, end));
            Checkpoint?.Invoke("move-switched");
            if (token.IsCancellationRequested)
                return StepOutcome.Stopped;

            // C4: in every database of the week but the target, by the placement as it is now (the target for every hour).
            placement = Batches.GetPlacement(product, id);
            if (!InClass(placement, step, first, end, step.ToClass))
                throw new InvalidOperationException($"the placement of the sensor does not name the target after the switch ({Describe(placement)}); nothing is deleted");

            foreach (var week in DatabasesOf(product, step.From.WeekStartTicks).Where(w => w.PeriodWeeks != step.ToClass))
            {
                TableBatchSensorHours gone;
                using (_gate.Delete())
                    gone = Batches.DeleteSensorHours(product, id, week, first, end);

                Checkpoint?.Invoke("move-source-deleted");

                if (gone.Batches > 0)
                    NoteDeleted(product, week);

                if (token.IsCancellationRequested)
                    return StepOutcome.Stopped;
            }

            // C5
            if (source.Batches > 0)
            {
                Batches.SetMoveInFlight(product, null);
                Checkpoint?.Invoke("move-done");
            }

            Stats.Add(ref Stats.RowsMoved, copied.Rows);
            Stats.Add(ref Stats.MovedBytes, copied.Bytes);
            run.Bytes += copied.Bytes;
            run.MovedBytes += copied.Bytes;

            return StepOutcome.Done;
        }

        // === An expired step ===

        // The target class has passed for the week: the rows are deleted, not copied, then the hours switched. Decided right
        // before the delete by the one rule of expiry applied to the placement the switch writes (the class that holds the
        // hours is then the target), with the sensor's setting read then: the stored target and the setting must be the same
        // class and have passed. No in-flight record: a crash after the delete leaves the placement in the old class with no
        // rows there (rows that have expired may be gone), and the next pass deletes nothing and switches.
        private StepOutcome ExpireStep(MoveStep step, MoveRun run, DateTime utcNow, CancellationToken token)
        {
            var (product, id) = (step.Product, step.Sensor.Id);
            var placement = Batches.GetPlacement(product, id);
            if (!InClass(placement, step, step.FirstHour, step.EndHour, step.From.PeriodWeeks))
                return StepOutcome.Skipped;

            Checkpoint?.Invoke("move-expiring");

            if (_cache.GetSensor(id) is not TableSensorModel sensor)
                return StepOutcome.Skipped;

            int setting = TablePeriodClass.Of(sensor, utcNow);
            var after = placement.WithoutHours(step.FirstHour, step.EndHour);

            if (setting != placement.Target || !TableBatchRetention.IsExpired(after, step.FirstHour, utcNow, setting))
                return StepOutcome.Skipped;

            LogMoveStart(step, placement);
            run.Steps++;

            // E1
            long rows = 0;
            foreach (var week in DatabasesOf(product, step.From.WeekStartTicks))
            {
                TableBatchSensorHours gone;
                using (_gate.Delete())
                    gone = Batches.DeleteSensorHours(product, id, week, step.FirstHour, step.EndHour);

                Checkpoint?.Invoke("expired-deleted");
                rows += gone.Rows;

                if (gone.Batches > 0)
                    NoteDeleted(product, week);

                if (token.IsCancellationRequested)
                {
                    Stats.Add(ref Stats.RowsExpiredByMove, rows);
                    return StepOutcome.Stopped;
                }
            }

            Stats.Add(ref Stats.RowsExpiredByMove, rows);
            run.ExpiredRows += rows;

            // E2, on the placement read again.
            placement = Batches.GetPlacement(product, id);
            if (!InClass(placement, step, step.FirstHour, step.EndHour, step.From.PeriodWeeks))
                throw new InvalidOperationException($"the placement of the sensor changed during the step ({Describe(placement)}); it is not switched");

            Batches.SetPlacement(product, id, placement.WithoutHours(step.FirstHour, step.EndHour));
            Checkpoint?.Invoke("expired-switched");

            if (rows > 0)
                _logger.Info($"Table move: {rows} rows of sensor [{id}][{step.Sensor.FullPath}] in {Name(step.From)} of product {product} ({Hour(step.FirstHour)} to " +
                             $"{Hour(step.EndHour)}) deleted: its period P{step.ToClass} has passed for the week");

            return StepOutcome.Done;
        }


        // === Compaction ===

        // At most one week database a pass, on this thread after retention (never beside a drop of it): one on a product's
        // compaction list whose deleted share is still at least a tenth, not of the newest week (pinned: its flush would wait
        // for the compaction), not due to be dropped by its own class (retention removes it whole). An entry whose database
        // is gone or dropped, or whose share fell below the threshold, is removed. Never throws.
        private int RunCompactions(DateTime utcNow, MoveRun run, CancellationToken token, ref string fault)
        {
            if (token.IsCancellationRequested)
                return 0;

            IReadOnlyList<Guid> products;
            try
            {
                products = Batches.GetProducts();
            }
            catch (Exception ex)
            {
                MoveFault(Guid.Empty, utcNow, ex, "the products of the batch store could not be listed for compaction", ref fault);
                return 0;
            }

            long clockWeek = StorageWeek.GetStartTicks(utcNow.Ticks);

            foreach (var product in products)
            {
                if (run.Compactions > 0 || token.IsCancellationRequested)
                    break;

                var week = default(TableBatchWeek);

                try
                {
                    var list = Batches.GetCompactList(product);
                    if (list.Count == 0)
                        continue;

                    var weeks = Batches.GetWeeks(product).ToHashSet();
                    long pinned = Batches.GetNewestHour(product) is long newest ? Math.Min(clockWeek, StorageWeek.GetStartTicks(newest)) : clockWeek;

                    foreach (var entry in list)
                    {
                        week = entry;

                        if (!weeks.Contains(week) || Batches.IsWeekDropped(product, week))
                        {
                            Batches.RemoveFromCompactList(product, week);
                            continue;
                        }

                        if (week.WeekStartTicks >= pinned || TableBatchRetention.IsExpired(week.WeekStartTicks, week.PeriodWeeks, utcNow))
                            continue;

                        var share = Batches.GetDeletedShare(product, week);
                        if (share.Share < CompactShare)
                        {
                            Batches.RemoveFromCompactList(product, week);
                            continue;
                        }

                        if (_notShrinking.TryGetValue((product, week), out var stuck) && share.DeletedBytes - stuck < CompactShare * share.FolderBytes)
                            continue;

                        Compact(product, week, share, run, utcNow, ref fault);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    MoveFault(product, utcNow, ex, $"the compaction list of product {product} could not be handled ({Name(week)})", ref fault);
                }
            }

            return run.Compactions;
        }

        private void Compact(Guid product, TableBatchWeek week, TableBatchDeletedShare before, MoveRun run, DateTime utcNow, ref string fault)
        {
            var watch = Stopwatch.StartNew();
            run.Compactions++;
            run.Bytes += before.FolderBytes;

            try
            {
                Batches.CompactWeek(product, week);
            }
            catch (Exception ex)
            {
                MoveFault(product, utcNow, ex, $"the week database {Name(week)} of product {product} was not compacted", ref fault);
            }

            var after = Batches.GetDeletedShare(product, week);

            if (after.Share >= CompactShare)
            {
                // Its counter did not go down (a fault after the compaction): not again until a tenth more is deleted. The
                // entry stays, so a restart tries once more.
                _notShrinking[(product, week)] = after.DeletedBytes;
                _logger.Warn($"Table move: the deleted share of {Name(week)} of product {product} is still {after.Share:P0} after its compaction; " +
                             "it is compacted again once a tenth more of it is deleted, or after a restart");
                return;
            }

            _notShrinking.Remove((product, week));
            Batches.RemoveFromCompactList(product, week);
            Stats.Add(ref Stats.DatabasesCompacted);

            _logger.Info($"Table move: the week database {Name(week)} of product {product} compacted ({before.DeletedBytes / 1048576.0:F1} MB deleted): " +
                         $"{before.FolderBytes / 1048576.0:F1} MB -> {after.FolderBytes / 1048576.0:F1} MB in {watch.ElapsedMilliseconds} ms");
        }

        // After a delete in a week database: on the compaction list once a tenth of it is deleted. The list is a hint: a fault
        // here is counted and logged, and the database is put on the list by its next delete.
        private void NoteDeleted(Guid product, TableBatchWeek week)
        {
            try
            {
                if (Batches.GetDeletedShare(product, week).Share >= CompactShare && !Batches.GetCompactList(product).Contains(week))
                    Batches.AddToCompactList(product, week);
            }
            catch (TableBatchStoreException ex)
            {
                Stats.Add(ref Stats.MoveFaults);
                _logger.Error($"Table move: {Name(week)} of product {product} was not put on the compaction list: {ex.Message}");
            }
        }


        // === Helpers ===

        // Whether every hour of [first, end) is in the class the placement gives them, and the step's target is the placement's.
        private static bool InClass(TablePlacement placement, MoveStep step, long first, long end, int periodWeeks)
        {
            if (placement is null || placement.Target != step.ToClass)
                return false;

            for (long hour = first; hour < end; hour += TimeSpan.TicksPerHour)
                if (placement.ClassOf(hour) != periodWeeks)
                    return false;

            return true;
        }

        private bool HasDisk(long bytes)
        {
            if (_options.MinFreeDiskMb <= 0)
                return true;

            var folder = _database.DatabaseSettings.PathToTableBlocks;
            var free = FreeBytes?.Invoke() ?? new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder))).AvailableFreeSpace;

            return free >= _options.MinFreeDiskMb * 1024 * 1024 + bytes;
        }

        private StepOutcome Refused(MoveStep step, DateTime utcNow, string why, StepOutcome outcome = StepOutcome.Refused)
        {
            Stats.Add(ref Stats.MoveStepsRefused);

            if (FirstInHour(_moveRefusedHour, step.Product, utcNow))
                _logger.Warn($"Table move: a step of sensor [{step.Sensor.Id}][{step.Sensor.FullPath}] ({Name(step.From)} -> P{step.ToClass}, {Hour(step.FirstHour)} to " +
                             $"{Hour(step.EndHour)}) cannot run now: {why}. The rows stay where the placement names them; tried again by a later pass (logged once an hour)");

            return outcome;
        }

        private void MoveFault(Guid product, DateTime utcNow, Exception ex, string what, ref string fault)
        {
            fault = ex.Message;
            Stats.Add(ref Stats.MoveFaults);

            if (FirstInHour(_moveFaultHour, product, utcNow))
                _logger.Error(ex, $"Table move: {what}; the next pass tries again (logged once an hour per product): {ex.Message}");
        }

        private void LogMoveStart(MoveStep step, TablePlacement placement)
        {
            if (!_movesStarted.Add((step.Product, step.Sensor.Id)))
                return;

            var weeks = placement.Segments.SelectMany(s => WeeksOf(s.FirstHour, s.EndHour).Select(w => (w, s.PeriodWeeks))).Distinct().Count();
            _logger.Info($"Table move of sensor [{step.Sensor.Id}][{step.Sensor.FullPath}] of product {step.Product} started: " +
                         $"{string.Join(", ", placement.Segments.Select(s => $"P{s.PeriodWeeks} {Hour(s.FirstHour)} to {Hour(s.EndHour)}"))} -> P{placement.Target}, {weeks} sensor-weeks to move");
        }

        private void LogMoveEnd(MoveStep step)
        {
            if (!_movesStarted.Contains((step.Product, step.Sensor.Id)) || Batches.GetPlacement(step.Product, step.Sensor.Id) is not { } placement || placement.Segments.Count > 0)
                return;

            _movesStarted.Remove((step.Product, step.Sensor.Id));
            _logger.Info($"Table move of sensor [{step.Sensor.Id}][{step.Sensor.FullPath}] of product {step.Product} ended: every row is in P{placement.Target}");
        }

        private IEnumerable<TableBatchWeek> DatabasesOf(Guid product, long weekStart) =>
            Batches.GetWeeks(product).Where(w => w.WeekStartTicks == weekStart).ToList();

        private static List<long> HoursOf(long first, long end)
        {
            var hours = new List<long>();
            for (long hour = first; hour < end; hour += TimeSpan.TicksPerHour)
                hours.Add(hour);

            return hours;
        }

        private static IEnumerable<long> WeeksOf(long first, long end)
        {
            for (long week = StorageWeek.GetStartTicks(first); week < end; week += WeekTicks)
                yield return week;
        }

        private static string Name(TableBatchWeek week) => $"{new DateTime(week.WeekStartTicks, DateTimeKind.Utc):yyyy-MM-dd} P{week.PeriodWeeks}";

        private static string Hour(long ticks) => $"{new DateTime(ticks, DateTimeKind.Utc):yyyy-MM-dd HH:mm}";

        private static string Describe(TablePlacement placement) =>
            placement is null ? "none" : $"P{placement.Target}, {string.Join(", ", placement.Segments.Select(s => $"P{s.PeriodWeeks} {Hour(s.FirstHour)} to {Hour(s.EndHour)}"))}";
    }
}
