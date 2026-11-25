using System;
using System.Collections.Generic;

namespace HSMServer.Core.TableSensors
{
    /// <summary>What one product's table blocks did in a period (#1847), drained by <see cref="TableFeedCounters.DrainProducts"/>.</summary>
    public sealed class TableFeedProductPeriod
    {
        /// <summary>Blocks of the product stored in the period (a duplicate is not counted).</summary>
        public long Blocks { get; internal set; }

        /// <summary>Rows of the accepted tables of the stored blocks.</summary>
        public long Rows { get; internal set; }

        /// <summary>Stored length of the stored blocks, in bytes.</summary>
        public long Bytes { get; internal set; }

        /// <summary>Tables refused, in stored blocks and in blocks every table of which was refused.</summary>
        public long RefusedTables { get; internal set; }

        /// <summary>Blocks the merge could not read, counted at the commit of their product's hour.</summary>
        public long UnreadableBlocks { get; internal set; }

        /// <summary>
        /// The largest arrival time minus the last row time of a stored block, in ticks; negative when the provider's
        /// clock is ahead. Meaningful only when <see cref="Blocks"/> is above 0.
        /// </summary>
        public long MaxLagTicks { get; internal set; } = long.MinValue;
    }


    /// <summary>
    /// The counters the feed sensors of table sensors are posted from (#1847): per product and per table sensor, since the
    /// last drain. The block service adds to them when a block is stored (or every table of it is refused), the merge when
    /// it commits a product's hour; <c>TableFeedSensorsService</c> drains them on its periods. Nothing is decompressed for
    /// them: they come from the block's directory and header, which the put has read already.
    /// <para>One short lock per block (a few dictionary updates under it): a drain swaps the dictionaries, so no add is
    /// lost between a drain and the next, and a product or table without activity holds no memory after its period.</para>
    /// </summary>
    public sealed class TableFeedCounters
    {
        private readonly object _lock = new();

        private Dictionary<Guid, TableFeedProductPeriod> _products = [];
        private Dictionary<Guid, long> _tables = [];


        /// <summary>The arrival time of a block, the base of its delivery lag. Test seam; the UTC clock by default.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;


        /// <summary>
        /// A stored block: its accepted tables with their rows, its stored length, its refused tables and its last row time
        /// (the header's, in ticks; the lag is measured against the provider's clock).
        /// </summary>
        public void AddStored(Guid productId, IReadOnlyList<(Guid SensorId, int Rows)> accepted, int storedLength, int refusedTables, long lastRowTicks)
        {
            var lag = Clock().Ticks - lastRowTicks;

            lock (_lock)
            {
                var product = Product(productId);

                product.Blocks++;
                product.Bytes += storedLength;
                product.RefusedTables += refusedTables;

                if (lag > product.MaxLagTicks)
                    product.MaxLagTicks = lag;

                foreach (var (sensorId, rows) in accepted)
                {
                    product.Rows += rows;
                    _tables[sensorId] = _tables.GetValueOrDefault(sensorId) + rows;
                }
            }
        }

        /// <summary>A block every table of which was refused: nothing was stored, the refusals count.</summary>
        public void AddRefused(Guid productId, int refusedTables)
        {
            if (refusedTables <= 0)
                return;

            lock (_lock)
                Product(productId).RefusedTables += refusedTables;
        }

        /// <summary>Blocks the merge could not read in a product's hour it committed.</summary>
        public void AddUnreadable(Guid productId, int blocks)
        {
            if (blocks <= 0)
                return;

            lock (_lock)
                Product(productId).UnreadableBlocks += blocks;
        }

        /// <summary>The product counters since the last drain; they start again from nothing.</summary>
        public Dictionary<Guid, TableFeedProductPeriod> DrainProducts()
        {
            lock (_lock)
            {
                var drained = _products;
                _products = [];
                return drained;
            }
        }

        /// <summary>The rows of each table sensor since the last drain; they start again from nothing.</summary>
        public Dictionary<Guid, long> DrainTables()
        {
            lock (_lock)
            {
                var drained = _tables;
                _tables = [];
                return drained;
            }
        }

        // Under _lock.
        private TableFeedProductPeriod Product(Guid productId)
        {
            if (!_products.TryGetValue(productId, out var product))
                _products[productId] = product = new TableFeedProductPeriod();

            return product;
        }
    }
}
