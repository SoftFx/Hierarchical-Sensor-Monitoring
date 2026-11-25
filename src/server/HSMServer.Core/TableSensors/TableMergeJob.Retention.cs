using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.Core.Model;
using HSMTableBlocks;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// Retention of table rows (#1850 part 4, ADR-0021 point 12), run by the merge pass after the merge, at most once per
    /// hour of the pass's clock:
    /// <list type="number">
    /// <item>Every week database of the batch store whose period class has passed for its week
    /// (<see cref="TableBatchRetention.IsExpired"/>: the week's end plus the class's whole weeks) is dropped as a folder
    /// (<see cref="ITableBatchStore.DropWeek"/>). A class-0 (for ever) database is never dropped.</item>
    /// <item>The blocks of an arrival hour that the ordinary deletion holds back for a product (all of them when the product
    /// did not commit the hour, the ones its merge could not read when it did) are deleted once every one of them has
    /// expired for every table sensor with rows in it, by the rule below. Per product: another product's blocks of the
    /// same hour stay as they are. A block whose directory cannot be read is judged by every table sensor of its product.
    /// The rows are logged as an Error and counted.</item>
    /// </list>
    /// The clock is the pass's; a fault is logged, counted and tried again an hour later.
    /// <para>
    /// Every decision here that deletes rows asks the one rule of expiry,
    /// <see cref="TableBatchRetention.IsExpired(TablePlacement, long, DateTime, int?)"/>, with three terms: the class that
    /// holds the hour, the stored target, and the class of the sensor's setting as read at the decision. A row is kept while
    /// any of them keeps it, so the decisions do not depend on a placement write having succeeded: retention runs for a
    /// product whose placements could not be reconciled in the pass, and writes no placement itself.
    /// </para>
    /// </summary>
    public sealed partial class TableMergeJob
    {
        // The hour (of the pass's clock) in which retention ran last: it runs once an hour.
        private long _retentionHour = long.MinValue;


        /// <summary>Called at the points a test stands a fault at in retention: "week-dropping" (before a drop), "expired-hour-deleting" (before an expired hour's blocks are deleted).</summary>
        internal Action<string> RetentionCheckpoint { get; set; }


        // Retention, once per hour of the clock. Never throws: a fault is logged, counted, reported, and tried again next hour.
        // It runs for every product, reconciled in this pass or not: it decides by the stored placements AND the sensors'
        // settings as they are now, and it writes no placement.
        private (int Weeks, int Hours) RunRetention(DateTime utcNow, CancellationToken token, ref string fault)
        {
            long hour = utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour;
            if (hour == _retentionHour || token.IsCancellationRequested)
                return (0, 0);

            _retentionHour = hour;

            // Expired blocks, then week databases, then the records of hours left with neither: an hour's record stays in
            // Hours while its blocks do (a dropped week does not remove it, #1850 part 4b), so the merge never takes an hour
            // whose blocks are still held as not merged; it is forgotten once no database of its week and none of its blocks is left.
            int hours = DeleteExpiredBlocks(utcNow, token, ref fault);
            int weeks = token.IsCancellationRequested ? 0 : DropExpiredWeeks(utcNow, token, ref fault);

            if (!token.IsCancellationRequested)
                ForgetHoursWithNothingLeft(ref fault);

            return (weeks, hours);
        }


        // === Week databases ===

        private int DropExpiredWeeks(DateTime utcNow, CancellationToken token, ref string fault)
        {
            int dropped = 0, held = 0;
            int result = DropExpiredWeeks(utcNow, token, ref fault, ref dropped, ref held);
            Stats.Set(ref Stats.MoveWeeksPending, held);
            return result;
        }

        private int DropExpiredWeeks(DateTime utcNow, CancellationToken token, ref string fault, ref int dropped, ref int held)
        {
            IReadOnlyList<Guid> products;

            try
            {
                products = Batches.GetProducts();
            }
            catch (Exception ex)
            {
                RetentionFault(ref fault, ex, "the products of the batch store could not be listed");
                return 0;
            }

            foreach (var product in products)
            {
                IReadOnlyList<TableBatchWeek> weeks;
                try
                {
                    weeks = Batches.GetWeeks(product);
                }
                catch (Exception ex)
                {
                    RetentionFault(ref fault, ex, $"the week databases of product {product} could not be listed");
                    continue;
                }

                List<(TablePlacement Placement, int? Setting)> placements;
                try
                {
                    // Each stored placement with the class of its sensor's setting as read now; a sensor no longer in the
                    // tree has no setting, and its stored placement alone decides.
                    var settings = TableSensorsOf(product).ToDictionary(s => s.Id, s => TablePeriodClass.Of(s, utcNow));
                    placements = Batches.GetPlacements(product)
                                        .Select(p => (p.Value, settings.TryGetValue(p.Key, out var setting) ? setting : (int?)null))
                                        .ToList();
                }
                catch (Exception ex)
                {
                    RetentionFault(ref fault, ex, $"the placements of product {product} could not be read");
                    continue;
                }

                foreach (var week in weeks)
                {
                    // Due by its own class: the rows of every sensor whose period is that class have expired (never for the
                    // class kept for ever).
                    if (!TableBatchRetention.IsExpired(week.WeekStartTicks, week.PeriodWeeks, utcNow))
                        continue;

                    if (HoldsUnexpiredRows(placements, week, utcNow))
                    {
                        held++;
                        continue;
                    }

                    if (token.IsCancellationRequested)
                        return dropped;

                    try
                    {
                        RetentionCheckpoint?.Invoke("week-dropping");

                        // Under the write side of the read gate: a read in flight ends before the week goes, so one read sees
                        // every hour of the week or none (the store's own wait covers only the hour a read holds at the moment).
                        using (_gate.Delete())
                            Batches.DropWeek(product, week);
                        dropped++;

                        _logger.Info($"Table retention: the week database {new DateTime(week.WeekStartTicks, DateTimeKind.Utc):yyyy-MM-dd} P{week.PeriodWeeks} " +
                                     $"of product {product} was dropped (its period of {week.PeriodWeeks} weeks has passed)");
                    }
                    catch (Exception ex)
                    {
                        // A drop whose folder could not be deleted is dropped all the same (the next open of the product
                        // deletes the folder); any other fault leaves the week listed for the next hour.
                        RetentionFault(ref fault, ex, $"the week database {new DateTime(week.WeekStartTicks, DateTimeKind.Utc):yyyy-MM-dd} P{week.PeriodWeeks} of product {product} was not dropped");
                    }
                }
            }

            return dropped;
        }

        // The retention guard (#1850 part 4b): a week database whose class has passed is kept while it may hold rows that have
        // not expired by the one rule (TableBatchRetention.IsExpired of a placement, an hour and the sensor's setting now):
        // rows of a sensor whose placement puts hours of that week in that class (a segment, or its target outside its
        // segments from its first hour on) and whose stored target or setting now has not passed for the week. Rows of a
        // sensor whose period changed since they were merged are owed to the class they belong to now, and the move brings
        // them there; rows of a sensor whose change is not written yet (the write failed, or it came after the
        // reconciliation of this pass) are held by its setting alone. Rows whose every term has passed go with the
        // database. Every hour of the week in that class has the same answer (the week, the class holding the hour, the
        // target and the setting are the same), so one is asked.
        private static bool HoldsUnexpiredRows(List<(TablePlacement Placement, int? Setting)> placements, TableBatchWeek week, DateTime utcNow)
        {
            long weekEnd = week.WeekStartTicks + 7 * TimeSpan.TicksPerDay;

            foreach (var (placement, setting) in placements)
                if (FirstHourInClass(placement, week.PeriodWeeks, week.WeekStartTicks, weekEnd) is long hour &&
                    !TableBatchRetention.IsExpired(placement, hour, utcNow, setting))
                    return true;

            return false;
        }

        // The first merge hour of [from, to), not before the placement's first hour, whose rows the placement puts in class
        // periodWeeks; null when it puts none there.
        private static long? FirstHourInClass(TablePlacement placement, int periodWeeks, long from, long to)
        {
            long start = Math.Max(from, placement.FirstHour);
            if (start >= to)
                return null;

            long? first = null;
            long covered = start;

            foreach (var segment in placement.Segments)
            {
                if (segment.EndHour <= start || segment.FirstHour >= to)
                    continue;

                // The hours before this segment are in the target.
                if (placement.Target == periodWeeks && segment.FirstHour > covered)
                    return covered;

                if (segment.PeriodWeeks == periodWeeks)
                    first ??= Math.Max(segment.FirstHour, start);

                covered = Math.Max(covered, segment.EndHour);
            }

            if (placement.Target == periodWeeks && covered < to)
                first = first is long f ? Math.Min(f, covered) : covered;

            return first;
        }

        // The records of committed hours no database of whose week is left and of which the block store holds no block.
        private void ForgetHoursWithNothingLeft(ref string fault)
        {
            IReadOnlyList<Guid> products;
            try
            {
                products = Batches.GetProducts();
            }
            catch (Exception ex)
            {
                RetentionFault(ref fault, ex, "the products of the batch store could not be listed");
                return;
            }

            foreach (var product in products)
            {
                try
                {
                    var hours = Batches.GetHoursWithoutDatabases(product);
                    if (hours.Count == 0)
                        continue;

                    var held = Blocks.GetBlocks(product).Select(e => e.ArrivalHourTicks).ToHashSet();
                    var forget = hours.Where(h => !held.Contains(h)).ToList();

                    if (forget.Count > 0)
                        Batches.ForgetHours(product, forget);
                }
                catch (Exception ex)
                {
                    RetentionFault(ref fault, ex, $"the merged hours of product {product} left without a database could not be removed");
                }
            }
        }


        // === Blocks never merged ===

        private int DeleteExpiredBlocks(DateTime utcNow, CancellationToken token, ref string fault)
        {
            SortedDictionary<long, Dictionary<Guid, List<TableBlockIndexEntry>>> byHour;

            try
            {
                byHour = BlocksByHour();
            }
            catch (Exception ex)
            {
                RetentionFault(ref fault, ex, "the blocks could not be listed");
                return 0;
            }

            var delay = TimeSpan.FromHours(_options.DeleteMergedBlocksAfterHours).Ticks;
            int deleted = 0;

            foreach (var (hour, products) in byHour)
            {
                long week = StorageWeek.GetStartTicks(hour);

                // The shortest period is one week: an hour of a week that ended less than a week ago has not expired for any
                // sensor, so an hour that is merely late (the merge lagging, a product faulting) is never looked at here. The
                // hours ascend: every later one is younger.
                if (!TableBatchRetention.IsExpired(week, 1, utcNow) || token.IsCancellationRequested)
                    break;

                try
                {
                    // Per product: its held blocks go when every one of them has expired. Another product's blocks of the
                    // hour, held or not, stay (the block store deletes by sequence), so one product kept for ever, within
                    // its delay, or faulting (its batches cannot be read, a read error on one of its blocks) does not hold
                    // the expired blocks of another. A fault keeps that product's blocks and is counted and logged (once
                    // an hour: retention runs once an hour); the next retention tries again.
                    var expired = new List<(Guid Product, TableBlockIndexEntry Entry, bool Unreadable)>();

                    foreach (var (product, entries) in products)
                    {
                        try
                        {
                            var held = HeldBlocks(product, entries, hour, utcNow, delay);
                            if (held is not null && held.Count > 0 && held.All(h => IsBlockExpired(product, h.Entry, h.Unreadable, hour, utcNow)))
                                expired.AddRange(held.Select(h => (product, h.Entry, h.Unreadable)));
                        }
                        catch (Exception ex)
                        {
                            RetentionFault(ref fault, ex, $"the expired blocks of product {product} in the arrival hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} were not judged");
                        }
                    }

                    if (expired.Count == 0)
                        continue;

                    RetentionCheckpoint?.Invoke("expired-hour-deleting");

                    var going = expired.Select(h => h.Entry.Sequence).ToHashSet();
                    var keep = products.Values.SelectMany(list => list).Select(e => e.Sequence).Where(s => !going.Contains(s)).ToHashSet();
                    var owners = expired.Select(h => h.Product).Distinct().ToList();

                    using (_gate.Delete())
                        Blocks.DeleteMergedHour(hour, keep, Watermarks(owners));

                    long rows = expired.Sum(h => (long)h.Entry.RowCount);
                    Stats.Add(ref Stats.ExpiredBlocksDeleted, expired.Count);
                    Stats.Add(ref Stats.ExpiredBlockRowsDeleted, rows);
                    deleted++;

                    // Rows that were never merged and are gone now: their period has passed, but they were never in a batch.
                    _logger.Error($"Table retention: {expired.Count} blocks of the arrival hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} " +
                                  $"never merged into batches were deleted ({rows} rows; products {string.Join(", ", owners)}): " +
                                  "the period of every table sensor with rows in them has passed");
                }
                catch (Exception ex)
                {
                    RetentionFault(ref fault, ex, $"the expired blocks of the arrival hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} were not deleted");
                }
            }

            return deleted;
        }

        // The blocks of a product in an hour that the ordinary deletion holds back: every block when the product did not
        // commit the hour, and the blocks its commit lists as unreadable. Null when the product committed the hour and its
        // deletion delay has not passed (they wait for it, as the ordinary deletion does).
        private List<(TableBlockIndexEntry Entry, bool Unreadable)> HeldBlocks(Guid product, List<TableBlockIndexEntry> entries, long hour, DateTime utcNow, long delay)
        {
            var record = Batches.GetHour(product, hour);

            if (record is null)
                return entries.Select(e => (e, false)).ToList();

            if (utcNow.Ticks < record.CommitTicks + delay)
                return null;

            var unreadable = record.UnreadableBlocks.ToHashSet();
            return entries.Where(e => unreadable.Contains(e.Sequence)).Select(e => (e, true)).ToList();
        }

        // Whether a block's rows have expired for every table sensor with rows in it, by the one rule
        // (TableBatchRetention.IsExpired of the sensor's stored placement, the hour and the sensor's setting as read now): the
        // class that holds the hour for the sensor, its stored target and its setting have all passed for the hour's week;
        // never for a sensor kept for ever by any of them. The sensors are those of the block's directory (a table without a
        // sensor, or of a block below its sensor's first sequence, is never served and counts as the shortest class); a block
        // whose directory cannot be read (or one its product's commit lists as unreadable) is judged by every table sensor of
        // its product. Nothing is written: a placement write that fails does not stop this decision.
        private bool IsBlockExpired(Guid product, TableBlockIndexEntry entry, bool unreadable, long hour, DateTime utcNow)
        {
            var placements = unreadable ? null : BlockPlacements(product, entry, hour, utcNow);
            placements ??= ProductPlacements(product, hour, utcNow);

            return placements.All(p => TableBatchRetention.IsExpired(p.Placement, hour, utcNow, p.Setting));
        }

        // The terms that stand for rows no sensor serves: the shortest class, with nothing before the hour, and no setting.
        private static (TablePlacement, int?) Unserved(long hour) => (new TablePlacement(1, hour), null);

        // The placements (and settings) of the sensors with rows in a block, by its directory; null when the block or its
        // directory cannot be read.
        private List<(TablePlacement Placement, int? Setting)> BlockPlacements(Guid product, TableBlockIndexEntry entry, long hour, DateTime utcNow)
        {
            // A read error is a fault (thrown): the hour is tried again next hour, never judged without the block.
            var record = Blocks.ReadBlock(entry, readErrorIsFault: true);
            if (record is null)
                return null;

            TableBlockDirectory directory;
            try
            {
                directory = TableBlockReader.ReadDirectory(record.Block.Span, TableRowCollector.StoredBlockLimits);
            }
            catch (TableBlockFormatException)
            {
                return null;
            }

            var placements = new List<(TablePlacement, int?)>();

            for (int i = 0; i < directory.Entries.Count; i++)
            {
                if (record.Envelope.IsRefused(i))
                    continue;

                var path = Path(directory.Entries[i].TableId);
                placements.Add(path is not null && _cache.TryGetSensorByPath(product, path, out var found) && found is TableSensorModel sensor &&
                               entry.Sequence >= sensor.FirstSequence
                    ? StoredPlacement(product, sensor, utcNow)
                    : Unserved(hour));
            }

            return placements;
        }

        // The placements (and settings) of every table sensor of a product (its whole tree); the shortest class when it has none.
        private List<(TablePlacement Placement, int? Setting)> ProductPlacements(Guid product, long hour, DateTime utcNow)
        {
            var placements = TableSensorsOf(product).Select(table => StoredPlacement(product, table, utcNow)).ToList();
            return placements.Count == 0 ? [Unserved(hour)] : placements;
        }

        private void RetentionFault(ref string fault, Exception ex, string what)
        {
            fault = ex.Message;
            Stats.Add(ref Stats.RetentionFaults);
            _logger.Error(ex, $"Table retention: {what}; tried again in an hour: {ex.Message}");
        }
    }
}
