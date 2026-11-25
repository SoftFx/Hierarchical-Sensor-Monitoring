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
    /// <item>An arrival hour whose blocks are held back from the ordinary deletion (a product that did not commit it, blocks
    /// the merge could not read) is deleted once every one of those blocks has expired: the period class of every table
    /// sensor with rows in the block has passed for the hour's week, and none of them is kept for ever. A block whose
    /// directory cannot be read is judged by every table sensor of its product. The rows are logged as an Error and counted.</item>
    /// </list>
    /// The clock is the pass's; a fault is logged, counted and tried again an hour later.
    /// <para>
    /// Every decision here that deletes rows asks the one rule of expiry, <see cref="TableBatchRetention.IsExpired(TablePlacement, long, DateTime)"/>:
    /// rows are stored in the class that holds their hour and expire by the period the sensor has now, and until the move
    /// (part 4, the next step) has brought them to their class a row is kept while either keeps it.
    /// </para>
    /// </summary>
    public sealed partial class TableMergeJob
    {
        // The hour (of the pass's clock) in which retention ran last: it runs once an hour.
        private long _retentionHour = long.MinValue;


        /// <summary>Called at the points a test stands a fault at in retention: "week-dropping" (before a drop), "expired-hour-deleting" (before an expired hour's blocks are deleted).</summary>
        internal Action<string> RetentionCheckpoint { get; set; }


        // Retention, once per hour of the clock. Never throws: a fault is logged, counted, reported, and tried again next hour.
        // A product whose placements were not reconciled in this pass gets none of it (fail closed, see Reconcile): no drop of
        // its week databases, no deletion of expired blocks of an hour it has blocks in, no forgetting of its hours. It waits
        // for the retention of the next hour.
        private (int Weeks, int Hours) RunRetention(DateTime utcNow, CancellationToken token, Unreconciled unreconciled, ref string fault)
        {
            long hour = utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour;
            if (hour == _retentionHour || token.IsCancellationRequested)
                return (0, 0);

            _retentionHour = hour;

            // Expired blocks, then week databases, then the records of hours left with neither: an hour's record stays in
            // Hours while its blocks do (a dropped week does not remove it, #1850 part 4b), so the merge never takes an hour
            // whose blocks are still held as not merged; it is forgotten once no database of its week and none of its blocks is left.
            if (unreconciled.All)
            {
                _logger.Warn("Table retention: skipped in this hour, the placements of no product were brought up to their sensors' periods");
                return (0, 0);
            }

            foreach (var product in unreconciled.Products)
                _logger.Warn($"Table retention: product {product} is skipped in this hour, its placements were not brought up to its sensors' periods");

            int hours = DeleteExpiredBlocks(utcNow, token, unreconciled, ref fault);
            int weeks = token.IsCancellationRequested ? 0 : DropExpiredWeeks(utcNow, token, unreconciled, ref fault);

            if (!token.IsCancellationRequested)
                ForgetHoursWithNothingLeft(unreconciled, ref fault);

            return (weeks, hours);
        }


        // === Week databases ===

        private int DropExpiredWeeks(DateTime utcNow, CancellationToken token, Unreconciled unreconciled, ref string fault)
        {
            int dropped = 0, held = 0;
            int result = DropExpiredWeeks(utcNow, token, unreconciled, ref fault, ref dropped, ref held);
            Stats.Set(ref Stats.MoveWeeksPending, held);
            return result;
        }

        private int DropExpiredWeeks(DateTime utcNow, CancellationToken token, Unreconciled unreconciled, ref string fault, ref int dropped, ref int held)
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
                if (unreconciled.Contains(product))
                    continue;

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

                IReadOnlyDictionary<Guid, TablePlacement> placements;
                try
                {
                    placements = Batches.GetPlacements(product);
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
        // not expired by the one rule (TableBatchRetention.IsExpired of a placement and an hour): rows of a sensor whose
        // period changed since they were merged (a segment of its placement in that class, over hours of that week) and
        // whose period now has not passed for the week. Those rows are owed to the class they belong to now, and the move
        // brings them there. Rows whose new period has passed too go with the database. Every hour of a segment within one
        // week has the same answer (the week, the class holding the hour and the target are the same), so one is asked.
        private static bool HoldsUnexpiredRows(IReadOnlyDictionary<Guid, TablePlacement> placements, TableBatchWeek week, DateTime utcNow)
        {
            long weekEnd = week.WeekStartTicks + 7 * TimeSpan.TicksPerDay;

            foreach (var placement in placements.Values)
                foreach (var segment in placement.Segments)
                    if (segment.PeriodWeeks == week.PeriodWeeks && segment.FirstHour < weekEnd && segment.EndHour > week.WeekStartTicks &&
                        !TableBatchRetention.IsExpired(placement, Math.Max(segment.FirstHour, week.WeekStartTicks), utcNow))
                        return true;

            return false;
        }

        // The records of committed hours no database of whose week is left and of which the block store holds no block.
        private void ForgetHoursWithNothingLeft(Unreconciled unreconciled, ref string fault)
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
                if (unreconciled.Contains(product))
                    continue;

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

        private int DeleteExpiredBlocks(DateTime utcNow, CancellationToken token, Unreconciled unreconciled, ref string fault)
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

                // An hour is deleted whole: with blocks of a product that was not reconciled in it, nothing of it is judged.
                if (products.Keys.Any(unreconciled.Contains))
                    continue;

                try
                {
                    var held = HeldBlocks(hour, products, utcNow, delay);
                    if (held is null || held.Count == 0)
                        continue;

                    if (!held.All(h => IsBlockExpired(h.Product, h.Entry, h.Unreadable, hour, utcNow)))
                        continue;

                    RetentionCheckpoint?.Invoke("expired-hour-deleting");

                    using (_gate.Delete())
                        Blocks.DeleteMergedHour(hour, [], Watermarks(products.Keys));

                    long rows = held.Sum(h => (long)h.Entry.RowCount);
                    Stats.Add(ref Stats.ExpiredBlocksDeleted, held.Count);
                    Stats.Add(ref Stats.ExpiredBlockRowsDeleted, rows);
                    deleted++;

                    // Rows that were never merged and are gone now: their period has passed, but they were never in a batch.
                    _logger.Error($"Table retention: the arrival hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} was deleted with {held.Count} blocks " +
                                  $"never merged into batches ({rows} rows; products {string.Join(", ", held.Select(h => h.Product).Distinct())}): " +
                                  "the period of every table sensor with rows in them has passed");
                }
                catch (Exception ex)
                {
                    RetentionFault(ref fault, ex, $"the expired blocks of the arrival hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} were not deleted");
                }
            }

            return deleted;
        }

        // The blocks of an hour the ordinary deletion holds back: every block of a product that did not commit the hour, and
        // the blocks a product's commit lists as unreadable. Null when a committed product's deletion delay has not passed
        // (the hour waits for it, as the ordinary deletion does).
        private List<(Guid Product, TableBlockIndexEntry Entry, bool Unreadable)> HeldBlocks(long hour, Dictionary<Guid, List<TableBlockIndexEntry>> products,
            DateTime utcNow, long delay)
        {
            var held = new List<(Guid, TableBlockIndexEntry, bool)>();

            foreach (var (product, entries) in products)
            {
                var record = Batches.GetHour(product, hour);

                if (record is null)
                {
                    held.AddRange(entries.Select(e => (product, e, false)));
                    continue;
                }

                if (utcNow.Ticks < record.CommitTicks + delay)
                    return null;

                var keep = record.UnreadableBlocks.ToHashSet();
                held.AddRange(entries.Where(e => keep.Contains(e.Sequence)).Select(e => (product, e, true)));
            }

            return held;
        }

        // Whether a block's rows have expired for every table sensor with rows in it, by the one rule
        // (TableBatchRetention.IsExpired of the sensor's placement and the hour): the class that holds the hour for the sensor
        // has passed for the hour's week, and so has the period the sensor has now; never for a sensor kept for ever. The
        // sensors are those of the block's directory (a table without a sensor, or of a block below its sensor's first
        // sequence, is never served and counts as the shortest class); a block whose directory cannot be read (or one its
        // product's commit lists as unreadable) is judged by every table sensor of its product.
        private bool IsBlockExpired(Guid product, TableBlockIndexEntry entry, bool unreadable, long hour, DateTime utcNow)
        {
            var placements = unreadable ? null : BlockPlacements(product, entry, hour, utcNow);
            placements ??= ProductPlacements(product, hour, utcNow);

            return placements.All(p => TableBatchRetention.IsExpired(p, hour, utcNow));
        }

        // The placement that stands for rows no sensor serves: the shortest class, with nothing before the hour.
        private static TablePlacement Unserved(long hour) => new(1, hour);

        // The placements of the sensors with rows in a block, by its directory; null when the block or its directory cannot be read.
        private List<TablePlacement> BlockPlacements(Guid product, TableBlockIndexEntry entry, long hour, DateTime utcNow)
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

            var placements = new List<TablePlacement>();
            var resolved = new Dictionary<string, (TableSensorModel, TablePlacement)>();

            for (int i = 0; i < directory.Entries.Count; i++)
            {
                if (record.Envelope.IsRefused(i))
                    continue;

                var (sensor, placement) = Resolve(product, Path(directory.Entries[i].TableId), resolved, utcNow, createFrom: null);
                placements.Add(sensor is not null && entry.Sequence >= sensor.FirstSequence ? placement : Unserved(hour));
            }

            return placements;
        }

        // The placements of every table sensor of a product (its whole tree); the shortest class when it has none.
        private List<TablePlacement> ProductPlacements(Guid product, long hour, DateTime utcNow)
        {
            var placements = TableSensorsOf(product).Select(table => PlaceSensor(product, table, utcNow, createFrom: null)).ToList();
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
