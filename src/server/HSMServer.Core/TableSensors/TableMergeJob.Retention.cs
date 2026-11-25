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
    /// The clock is the pass's; a fault is logged, counted and tried again an hour later. A period changed since the rows
    /// were merged is the move's (part 4, the next step): until it exists a database is dropped by the class it was
    /// written under.
    /// </summary>
    public sealed partial class TableMergeJob
    {
        // The hour (of the pass's clock) in which retention ran last: it runs once an hour.
        private long _retentionHour = long.MinValue;


        /// <summary>Called at the points a test stands a fault at in retention: "week-dropping" (before a drop), "expired-hour-deleting" (before an expired hour's blocks are deleted).</summary>
        internal Action<string> RetentionCheckpoint { get; set; }


        // Retention, once per hour of the clock. Never throws: a fault is logged, counted, reported, and tried again next hour.
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
                    if (week.PeriodWeeks == TablePeriodClass.Forever || !TableBatchRetention.IsExpired(week.WeekStartTicks, week.PeriodWeeks, utcNow))
                        continue;

                    if (IsOwedToAnotherClass(placements, week, utcNow))
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

        // The retention guard (#1850 part 4b): a week database whose class has passed is kept while it holds rows of a sensor
        // whose period changed since they were merged (a segment of its placement in that class and week) and whose current
        // period has not passed for that week: those rows are owed to the class they belong to now, and the move brings them
        // there. Rows whose new period has passed too go with the database.
        private static bool IsOwedToAnotherClass(IReadOnlyDictionary<Guid, TablePlacement> placements, TableBatchWeek week, DateTime utcNow)
        {
            long weekEnd = week.WeekStartTicks + 7 * TimeSpan.TicksPerDay;
            bool targetKeeps(int target) => target == TablePeriodClass.Forever || !TableBatchRetention.IsExpired(week.WeekStartTicks, target, utcNow);

            foreach (var placement in placements.Values)
                foreach (var segment in placement.Segments)
                    if (segment.PeriodWeeks == week.PeriodWeeks && segment.FirstHour < weekEnd && segment.EndHour > week.WeekStartTicks && targetKeeps(placement.Target))
                        return true;

            return false;
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

        // Whether a block's rows have expired for every table sensor with rows in it: the class of each such sensor has
        // passed for the week of the hour, and none is kept for ever. The sensors are those of the block's directory (a table
        // without a sensor, or of a block below its sensor's first sequence, is never served and counts as the shortest
        // class); a block whose directory cannot be read (or one its product's commit lists as unreadable) is judged by every
        // table sensor of its product.
        private bool IsBlockExpired(Guid product, TableBlockIndexEntry entry, bool unreadable, long hour, DateTime utcNow)
        {
            long week = StorageWeek.GetStartTicks(hour);
            var classes = unreadable ? null : BlockClasses(product, entry, hour, utcNow);
            classes ??= ProductClasses(product, hour, utcNow);

            return classes.All(c => c != TablePeriodClass.Forever && TableBatchRetention.IsExpired(week, c, utcNow));
        }

        // The classes of the sensors with rows in a block, by its directory; null when the block or its directory cannot be read.
        private List<int> BlockClasses(Guid product, TableBlockIndexEntry entry, long hour, DateTime utcNow)
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

            var classes = new List<int>();
            var resolved = new Dictionary<string, (TableSensorModel, int)>();

            for (int i = 0; i < directory.Entries.Count; i++)
            {
                if (record.Envelope.IsRefused(i))
                    continue;

                var (sensor, period) = Resolve(product, Path(directory.Entries[i].TableId), resolved, utcNow, hour, createFrom: null);
                classes.Add(sensor is not null && entry.Sequence >= sensor.FirstSequence ? period : 1);
            }

            return classes;
        }

        // The classes of every table sensor of a product (its whole tree) for the hour; the shortest class when it has none.
        private List<int> ProductClasses(Guid product, long hour, DateTime utcNow)
        {
            var classes = TableSensorsOf(product).Select(table => PlaceSensor(product, table, utcNow, createFrom: null).ClassOf(hour)).ToList();
            return classes.Count == 0 ? [1] : classes;
        }

        private void RetentionFault(ref string fault, Exception ex, string what)
        {
            fault = ex.Message;
            Stats.Add(ref Stats.RetentionFaults);
            _logger.Error(ex, $"Table retention: {what}; tried again in an hour: {ex.Message}");
        }
    }
}
