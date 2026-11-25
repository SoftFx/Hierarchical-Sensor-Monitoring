using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.TableBatches;
using HSMServer.Core.Model;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// The placements of table sensors (#1850 part 4b, ADR-0021 point 12): where the batches of each sensor are, kept by the
    /// batch store. The job is the only reader of a table sensor's history setting for storage: <see cref="PlaceSensor"/>
    /// compares the setting with the placement's target and records a change, from a cut-off hour on. The merge writes an
    /// hour into <see cref="TablePlacement.ClassOf"/> and the reads look there, so the two never disagree.
    /// </summary>
    public sealed partial class TableMergeJob
    {
        /// <summary>
        /// The placement of a table sensor of a product, brought up to its history setting. A sensor without one gets one
        /// only when <paramref name="createFrom"/> is given (the merge, before it writes the sensor's first batch): the
        /// first hour of the new placement (<see cref="FirstUncommittedHour"/>). Otherwise the setting's class is answered
        /// and nothing is written. A setting whose class differs from the target is a change of period: recorded durably,
        /// effective from <see cref="Cutoff"/>, so every hour before it keeps the class it has.
        /// </summary>
        internal TablePlacement PlaceSensor(Guid product, TableSensorModel sensor, DateTime utcNow, long? createFrom)
        {
            int setting = TablePeriodClass.Of(sensor, utcNow);
            var placement = Batches.GetPlacement(product, sensor.Id);

            if (placement is null)
            {
                // Not stored: a sensor without batches, as it would be placed now (it has no segment, so its first hour
                // decides nothing).
                if (createFrom is not long firstHour)
                    return new TablePlacement(setting, utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour);

                placement = new TablePlacement(setting, firstHour);
                Batches.SetPlacement(product, sensor.Id, placement);

                return placement;
            }

            if (placement.Target == setting)
                return placement;

            long cutoff = Cutoff(product, utcNow, placement);
            var changed = placement.Retarget(setting, cutoff);
            Batches.SetPlacement(product, sensor.Id, changed);

            _logger.Info($"Table sensor [{sensor.Id}][{sensor.FullPath}]: period class P{placement.Target} -> P{setting} from the merge hour " +
                         $"{new DateTime(cutoff, DateTimeKind.Utc):yyyy-MM-dd HH:mm}; earlier hours stay where they are ({changed.Segments.Count} segments)");
            return changed;
        }

        /// <summary>
        /// The terms of the rule of expiry for a table sensor, for retention: its placement as stored (or, with none stored,
        /// the one it would get now, which has no segment) and the class of its history setting as read now. Writes
        /// nothing, so a placement write that fails never stops a decision; a change not yet written is covered by the
        /// setting's term (<see cref="TableBatchRetention.IsExpired(TablePlacement, long, DateTime, int?)"/>).
        /// </summary>
        private (TablePlacement Placement, int? Setting) StoredPlacement(Guid product, TableSensorModel sensor, DateTime utcNow)
        {
            int setting = TablePeriodClass.Of(sensor, utcNow);
            var placement = Batches.GetPlacement(product, sensor.Id) ?? new TablePlacement(setting, utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour);

            return (placement, setting);
        }

        /// <summary>
        /// The merge hour a change of period takes effect from: an hour no part of which can be on disk, and none of whose
        /// blocks can have been merged under the old class. The latest of:
        /// <list type="bullet">
        /// <item>the current hour of the pass's clock;</item>
        /// <item>the hour AFTER the newest arrival hour with blocks of the product, the open hour included: at 10:05 with a
        /// block in the 10:00 hour the cut-off is 11:00, and the whole 10:00 hour stays in the old class;</item>
        /// <item>the hour after the newest committed hour;</item>
        /// <item>the end of the last segment (an earlier change).</item>
        /// </list>
        /// The later cut-off is the safe one. An hour that holds blocks is never at or after it, whatever the clocks say: the
        /// merge takes an hour for closed by the clock of its pass, the block store files a block under the hour of its own
        /// clock, and a clock set back can make an hour look open that a run of the merge has already written parts of. A
        /// merge that repeats an earlier hour (a run that crashed before its commit) then gives every sensor the class the
        /// earlier run gave it, and the parts of the two runs name the same classes. The price: a change made while the
        /// current hour already holds blocks applies to the rows that arrive from the next hour on.
        /// </summary>
        private long Cutoff(Guid product, DateTime utcNow, TablePlacement placement)
        {
            long cutoff = utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour;

            foreach (var entry in Blocks.GetBlocks(product))
                cutoff = Math.Max(cutoff, entry.ArrivalHourTicks + TimeSpan.TicksPerHour);

            if (Batches.GetNewestHour(product) is long newest)
                cutoff = Math.Max(cutoff, newest + TimeSpan.TicksPerHour);

            if (placement.Segments.Count > 0)
                cutoff = Math.Max(cutoff, placement.Segments[^1].EndHour);

            return cutoff;
        }

        /// <summary>
        /// The first merge hour a sensor whose placement is created during the merge of <paramref name="hour"/> can have
        /// rows in: the oldest arrival hour with blocks of the product that is not committed. That is the hour being merged,
        /// unless an earlier hour was left behind (a batch that did not verify, merged after the next start): it is merged
        /// later and may hold the sensor's rows. Durable in the placement (<see cref="TablePlacement.FirstHour"/>): a segment
        /// of a later change of period starts there, not at the start of time.
        /// </summary>
        private long FirstUncommittedHour(Guid product, long hour)
        {
            long first = hour;
            var asked = new HashSet<long>();

            foreach (var entry in Blocks.GetBlocks(product))
                if (entry.ArrivalHourTicks < first && asked.Add(entry.ArrivalHourTicks) && !Batches.IsHourMerged(product, entry.ArrivalHourTicks))
                    first = entry.ArrivalHourTicks;

            return first;
        }

        // The products a pass does not merge because their placements are not known to be up to their sensors' periods: the
        // ones named, or every product when the batch store could not even list them.
        private sealed class Unreconciled
        {
            public readonly HashSet<Guid> Products = [];
            public bool All;

            public int Count => All ? Math.Max(1, Products.Count) : Products.Count;

            public bool Contains(Guid product) => All || Products.Contains(product);
        }

        // Every placement of a sensor that has batches is brought up to its setting at the start of a pass, so a change on a
        // folder or a product (which moves many sensors at once and raises no event per sensor) is recorded within a pass,
        // whether or not the sensor has new rows. A product of the batch store that is not in the tree is left alone.
        //
        // Fail closed only where a placement says WHERE to write: a product for which this did not complete (the synced
        // write of a placement failed, its placements could not be read) is answered as not reconciled, and its hours are
        // not merged in this pass. Retention and the deletion of expired blocks still run for it: every expiry decision
        // reads the sensor's setting as it is at the decision as its third term (TableBatchRetention.IsExpired), so a
        // placement on record that a lengthened period has made stale cannot cause a drop the setting forbids. Otherwise a
        // product whose placement write keeps failing on a full disk would free no disk at all, and the write would keep
        // failing. Counted (ProductsNotReconciled, Faults) and logged once per product and hour of the pass's clock (as
        // retention runs once an hour), not once a pass; the report of every pass names it; the next pass tries again.
        private Unreconciled Reconcile(DateTime utcNow, ref string fault)
        {
            var unreconciled = new Unreconciled();

            IReadOnlyList<Guid> products;
            try
            {
                products = Batches.GetProducts();
            }
            catch (Exception ex)
            {
                fault = ex.Message;
                unreconciled.All = true;
                if (FirstInHour(Guid.Empty, utcNow))
                {
                    Stats.Add(ref Stats.Faults);
                    Stats.Add(ref Stats.ProductsNotReconciled);
                    _logger.Error(ex, "Table placements: the products of the batch store could not be listed, so no placement was brought up to its sensor's period; " +
                                      $"nothing is merged in this pass, and the next pass tries again (logged once an hour): {ex.Message}");
                }
                return unreconciled;
            }

            foreach (var product in products)
            {
                try
                {
                    var placements = Batches.GetPlacements(product);

                    foreach (var sensor in TableSensorsOf(product))
                        if (placements.ContainsKey(sensor.Id))
                            PlaceSensor(product, sensor, utcNow, createFrom: null);
                }
                catch (Exception ex)
                {
                    fault = ex.Message;
                    unreconciled.Products.Add(product);
                    if (FirstInHour(product, utcNow))
                    {
                        Stats.Add(ref Stats.Faults);
                        Stats.Add(ref Stats.ProductsNotReconciled);
                        _logger.Error(ex, $"Table placements of product {product} were not brought up to the sensors' periods; its hours are not merged in this pass " +
                                          $"(retention runs, by the sensors' settings as they are), and the next pass tries again (logged once an hour): {ex.Message}");
                    }
                }
            }

            return unreconciled;
        }

        // The hour of the pass's clock in which each product's failed reconciliation was last counted and logged (Guid.Empty:
        // the products could not be listed).
        private readonly Dictionary<Guid, long> _notReconciledHour = [];

        // Whether this is the first failed reconciliation of the product in this hour of the pass's clock (and notes it).
        private bool FirstInHour(Guid product, DateTime utcNow)
        {
            long hour = utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerHour;
            if (_notReconciledHour.TryGetValue(product, out var last) && last == hour)
                return false;

            _notReconciledHour[product] = hour;
            return true;
        }

        // The table sensors of a product's tree.
        private List<TableSensorModel> TableSensorsOf(Guid product)
        {
            var sensors = new List<TableSensorModel>();

            void Visit(ProductModel node)
            {
                foreach (var (_, sensor) in node.Sensors)
                    if (sensor is TableSensorModel table)
                        sensors.Add(table);

                foreach (var (_, child) in node.SubProducts)
                    Visit(child);
            }

            if (_cache.TryGetProduct(product, out var model))
                Visit(model);

            return sensors;
        }
    }
}
