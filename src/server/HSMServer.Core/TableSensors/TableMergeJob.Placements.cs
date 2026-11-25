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
        /// only when <paramref name="create"/> (the merge, before it writes the sensor's first batch); otherwise the setting's
        /// class is answered and nothing is written. A setting whose class differs from the target is a change of period:
        /// recorded durably, effective from <see cref="Cutoff"/>, so every hour before it keeps the class it has.
        /// </summary>
        internal TablePlacement PlaceSensor(Guid product, TableSensorModel sensor, DateTime utcNow, bool create)
        {
            int setting = TablePeriodClass.Of(sensor, utcNow);
            var placement = Batches.GetPlacement(product, sensor.Id);

            if (placement is null)
            {
                placement = new TablePlacement(setting);
                if (create)
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
        /// The merge hour a change of period takes effect from: an hour no part of which can be on disk. The merge writes an
        /// hour only after closing it, and only hours with blocks; so it is the hour after the newest one with blocks of the
        /// product, after the newest committed one, and not before the current hour of the clock or an earlier change. A merge
        /// that repeats an earlier hour (a run that crashed before its commit) then gives every sensor the class the earlier
        /// run gave it, and the parts of the two runs name the same classes.
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

        // Every placement of a sensor that has batches is brought up to its setting at the start of a pass, so a change on a
        // folder or a product (which moves many sensors at once and raises no event per sensor) is recorded within a pass,
        // whether or not the sensor has new rows. A product of the batch store that is not in the tree is left alone.
        private void Reconcile(DateTime utcNow, ref string fault)
        {
            IReadOnlyList<Guid> products;
            try
            {
                products = Batches.GetProducts();
            }
            catch (Exception ex)
            {
                fault = ex.Message;
                Stats.Add(ref Stats.Faults);
                _logger.Error(ex, $"Table placements: the products of the batch store could not be listed: {ex.Message}");
                return;
            }

            foreach (var product in products)
            {
                try
                {
                    var placements = Batches.GetPlacements(product);

                    foreach (var sensor in TableSensorsOf(product))
                        if (placements.ContainsKey(sensor.Id))
                            PlaceSensor(product, sensor, utcNow, create: false);
                }
                catch (Exception ex)
                {
                    fault = ex.Message;
                    Stats.Add(ref Stats.Faults);
                    _logger.Error(ex, $"Table placements of product {product} were not brought up to the sensors' periods; the next pass tries again: {ex.Message}");
                }
            }
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
