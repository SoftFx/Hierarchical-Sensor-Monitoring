using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.Sealed;

namespace HSMDatabase.LevelDB.TableBatches
{
    // The removal of table sensors and root products (#1850 part 4d): the store-wide record of the removals the tree made
    // (Removed), and the removal of a product's folder whole. The merge job deletes a removed sensor's rows with
    // DeleteSensorHours and its placement with SetPlacement(null).
    public sealed partial class TableBatchDatabase
    {
        /// <summary>
        /// The store-wide record of removals (#1850 part 4d): a text file in the store's folder, one mark per line,
        /// <c>S &lt;sensor&gt; &lt;product&gt;</c> for a table sensor (the product's id of 32 zeros when not known) and
        /// <c>P &lt;product&gt;</c> for a root product, ids in the "N" format; written atomically and synced (strict), deleted
        /// when empty. A file: the store's product listing looks at folders only, so this build and the builds before it
        /// ignore it.
        /// </summary>
        public const string RemovedName = "Removed";

        /// <summary>The suffix of a removed product's folder before it is deleted: <c>&lt;product&gt;.removed-&lt;ticks&gt;</c>.</summary>
        public const string RemovedFolderSuffix = ".removed-";

        // The marks, under _removalsLock; null until the file was read (a file that cannot be read is read again at the next
        // call: nothing is taken as removed, and nothing written over it, while it is not known).
        private readonly object _removalsLock = new();
        private Dictionary<Guid, TableRemoval> _sensorMarks;
        private Dictionary<Guid, TableRemoval> _productMarks;

        // Products removed in this run (RemoveProduct): Acquire treats every database of them as absent. Under _cacheLock.
        private readonly HashSet<Guid> _productsGone = [];

        private int _removedFoldersDeleted;

        /// <summary>Reads the lines of the record of removals. Test seam for a record that cannot be read.</summary>
        internal Func<string, string[]> ReadRemovedLines { get; set; } = File.ReadAllLines;


        public void MarkRemoved(IReadOnlyCollection<(Guid Product, Guid Sensor)> sensors, IReadOnlyCollection<Guid> products)
        {
            ArgumentNullException.ThrowIfNull(sensors);
            ArgumentNullException.ThrowIfNull(products);

            if (sensors.Count == 0 && products.Count == 0)
                return;

            lock (_removalsLock)
            {
                ThrowIfClosed();
                LoadRemovals();

                var sensorMarks = new Dictionary<Guid, TableRemoval>(_sensorMarks);
                var productMarks = new Dictionary<Guid, TableRemoval>(_productMarks);
                var write = false;

                foreach (var (product, sensor) in sensors)
                {
                    // A sensor marked again keeps the product named before when this call does not know it.
                    var known = product != Guid.Empty || !sensorMarks.TryGetValue(sensor, out var earlier) ? product : earlier.ProductId;
                    write |= !sensorMarks.TryGetValue(sensor, out var old) || old.ProductId != known;
                    sensorMarks[sensor] = new TableRemoval(known, sensor, Earlier: false);
                }

                foreach (var product in products)
                {
                    write |= !productMarks.ContainsKey(product);
                    productMarks[product] = new TableRemoval(product, null, Earlier: false);
                }

                // Only the marks' Earlier changed (a mark of an earlier run made again): the file is the same.
                if (write)
                    WriteRemovals(sensorMarks, productMarks);

                (_sensorMarks, _productMarks) = (sensorMarks, productMarks);
            }
        }

        public IReadOnlyList<TableRemoval> GetRemovals()
        {
            lock (_removalsLock)
            {
                ThrowIfClosed();
                LoadRemovals();

                return _productMarks.Values.OrderBy(m => m.ProductId).Concat(_sensorMarks.Values.OrderBy(m => m.SensorId)).ToArray();
            }
        }

        public void ConfirmRemoved(IReadOnlyCollection<Guid> sensors, IReadOnlyCollection<Guid> products)
        {
            ArgumentNullException.ThrowIfNull(sensors);
            ArgumentNullException.ThrowIfNull(products);

            lock (_removalsLock)
            {
                // Nothing to confirm while the record was never read (a mark that could not be written is not there).
                if (_sensorMarks is null)
                    return;

                var sensorMarks = new Dictionary<Guid, TableRemoval>(_sensorMarks);
                var productMarks = new Dictionary<Guid, TableRemoval>(_productMarks);

                foreach (var sensor in sensors)
                    if (sensorMarks.TryGetValue(sensor, out var mark) && !mark.Earlier)
                        sensorMarks[sensor] = mark with { Confirmed = true };

                foreach (var product in products)
                    if (productMarks.TryGetValue(product, out var mark) && !mark.Earlier)
                        productMarks[product] = mark with { Confirmed = true };

                (_sensorMarks, _productMarks) = (sensorMarks, productMarks);
            }
        }

        public void ClearRemovals(IReadOnlyCollection<TableRemoval> removals)
        {
            ArgumentNullException.ThrowIfNull(removals);

            lock (_removalsLock)
            {
                ThrowIfClosed();
                LoadRemovals();

                var sensorMarks = new Dictionary<Guid, TableRemoval>(_sensorMarks);
                var productMarks = new Dictionary<Guid, TableRemoval>(_productMarks);
                var cleared = false;

                // Only the same mark: one made again in this run since it was read stays.
                foreach (var mark in removals)
                    cleared |= mark.SensorId is Guid sensor
                        ? sensorMarks.TryGetValue(sensor, out var current) && current == mark && sensorMarks.Remove(sensor)
                        : productMarks.TryGetValue(mark.ProductId, out var product) && product == mark && productMarks.Remove(mark.ProductId);

                if (!cleared)
                    return;

                WriteRemovals(sensorMarks, productMarks);
                (_sensorMarks, _productMarks) = (sensorMarks, productMarks);
            }
        }

        public void RemoveProduct(Guid productId)
        {
            ProductSlot slot;

            lock (_productsLock)
            {
                if (_disposed)
                    throw new TableBatchStoreException("The table batch store is closed.");

                if (!_products.TryGetValue(productId, out slot))
                    _products[productId] = slot = new ProductSlot();
            }

            // An open in flight ends first (its gate); from the moment the slot is marked, every call finds no product (a
            // read) or is refused (a write), and nothing opens it again.
            ProductState state;
            bool gateTaken = false;
            try
            {
                EnterGate(slot, "product-busy", ref gateTaken);

                lock (_productsLock)
                {
                    if (_disposed)
                        throw new TableBatchStoreException("The table batch store is closed.");

                    slot.Removed = true;
                    state = slot.State;
                    slot.State = null;
                }
            }
            finally
            {
                if (gateTaken)
                    Monitor.Exit(slot.Gate);
            }

            lock (_cacheLock)
                _productsGone.Add(productId);

            // A call that took the product's state before holds its lock (a write: it ends first, and finds the product
            // closed if it comes after) or a lease on a week database (a read: the database is closed once it ends).
            if (state is not null)
            {
                lock (state.Lock)
                {
                    CloseProductHandles(productId);
                    CloseProductDatabases(state);
                }
            }
            else
                CloseProductHandles(productId);

            var folder = ProductPath(productId);
            if (!Directory.Exists(folder))
                return;

            var aside = $"{folder}{RemovedFolderSuffix}{DateTime.UtcNow.Ticks}";

            try
            {
                Checkpoint?.Invoke("product-renaming");
                Directory.Move(folder, aside);
                DurableFile.SyncDirectory(_root, strict: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Statistics.Add(ref Statistics.WriteFailures);
                throw new TableBatchStoreException($"The folder of the removed product {productId} could not be renamed for its delete: {ex.Message}", ex);
            }

            Checkpoint?.Invoke("product-renamed");

            try
            {
                DeleteFolder(aside);
                _logger.Info($"The table batches of the removed product {productId} were deleted");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not listed as a product (its name is not an id): deleted by the first listing of a later run.
                _logger.Error($"The folder {aside} of the removed product {productId} could not be deleted ({ex.Message}); it is deleted at the next start");
            }
        }


        // === Helpers ===

        private void ThrowIfClosed()
        {
            lock (_productsLock)
                if (_disposed)
                    throw new TableBatchStoreException("The table batch store is closed.");
        }

        // Under _productsLock: a removed product's slot. A read finds no product; a write is refused.
        private static ProductState Removed(Guid productId, bool create) =>
            create ? throw new TableBatchStoreException($"The table batches of product {productId} were removed with the product: nothing is written for it.") : null;

        // Closes every week database of a removed product once the reads that hold it end (Acquire opens none of them any
        // more). Without a flush: the folder is deleted next.
        private void CloseProductHandles(Guid productId)
        {
            lock (_cacheLock)
            {
                while (true)
                {
                    var handles = _handles.Values.Where(h => h.Key.Product == productId).ToList();
                    if (handles.Count == 0)
                        break;

                    foreach (var handle in handles.Where(h => h.Leases == 0))
                    {
                        handle.Db.Dispose();
                        _lru.Remove(handle.Node);
                        _handles.Remove(handle.Key);
                        Statistics.Add(ref Statistics.DatabasesClosed);
                    }

                    if (handles.Any(h => h.Leases > 0))
                    {
                        Checkpoint?.Invoke("product-remove-waiting");
                        Monitor.Wait(_cacheLock);
                    }
                }

                Monitor.PulseAll(_cacheLock);
            }
        }

        // Once per run, at the first listing of the products: the folders of removed products a crash or a failed delete
        // left. A delete that fails is logged and tried again in the next run.
        private void DeleteRemovedFolders()
        {
            if (Interlocked.Exchange(ref _removedFoldersDeleted, 1) != 0)
                return;

            foreach (var dir in Directory.GetDirectories(_root))
            {
                var name = Path.GetFileName(dir);
                var at = name.IndexOf(RemovedFolderSuffix, StringComparison.Ordinal);

                if (at < 0 || !Guid.TryParseExact(name[..at], "N", out _))
                    continue;

                try
                {
                    DeleteFolder(dir);
                    _logger.Info($"The folder {dir} of a removed product was deleted");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Error($"The folder {dir} of a removed product could not be deleted ({ex.Message}); it is tried again at the next start");
                }
            }
        }

        // Under _removalsLock. Reads the file once; one that cannot be read throws, and is read again by the next call.
        private void LoadRemovals()
        {
            if (_sensorMarks is not null)
                return;

            var sensors = new Dictionary<Guid, TableRemoval>();
            var products = new Dictionary<Guid, TableRemoval>();
            var path = Path.Combine(_root, RemovedName);

            if (File.Exists(path))
            {
                string[] lines;
                try
                {
                    lines = ReadRemovedLines(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Statistics.Add(ref Statistics.ReadFailures);
                    throw new TableBatchStoreException($"The record of removed table sensors and products could not be read: {ex.Message}", ex);
                }

                foreach (var line in lines)
                {
                    var fields = line.Split(' ');

                    if (fields.Length == 3 && fields[0] == "S" && Guid.TryParseExact(fields[1], "N", out var sensor) && Guid.TryParseExact(fields[2], "N", out var product))
                        sensors[sensor] = new TableRemoval(product, sensor, Earlier: true);
                    else if (fields.Length == 2 && fields[0] == "P" && Guid.TryParseExact(fields[1], "N", out var root))
                        products[root] = new TableRemoval(root, null, Earlier: true);
                    else if (line.Length > 0)
                        _logger.Error($"The record of removed table sensors and products has a line that is not a removal: '{line}'; skipped (nothing is deleted for it)");
                }
            }

            (_sensorMarks, _productMarks) = (sensors, products);
        }

        // Under _removalsLock.
        private void WriteRemovals(Dictionary<Guid, TableRemoval> sensors, Dictionary<Guid, TableRemoval> products)
        {
            var path = Path.Combine(_root, RemovedName);
            var text = string.Concat(products.Values.OrderBy(m => m.ProductId).Select(m => $"P {m.ProductId:N}\n")
                .Concat(sensors.Values.OrderBy(m => m.SensorId).Select(m => $"S {m.SensorId:N} {m.ProductId:N}\n")));

            try
            {
                Checkpoint?.Invoke("removals-writing");
                Directory.CreateDirectory(_root);

                if (text.Length > 0)
                    DurableFile.WriteAtomic(path, text, strict: true);
                else if (File.Exists(path))
                    DurableFile.Delete(path, strict: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Statistics.Add(ref Statistics.WriteFailures);
                throw new TableBatchStoreException($"The record of removed table sensors and products could not be written: {ex.Message}", ex);
            }
        }
    }
}
