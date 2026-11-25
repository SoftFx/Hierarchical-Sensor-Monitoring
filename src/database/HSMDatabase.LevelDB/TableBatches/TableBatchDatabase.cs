using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.Sealed;
using LevelDB;
using NLog;

namespace HSMDatabase.LevelDB.TableBatches
{
    /// <summary>
    /// The batch store of table sensors (#1850; layout in <c>aicontext/features/storage/table-batches/feature.md</c>).
    /// <para>
    /// Under <c>&lt;Tables&gt;/BatchesV1/&lt;productId&gt;/</c>: one LevelDB database per Monday-aligned week of the merge
    /// hour and per retention period class, <c>&lt;yyyyMMdd&gt;_P&lt;weeks&gt;</c>, and <c>Hours</c>, the product's merged
    /// hours. In a week database every key starts with the merge hour (index records, batches, the hour's mark), except
    /// the region of moved batches, which starts with 0xFF and so lies after every hour key: ingest and a move never write
    /// into each other's range. A merged hour is committed by its record in <c>Hours</c>, written after its parts.
    /// </para>
    /// <para>
    /// Week databases are opened through a bounded cache (<see cref="MaxOpenDatabases"/>, least recently used closed first).
    /// The databases of a product's newest week are pinned while it is written; when a later week starts they are flushed
    /// (no log left to replay at the next open) and become ordinary cached databases. A read holds a database while it reads
    /// it and lets it go before the next week.
    /// </para>
    /// </summary>
    public sealed class TableBatchDatabase : ITableBatchStore, IDisposable
    {
        public const string FolderName = "BatchesV1";
        public const string HoursName = "Hours";

        /// <summary>
        /// The product's record of week databases retention dropped, one name (<c>&lt;yyyyMMdd&gt;_P&lt;weeks&gt;</c>) per
        /// line, written durably before a folder is deleted. Outside <c>Hours</c>, so it survives the loss of <c>Hours</c>.
        /// </summary>
        public const string DroppedName = "Dropped";

        internal const byte TagIndex = 0x00;
        internal const byte TagBatch = 0x01;
        internal const byte TagMark = 0x02;
        internal const byte MovedRegion = 0xFF;

        private const byte IndexVersion = 1;
        private const byte CommitVersion = 1;

        public const int DefaultMaxOpenDatabases = 32;

        // A key below the region of moved batches and above every hour key: a compaction of [FlushKey, FlushKey] has no file
        // to merge and only writes the memtable out (LevelDB flushes the memtable at the start of every CompactRange).
        private static readonly byte[] FlushKey = [0xFE];

        private static readonly WriteOptions _syncWrite = new() { Sync = true };
        private static readonly Logger _logger = LogManager.GetLogger("TableBatches");

        private readonly string _root;
        private readonly long _writeBufferSize;
        private readonly Func<string, Options, DB> _open;
        private readonly Action<TimeSpan> _pause;

        // The products by id. _productsLock guards the dictionary and each slot's State; a product is opened under its slot's
        // own gate, outside _productsLock, so the open of one product (a rebuild of its Hours opens every week database)
        // never holds up the calls on the others.
        private readonly object _productsLock = new();
        private readonly Dictionary<Guid, ProductSlot> _products = [];

        // The cache of open week databases. Every handle is changed under _cacheLock; a read or write uses a database while
        // holding a lease on its handle, outside the lock.
        private readonly object _cacheLock = new();
        private readonly Dictionary<(Guid Product, long Week, int Period), Handle> _handles = [];
        private readonly LinkedList<Handle> _lru = new();

        // Week databases Acquire treats as absent (a read finds nothing, a write is a fault): while a drop closes and deletes
        // them, and after a failed delete until the next open of the product deletes the folder left. Under _cacheLock.
        private readonly HashSet<(Guid Product, long Week, int Period)> _dropping = [];

        private int _maxOpen = DefaultMaxOpenDatabases;
        private bool _disposed;


        public TableBatchDatabase(string tablesFolder) : this(tablesFolder, 8L * 1024 * 1024) { }

        internal TableBatchDatabase(string tablesFolder, long writeBufferSize, Func<string, Options, DB> open = null, Action<TimeSpan> pause = null)
        {
            _root = Path.Combine(tablesFolder, FolderName);
            _writeBufferSize = writeBufferSize;
            _open = open;
            _pause = pause;
        }


        /// <summary>The clock of commit times. Test seam.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>
        /// Called at the points a test stands a crash or a pause at: "part-written", "stale-removed" (after each part of a
        /// class the record does not name is deleted), "before-commit", "committed" (the hour write); "flush" (before a week's
        /// memtable is written out); "read-holding" (a read holds a database and its iterator); "drop-waiting" (a drop waits
        /// for the leases of the week, under the cache lock), "drop-deleting" (before the folder is deleted), "drop-hours"
        /// (before the hours left without a class are removed from Hours); "dispose-waiting" (a dispose waits for leases,
        /// under the cache lock), "dispose-product" (before a dispose takes a product's lock); "product-opening" (a product's
        /// open, under its slot's gate and outside the store-wide lock).
        /// </summary>
        internal Action<string> Checkpoint { get; set; }

        /// <summary>Deletes a dropped week's folder. Test seam for a delete that fails.</summary>
        internal Action<string> DeleteFolder { get; set; } = path => Directory.Delete(path, recursive: true);

        public string Folder => _root;

        public bool IsOpened { get { lock (_productsLock) return _products.Values.Any(s => s.State is not null); } }

        public TableBatchStoreStatistics Statistics { get; } = new();

        public TableMergeStatistics MergeStatistics { get; } = new();

        public int MaxOpenDatabases
        {
            get => Volatile.Read(ref _maxOpen);
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "at least one database");

                lock (_cacheLock)
                {
                    _maxOpen = value;
                    EvictOverBound();
                }
            }
        }

        public int OpenDatabases { get { lock (_cacheLock) return _handles.Count; } }

        public int WeekDatabases
        {
            get
            {
                // The products are copied under the store-wide lock and visited outside it: a product's lock held long (a
                // synced write, a drop waiting for its reads) must not stall every other call through _productsLock.
                List<ProductState> products;
                lock (_productsLock)
                    products = _products.Values.Select(s => s.State).Where(p => p is not null).ToList();

                return products.Sum(p => { lock (p.Lock) return p.Weeks.Count; });
            }
        }


        // === Writing ===

        public void WriteHour(Guid productId, TableBatchHourCommit commit, IReadOnlyList<TableBatchHourPart> parts)
        {
            ArgumentNullException.ThrowIfNull(commit);
            ArgumentNullException.ThrowIfNull(parts);

            if (parts.Select(p => p.PeriodWeeks).Distinct().Count() != parts.Count || parts.Any(p => p.PeriodWeeks < 0 || p.PeriodWeeks > ushort.MaxValue))
                throw new ArgumentException($"One part per period class, a period of 0 (forever) to {ushort.MaxValue} weeks.", nameof(parts));

            long hour = commit.HourTicks;
            long week = StorageWeek.GetStartTicks(hour);

            if (hour % TimeSpan.TicksPerHour != 0)
                throw new ArgumentException("The merge hour starts on the hour.", nameof(commit));

            var product = Product(productId, create: true);

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                if (product.Hours.ContainsKey(hour))
                    return;

                var ordered = parts.OrderBy(p => p.PeriodWeeks).ToList();
                var classes = ordered.Select(p => new TableBatchClassRange(p.PeriodWeeks,
                    p.Batches.Count == 0 ? 0 : p.Batches.Min(b => b.FirstTime), p.Batches.Count == 0 ? 0 : p.Batches.Max(b => b.LastTime),
                    p.Batches.Count, p.Batches.Sum(b => (long)b.RowCount))).ToList();
                // The commit time: the deletion delay of the hour's blocks runs from it, also across a restart.
                long commitTicks = Clock().Ticks;
                byte[] record;

                try
                {
                    RefuseDropped(product, ordered.Select(p => new TableBatchWeek(week, p.PeriodWeeks)));
                    PinWeek(product, week);
                    // Encoded once: every mark this run writes is the same bytes, the commit time included, so the marks of
                    // two runs never compare equal (Rebuild).
                    record = EncodeCommit(commit, classes, commitTicks);

                    foreach (var part in ordered)
                    {
                        var key = (productId, week, part.PeriodWeeks);
                        var handle = Acquire(key, create: true, pin: week >= product.PinnedWeek);

                        try
                        {
                            product.Weeks.Add(new TableBatchWeek(week, part.PeriodWeeks));

                            // A mark of the hour here was written by a run that did not reach the commit (a committed hour
                            // returned above). The part is always written again, whatever that run wrote: its metadata cannot
                            // tell which sensors' rows it holds. The hour's keys of this database are deleted and the new ones
                            // and the mark written in one write.
                            var rewrite = handle.Db.Get(HourKey(hour, TagMark)) is not null;

                            long bytes = 0;
                            using var batch = new WriteBatch();

                            if (rewrite)
                                DeleteHourKeys(handle.Db, hour, batch);

                            for (int i = 0; i < part.Batches.Count; i++)
                            {
                                var b = part.Batches[i];
                                batch.Put(IndexKey(hour, b.SensorId, b.FirstTime, i), EncodeIndex(b));
                                batch.Put(BatchKey(hour, i), b.Batch);
                                bytes += b.Batch.Length;
                            }

                            batch.Put(HourKey(hour, TagMark), record);
                            handle.Db.Write(batch, _syncWrite);
                            handle.Dirty = true;

                            if (rewrite)
                            {
                                Statistics.Add(ref Statistics.PartsRewritten);
                                _logger.Warn($"The table batches of hour {new DateTime(hour, DateTimeKind.Utc):O} in class P{part.PeriodWeeks} of product {productId}, " +
                                             "left by a merge that did not commit, were written again");
                            }

                            Statistics.Add(ref Statistics.BatchesWritten, part.Batches.Count);
                            Statistics.Add(ref Statistics.BytesWritten, bytes);
                        }
                        finally
                        {
                            Release(handle);
                        }

                        Checkpoint?.Invoke("part-written");
                    }

                    // After every part of the record is written: a run that did not reach the commit may have left a part of
                    // this hour in a class the record does not name (a sensor's period changed in between). Removed last, so a
                    // crash at any point leaves either marks of two runs (not committed by Rebuild) or only marks of this
                    // record over its complete parts.
                    RemoveStaleParts(product, week, hour, ordered.Select(p => p.PeriodWeeks).ToHashSet());

                    Checkpoint?.Invoke("before-commit");
                    product.HoursDb.Put(CommitKey(hour), record, _syncWrite);
                }
                catch (Exception ex) when (ex is not TableBatchStoreException and not CheckpointException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The merged hour {new DateTime(hour, DateTimeKind.Utc):O} of product {productId} could not be written: {ex.Message}", ex);
                }

                product.Hours[hour] = new TableBatchHour(hour, classes, commit.FirstBlockSequence, commit.LastBlockSequence, commit.BlocksMerged, commit.UnreadableBlocks.ToArray(), commitTicks);
                product.Snapshot = null;

                RaiseWatermarks(product, commit.Watermarks);

                Statistics.Add(ref Statistics.HoursCommitted);
                Checkpoint?.Invoke("committed");
            }
        }

        // Under the product's lock. Deletes the part of the hour (index records, batches, mark) from every week database of the
        // product's week whose class is not among the given ones and that holds a mark of the hour. One synced write each,
        // pinned as a part write is, so the newest week's database is flushed when the next week starts.
        private void RemoveStaleParts(ProductState product, long week, long hour, HashSet<int> classes)
        {
            foreach (var stale in product.Weeks.Where(w => w.WeekStartTicks == week && !classes.Contains(w.PeriodWeeks)).ToList())
            {
                var handle = Acquire((product.Id, week, stale.PeriodWeeks), create: false, pin: week >= product.PinnedWeek);
                if (handle is null)
                    continue;

                try
                {
                    if (handle.Db.Get(HourKey(hour, TagMark)) is null)
                        continue;

                    using var batch = new WriteBatch();
                    DeleteHourKeys(handle.Db, hour, batch);
                    handle.Db.Write(batch, _syncWrite);
                    handle.Dirty = true;
                    Statistics.Add(ref Statistics.PartsRewritten);
                    _logger.Warn($"The table batches of hour {new DateTime(hour, DateTimeKind.Utc):O} in class P{stale.PeriodWeeks} of product {product.Id}, " +
                                 "left by a merge that did not commit and not in the class list of the repeat, were deleted");
                }
                finally
                {
                    Release(handle);
                }

                Checkpoint?.Invoke("stale-removed");
            }
        }

        // Adds to the batch a delete of every key of the hour region of one hour (index records, batches, the mark). The region
        // of moved batches starts with 0xFF and is not touched.
        private static void DeleteHourKeys(DB db, long hour, WriteBatch batch)
        {
            var prefix = HourKey(hour, TagIndex)[..8];

            using var iterator = db.CreateIterator();
            for (iterator.Seek(prefix); iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix); iterator.Next())
                batch.Delete(iterator.Key());
        }

        /// <summary>
        /// Writes batches of one sensor and merge hour into the region of moved batches of a week database (the move of a
        /// sensor to another period class, #1850 PR 4, writes there: after every hour key, so it never overlaps ingest).
        /// One synced write. Nothing is committed: the hour's commit is the merge's.
        /// </summary>
        internal void WriteMoved(Guid productId, TableBatchWeek week, long hour, IReadOnlyList<TableBatchToWrite> batches)
        {
            var product = Product(productId, create: true);

            lock (product.Lock)
            {
                RefuseDropped(product, [week]);
                var handle = Acquire((productId, week.WeekStartTicks, week.PeriodWeeks), create: true, pin: false);

                try
                {
                    product.Weeks.Add(week);
                    product.Snapshot = null;

                    using var batch = new WriteBatch();
                    for (int i = 0; i < batches.Count; i++)
                    {
                        var b = batches[i];
                        batch.Put(MovedIndexKey(b.SensorId, hour, b.FirstTime, i), EncodeIndex(b));
                        batch.Put(MovedBatchKey(b.SensorId, hour, i), b.Batch);
                    }

                    handle.Db.Write(batch, _syncWrite);
                    handle.Dirty = true;
                }
                finally
                {
                    Release(handle);
                }
            }
        }

        // Under the product's lock. The databases of a week before the newest one written are flushed and unpinned: that
        // week is closed, and a database closed later has no log to replay.
        private void PinWeek(ProductState product, long week)
        {
            if (week <= product.PinnedWeek)
                return;

            List<Handle> closed;
            lock (_cacheLock)
            {
                closed = _handles.Values.Where(h => h.Key.Product == product.Id && h.Pinned && h.Key.Week < week).ToList();
                foreach (var handle in closed)
                    handle.Leases++;
            }

            try
            {
                foreach (var handle in closed)
                    Flush(handle);
            }
            finally
            {
                // Every lease taken is released, also after a failed flush: a drop or a dispose waits for them.
                lock (_cacheLock)
                {
                    foreach (var handle in closed)
                    {
                        handle.Pinned = false;
                        handle.Leases--;
                    }

                    EvictOverBound();
                    Monitor.PulseAll(_cacheLock);
                }
            }

            product.PinnedWeek = week;
        }

        private void Flush(Handle handle)
        {
            if (!handle.Dirty)
                return;

            Checkpoint?.Invoke("flush");
            handle.Db.CompactRange(FlushKey, FlushKey);
            handle.Dirty = false;
            Statistics.Add(ref Statistics.WeeksFlushed);
        }


        // === Reading ===

        public IReadOnlyList<TableBatchHour> GetHours(Guid productId)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return [];

            lock (product.Lock)
                return product.Snapshot ??= product.Hours.Values.Select(h => Visible(product, h)).Where(h => h.Classes.Count > 0).ToArray();
        }

        public bool IsHourMerged(Guid productId, long hourTicks)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return false;

            lock (product.Lock)
                return product.Hours.ContainsKey(hourTicks);
        }

        public TableBatchHour GetHour(Guid productId, long hourTicks)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return null;

            lock (product.Lock)
                return product.Hours.TryGetValue(hourTicks, out var hour) ? hour : null;
        }

        public long? GetNewestHour(Guid productId)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return null;

            lock (product.Lock)
                return product.Hours.Count == 0 ? null : product.Hours.Keys.Last();
        }

        public IReadOnlyDictionary<Guid, long> GetWatermarks(Guid productId)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return new Dictionary<Guid, long>();

            lock (product.Lock)
                return product.Watermarks;
        }

        public IReadOnlyList<Guid> GetProducts()
        {
            if (!Directory.Exists(_root))
                return [];

            return Directory.GetDirectories(_root).Select(Path.GetFileName).Select(n => Guid.TryParseExact(n, "N", out var id) ? id : Guid.Empty)
                            .Where(id => id != Guid.Empty).OrderBy(id => id).ToArray();
        }

        public IReadOnlyList<TableBatchWeek> GetWeeks(Guid productId)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return [];

            lock (product.Lock)
                return product.Weeks.OrderBy(w => w.WeekStartTicks).ThenBy(w => w.PeriodWeeks).ToArray();
        }

        public IReadOnlyList<TableBatchRead> ReadSensor(Guid productId, Guid sensorId, int periodWeeks, IReadOnlyCollection<long> hours)
        {
            ArgumentNullException.ThrowIfNull(hours);

            var result = new List<TableBatchRead>();
            if (hours.Count == 0 || Product(productId, create: false) is null)
                return result;

            foreach (var week in hours.Distinct().GroupBy(StorageWeek.GetStartTicks).OrderBy(g => g.Key))
            {
                var handle = Acquire((productId, week.Key, periodWeeks), create: false, pin: false);
                if (handle is null)
                    continue;

                try
                {
                    var selected = week.OrderBy(h => h).ToArray();
                    var found = new List<TableBatchRead>();

                    using (var iterator = handle.Db.CreateIterator())
                    {
                        Checkpoint?.Invoke("read-holding");

                        foreach (var hour in selected)
                        {
                            var prefix = IndexPrefix(hour, sensorId);
                            for (iterator.Seek(prefix); iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix); iterator.Next())
                                found.Add(ReadIndexed(handle.Db, iterator.Key(), iterator.Value(), moved: false));
                        }

                        // The region of moved batches: 0xFF | sensor | hour | tag | ...
                        var movedPrefix = MovedPrefix(sensorId);
                        var hourSet = new HashSet<long>(selected);
                        for (iterator.Seek(MovedPrefix(sensorId, selected[0])); iterator.IsValid && iterator.Key().AsSpan().StartsWith(movedPrefix); iterator.Next())
                        {
                            var key = iterator.Key();
                            long hour = BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(17));
                            if (hour > selected[^1])
                                break;
                            if (key[25] == TagIndex && hourSet.Contains(hour))
                                found.Add(ReadIndexed(handle.Db, key, iterator.Value(), moved: true));
                        }
                    }

                    result.AddRange(found.Where(f => f is not null));
                }
                finally
                {
                    Release(handle);
                }
            }

            return result.OrderBy(r => r.HourTicks).ThenBy(r => r.Moved).ThenBy(r => r.Ordinal).ToList();
        }

        // An index record and its batch; null (counted) when the batch is missing or the record is damaged.
        private TableBatchRead ReadIndexed(DB db, byte[] key, byte[] value, bool moved)
        {
            try
            {
                Guid sensor;
                long hour, first;
                int ordinal;
                byte[] batchKey;

                if (moved)
                {
                    sensor = new Guid(key.AsSpan(1, 16), bigEndian: true);
                    hour = BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(17));
                    first = BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(26));
                    ordinal = BinaryPrimitives.ReadInt32BigEndian(key.AsSpan(34));
                    batchKey = MovedBatchKey(sensor, hour, ordinal);
                }
                else
                {
                    hour = BinaryPrimitives.ReadInt64BigEndian(key);
                    sensor = new Guid(key.AsSpan(9, 16), bigEndian: true);
                    first = BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(25));
                    ordinal = BinaryPrimitives.ReadInt32BigEndian(key.AsSpan(33));
                    batchKey = BatchKey(hour, ordinal);
                }

                if (value.Length != 17 || value[0] != IndexVersion)
                    throw new FormatException($"an index record of {value.Length} bytes, version {(value.Length > 0 ? value[0] : 0)}");

                var batch = db.Get(batchKey) ?? throw new FormatException("the batch of an index record is missing");

                return new TableBatchRead(sensor, hour, ordinal, moved, first, BinaryPrimitives.ReadInt64LittleEndian(value.AsSpan(1)),
                    BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(9)), batch);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                Statistics.Add(ref Statistics.ReadFailures);
                _logger.Error($"A table batch record could not be read: {ex.Message}");
                return null;
            }
        }


        // === Retention ===

        public void DropWeek(Guid productId, TableBatchWeek week)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return;

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                var key = (productId, week.WeekStartTicks, week.PeriodWeeks);
                var path = WeekPath(productId, week.WeekStartTicks, week.PeriodWeeks);

                if (!product.Weeks.Contains(week) && !Directory.Exists(path))
                    return;

                bool leftover = false;
                try
                {
                    lock (_cacheLock)
                    {
                        // From here on Acquire treats the week as absent: no read opens it again, the leases held only end.
                        _dropping.Add(key);

                        // A reader's Release may close the handle itself (over the bound), so it is looked up after every wait.
                        while (_handles.TryGetValue(key, out var handle))
                        {
                            if (handle.Leases == 0)
                            {
                                Close(handle);
                                break;
                            }

                            Checkpoint?.Invoke("drop-waiting");
                            Monitor.Wait(_cacheLock);
                        }
                    }

                    // Durable before the folder goes: a rebuild of Hours tells a dropped class from a part never written by it.
                    try
                    {
                        RecordDropped(product, week);
                    }
                    catch (Exception ex) when (ex is not TableBatchStoreException and not CheckpointException)
                    {
                        Statistics.Add(ref Statistics.WriteFailures);
                        throw new TableBatchStoreException($"The drop of the table batch database {path} could not be recorded: {ex.Message}", ex);
                    }

                    Exception failure = null;
                    try
                    {
                        Checkpoint?.Invoke("drop-deleting");
                        if (Directory.Exists(path))
                            DeleteFolder(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (Directory.Exists(path))
                        {
                            failure = ex;
                            leftover = true;
                        }
                    }

                    // Dropped by the record, whether the folder went or not: a folder left is deleted at the next open.
                    product.Weeks.Remove(week);
                    product.Snapshot = null;

                    // Hours of the week left without any database are removed from the hours database.
                    var weekEnd = week.WeekStartTicks + 7 * TimeSpan.TicksPerDay;
                    using var batch = new WriteBatch();
                    var removed = new List<long>();

                    foreach (var hour in product.Hours.Values.Where(h => h.HourTicks >= week.WeekStartTicks && h.HourTicks < weekEnd))
                        if (Visible(product, hour).Classes.Count == 0)
                        {
                            batch.Delete(CommitKey(hour.HourTicks));
                            removed.Add(hour.HourTicks);
                        }

                    if (removed.Count > 0)
                    {
                        try
                        {
                            Checkpoint?.Invoke("drop-hours");
                            product.HoursDb.Write(batch, _syncWrite);
                        }
                        catch (Exception ex) when (ex is not TableBatchStoreException and not CheckpointException)
                        {
                            // The drop is recorded and the week out of the lists: the hours stay in memory and in Hours with no
                            // class left, hidden by Visible, and the next open loads them the same way.
                            Statistics.Add(ref Statistics.WriteFailures);
                            throw new TableBatchStoreException($"The hours of the dropped table batch database {path} could not be removed from the merged hours: {ex.Message}", ex);
                        }
                    }

                    foreach (var hour in removed)
                        product.Hours.Remove(hour);

                    if (failure is not null)
                    {
                        Statistics.Add(ref Statistics.WriteFailures);
                        _logger.Error($"The folder of the dropped table batch database {path} could not be deleted ({failure.Message}); it is deleted at the next open of the product");
                        throw new TableBatchStoreException($"The folder of the dropped table batch database {path} could not be deleted: {failure.Message}", failure);
                    }

                    Statistics.Add(ref Statistics.WeeksDropped);
                }
                finally
                {
                    // A folder left by a failed delete stays absent to Acquire until the next open of the product deletes it.
                    if (!leftover)
                        lock (_cacheLock)
                            _dropping.Remove(key);
                }
            }
        }

        // Under the product's lock. Rewrites the product's record of dropped week databases with the given one added. An
        // entry is kept while a database of its week is left (a mark there may name its class) or its own folder is still on
        // disk; the others go, so the record holds at most the dropped classes of the weeks still on disk.
        private void RecordDropped(ProductState product, TableBatchWeek week)
        {
            var entries = product.Dropped.Append(week).Distinct()
                .Where(e => e == week || product.Weeks.Any(w => w.WeekStartTicks == e.WeekStartTicks) ||
                            Directory.Exists(WeekPath(product.Id, e.WeekStartTicks, e.PeriodWeeks)))
                .OrderBy(e => e.WeekStartTicks).ThenBy(e => e.PeriodWeeks).ToList();

            var text = string.Concat(entries.Select(e => WeekName(e.WeekStartTicks, e.PeriodWeeks) + "\n"));
            DurableFile.WriteAtomic(Path.Combine(ProductPath(product.Id), DroppedName), text, strict: true);

            product.Dropped = [.. entries];
        }

        private HashSet<TableBatchWeek> ReadDropped(Guid productId, string folder)
        {
            var path = Path.Combine(folder, DroppedName);
            var dropped = new HashSet<TableBatchWeek>();

            if (!File.Exists(path))
                return dropped;

            foreach (var line in File.ReadAllLines(path))
                if (TryParseWeek(line, out var week))
                    dropped.Add(week);
                else if (line.Length > 0)
                    _logger.Error($"The record of dropped table batch databases of product {productId} has a line that is not a week database: '{line}'; skipped");

            return dropped;
        }

        // Under the product's lock. A write into a week database retention dropped is refused: its period has passed, and a
        // database created again would hold hours the record says lost their class.
        private void RefuseDropped(ProductState product, IEnumerable<TableBatchWeek> weeks)
        {
            foreach (var week in weeks)
                if (product.Dropped.Contains(week))
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The table batch database {WeekName(week.WeekStartTicks, week.PeriodWeeks)} of product {product.Id} " +
                                                       "was dropped by retention: nothing is written into it.");
                }
        }

        // The hour with only the classes whose week database exists.
        private static TableBatchHour Visible(ProductState product, TableBatchHour hour)
        {
            var week = StorageWeek.GetStartTicks(hour.HourTicks);
            if (hour.Classes.All(c => product.Weeks.Contains(new TableBatchWeek(week, c.PeriodWeeks))))
                return hour;

            return hour with { Classes = hour.Classes.Where(c => product.Weeks.Contains(new TableBatchWeek(week, c.PeriodWeeks))).ToArray() };
        }


        // === Products ===

        // The product's state, opened on first use. Two calls for the same product get one open: the second waits on the
        // slot's gate and takes the first one's state. An open that fails leaves the slot empty, so the next call opens again
        // (unlike a Lazy, which would keep the exception until a restart). Only the slot is looked up under _productsLock.
        private ProductState Product(Guid productId, bool create)
        {
            ProductSlot slot;

            lock (_productsLock)
            {
                if (_disposed)
                    throw new TableBatchStoreException("The table batch store is closed.");

                if (_products.TryGetValue(productId, out slot) && slot.State is { } open)
                    return open;

                if (slot is null)
                    _products[productId] = slot = new ProductSlot();
            }

            lock (slot.Gate)
            {
                lock (_productsLock)
                {
                    if (_disposed)
                        throw new TableBatchStoreException("The table batch store is closed.");

                    if (slot.State is { } opened)
                        return opened;
                }

                var folder = ProductPath(productId);
                if (!create && !Directory.Exists(folder))
                    return null;

                Checkpoint?.Invoke("product-opening");

                ProductState product;
                try
                {
                    product = OpenProduct(productId, folder);
                }
                catch (Exception ex) when (ex is not TableBatchStoreException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The table batches of product {productId} could not be opened: {ex.Message}", ex);
                }

                lock (_productsLock)
                {
                    if (!_disposed)
                    {
                        slot.State = product;
                        return product;
                    }
                }

                // The store was disposed while the product opened: its hours database is closed here, as the dispose (which
                // waits on this gate) would have closed it.
                product.HoursDb?.Dispose();
                product.HoursDb = null;
                throw new TableBatchStoreException("The table batch store is closed.");
            }
        }

        private ProductState OpenProduct(Guid productId, string folder)
        {
            var product = new ProductState(productId);

            try
            {
                return OpenProduct(product, folder);
            }
            catch
            {
                // Nothing of a product that failed to open stays open: the next call opens it again over the same folder.
                product.HoursDb?.Dispose();
                product.HoursDb = null;
                throw;
            }
        }

        private ProductState OpenProduct(ProductState product, string folder)
        {
            var productId = product.Id;
            var started = DateTime.UtcNow;
            Directory.CreateDirectory(folder);

            product.Dropped = ReadDropped(productId, folder);

            foreach (var dir in Directory.GetDirectories(folder))
                if (TryParseWeek(Path.GetFileName(dir), out var week))
                {
                    if (!product.Dropped.Contains(week))
                    {
                        product.Weeks.Add(week);
                        continue;
                    }

                    // Dropped, but a crash or a failed delete left its folder: deleted now; if it fails again, still absent.
                    try
                    {
                        DeleteFolder(dir);
                        _logger.Info($"The folder of the dropped table batch database {dir} was deleted at the open of product {productId}");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.Error($"The folder of the dropped table batch database {dir} could not be deleted ({ex.Message}); it is left until the next open");
                        lock (_cacheLock)
                            _dropping.Add((productId, week.WeekStartTicks, week.PeriodWeeks));
                    }
                }

            var hoursPath = Path.Combine(folder, HoursName);
            var existed = Directory.Exists(hoursPath);

            try
            {
                product.HoursDb = OpenDb(hoursPath, Options());
                LoadCommits(product);
            }
            catch (Exception ex) when (existed && (ex is FormatException || ex.Message?.Contains("Corruption", StringComparison.OrdinalIgnoreCase) == true))
            {
                // Derived from the marks in the week databases: put aside and rebuilt.
                product.HoursDb?.Dispose();
                var aside = $"{hoursPath}.damaged-{DateTime.UtcNow.Ticks}";
                Directory.Move(hoursPath, aside);
                _logger.Warn($"The merged hours of product {productId} could not be read ({ex.Message}); moved to {aside} and rebuilt from the week databases");

                product.HoursDb = OpenDb(hoursPath, Options());
                product.Hours.Clear();
                existed = false;
            }

            // Only a missing or damaged hours database is rebuilt; an empty one that exists is the truth (a first hour whose
            // commit was not written is not merged, whatever its parts hold).
            if (!existed && product.Weeks.Count > 0)
                Rebuild(product);

            _logger.Info($"Table batches of product {productId} opened in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms: " +
                         $"{product.Hours.Count} merged hours, {product.Weeks.Count} week databases");
            return product;
        }

        private static void LoadCommits(ProductState product)
        {
            using var iterator = product.HoursDb.CreateIterator();

            for (iterator.SeekToFirst(); iterator.IsValid; iterator.Next())
            {
                var key = iterator.Key();
                if (key.Length != 8)
                    throw new FormatException($"a merged hour key of {key.Length} bytes");

                var (hour, watermarks) = DecodeCommit(BinaryPrimitives.ReadInt64BigEndian(key), iterator.Value());
                product.Hours[hour.HourTicks] = hour;

                RaiseWatermarks(product, watermarks);
            }
        }

        // The watermark of a producer is the highest over every commit of the product, whatever the order the hours were
        // committed in: an hour merged after a later one may name a producer the later one does not. A new map each time,
        // since GetWatermarks hands the current one out.
        private static void RaiseWatermarks(ProductState product, IReadOnlyDictionary<Guid, long> watermarks)
        {
            Dictionary<Guid, long> raised = null;

            foreach (var (producer, sequence) in watermarks)
                if (!product.Watermarks.TryGetValue(producer, out var current) || sequence > current)
                {
                    raised ??= new Dictionary<Guid, long>(product.Watermarks);
                    raised[producer] = sequence;
                }

            if (raised is not null)
                product.Watermarks = raised;
        }

        // The hours database is missing or was damaged: every hour whose mark is in every week database its commit names
        // was committed (the parts are written before the commit, and each part holds the same record as its mark). A class
        // the record of dropped databases names for the hour's week counts as found: retention dropped it after the commit
        // (a write into a dropped database is refused, so its absence is never a part that was not written).
        private void Rebuild(ProductState product)
        {
            var marks = new Dictionary<long, (byte[] Record, HashSet<int> Found, bool Agree)>();

            foreach (var week in product.Weeks.OrderBy(w => w.WeekStartTicks).ThenBy(w => w.PeriodWeeks).ToList())
            {
                var handle = Acquire((product.Id, week.WeekStartTicks, week.PeriodWeeks), create: false, pin: false);
                if (handle is null)
                    continue;

                try
                {
                    for (int h = 0; h < 7 * 24; h++)
                    {
                        long hour = week.WeekStartTicks + h * TimeSpan.TicksPerHour;
                        var mark = handle.Db.Get(HourKey(hour, TagMark));
                        if (mark is null)
                            continue;

                        if (!marks.TryGetValue(hour, out var entry))
                            marks[hour] = entry = (mark, [], true);
                        else if (!entry.Record.AsSpan().SequenceEqual(mark))
                            marks[hour] = entry = (entry.Record, entry.Found, false);

                        entry.Found.Add(week.PeriodWeeks);
                    }
                }
                finally
                {
                    Release(handle);
                }
            }

            using var batch = new WriteBatch();
            int rebuilt = 0;

            // A mark holds the time its part was written, which may be earlier than the commit (a part written by a run that
            // did not commit). The rebuilt commit takes the time of the rebuild: the safe side, the blocks live a little longer.
            long rebuildTicks = Clock().Ticks;

            // Every mark of a committed hour is its record, byte for byte: one run encodes the record once and writes it as the
            // mark of each part, a repeat rewrites every part that has a mark, and the parts of classes the record does not name
            // are removed after the parts are written and before the commit. So an hour is rebuilt when its marks are the same
            // bytes (commit time included: two runs differ in it) and every class the record names has its mark or was dropped;
            // marks that differ are parts of two runs, a run that did not commit, and the hour stays on its blocks until the
            // merge repeats it. The weeks are visited by start, then class, for a fixed order.
            foreach (var (hour, (record, found, agree)) in marks.OrderBy(m => m.Key))
            {
                if (!agree)
                    continue;

                var (decoded, _) = DecodeCommit(hour, record);
                var week = StorageWeek.GetStartTicks(hour);
                if (!decoded.Classes.All(c => found.Contains(c.PeriodWeeks) || product.Dropped.Contains(new TableBatchWeek(week, c.PeriodWeeks))))
                    continue;

                var rebuiltRecord = (byte[])record.Clone();
                BinaryPrimitives.WriteInt64LittleEndian(rebuiltRecord.AsSpan(rebuiltRecord.Length - 8), rebuildTicks);
                batch.Put(CommitKey(hour), rebuiltRecord);
                rebuilt++;
            }

            if (rebuilt > 0)
                product.HoursDb.Write(batch, _syncWrite);

            LoadCommits(product);
            Statistics.Add(ref Statistics.HoursRebuilt, rebuilt);
            _logger.Warn($"The merged hours of product {product.Id} were rebuilt from the marks of {product.Weeks.Count} week databases: {rebuilt} hours");
        }


        // === The cache of week databases ===

        // Lock order: a product slot's gate (an open), then _productsLock or _cacheLock, each held briefly; a product's lock,
        // then _cacheLock. _productsLock is never taken under _cacheLock, and a product's lock never under a slot's gate.
        private Handle Acquire((Guid Product, long Week, int Period) key, bool create, bool pin)
        {
            lock (_cacheLock)
            {
                if (_disposed)
                    throw new TableBatchStoreException("The table batch store is closed.");

                if (_dropping.Contains(key))
                {
                    if (!create)
                        return null;

                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The table batch database {WeekPath(key.Product, key.Week, key.Period)} is being dropped.");
                }

                if (_handles.TryGetValue(key, out var handle))
                {
                    handle.Leases++;
                    handle.Pinned |= pin;
                    _lru.Remove(handle.Node);
                    _lru.AddFirst(handle.Node);
                    return handle;
                }

                var path = WeekPath(key.Product, key.Week, key.Period);
                if (!create && !Directory.Exists(path))
                    return null;

                DB db;
                try
                {
                    var options = Options();
                    options.CreateIfMissing = create;
                    db = OpenDb(path, options);
                }
                catch (Exception ex)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The table batch database {path} could not be opened: {ex.Message}", ex);
                }

                handle = new Handle(key, db) { Leases = 1, Pinned = pin };

                handle.Node = _lru.AddFirst(handle);
                _handles[key] = handle;
                Statistics.Add(ref Statistics.DatabasesOpened);

                EvictOverBound();
                return handle;
            }
        }

        private void Release(Handle handle)
        {
            lock (_cacheLock)
            {
                handle.Leases--;
                EvictOverBound();
                Monitor.PulseAll(_cacheLock);
            }
        }

        // Under _cacheLock. Closes the least recently used databases that nothing holds and nothing writes, until the count is
        // within the bound or none is left to close.
        private void EvictOverBound()
        {
            var node = _lru.Last;

            while (_handles.Count > _maxOpen && node is not null)
            {
                var previous = node.Previous;
                var handle = node.Value;

                if (handle.Leases == 0 && !handle.Pinned)
                    Close(handle);

                node = previous;
            }
        }

        // Under _cacheLock.
        private void Close(Handle handle)
        {
            try
            {
                Flush(handle);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Flushing the table batch database of week {new DateTime(handle.Key.Week):yyyy-MM-dd} failed before its close");
            }

            handle.Db.Dispose();
            _lru.Remove(handle.Node);
            _handles.Remove(handle.Key);
            Statistics.Add(ref Statistics.DatabasesClosed);
        }

        /// <summary>
        /// Closes every database without flushing a memtable, as a process that stops does: what is not flushed is in the
        /// databases' logs and is replayed at the next open. Test seam for a stop that is not a clean dispose.
        /// </summary>
        internal void CloseWithoutFlush()
        {
            lock (_productsLock)
            {
                lock (_cacheLock)
                {
                    _disposed = true;

                    foreach (var handle in _handles.Values.ToList())
                    {
                        handle.Db.Dispose();
                        _lru.Remove(handle.Node);
                        _handles.Remove(handle.Key);
                    }
                }

                foreach (var slot in _products.Values)
                    slot.State?.HoursDb?.Dispose();

                _products.Clear();
            }
        }

        /// <summary>
        /// Refuses every operation that starts from here on, then closes each week database once the reads and writes that
        /// hold it let it go, and each product's hours database under the product's lock (after a write in flight). Closing a
        /// database under a live iterator or a running write would free it under native code. Nothing is bounded by a timer:
        /// an operation in flight ends.
        /// </summary>
        public void Dispose()
        {
            List<ProductSlot> slots;

            lock (_productsLock)
            {
                if (_disposed)
                    return;

                lock (_cacheLock)
                    _disposed = true;

                slots = [.. _products.Values];
            }

            // An open in flight ends first (it sees the store closed and closes what it opened, or published its state
            // before the dispose began): its gate is taken once, after which the slot's state is final.
            var products = new List<ProductState>();
            foreach (var slot in slots)
                lock (slot.Gate)
                    lock (_productsLock)
                        if (slot.State is not null)
                            products.Add(slot.State);

            lock (_cacheLock)
            {
                while (_handles.Count > 0)
                {
                    foreach (var handle in _handles.Values.Where(h => h.Leases == 0).ToList())
                        Close(handle);

                    if (_handles.Count > 0)
                    {
                        Checkpoint?.Invoke("dispose-waiting");
                        Monitor.Wait(_cacheLock);
                    }
                }
            }

            foreach (var product in products)
            {
                Checkpoint?.Invoke("dispose-product");

                lock (product.Lock)
                {
                    product.HoursDb?.Dispose();
                    product.HoursDb = null;
                }
            }

            lock (_productsLock)
                _products.Clear();
        }


        // Through the shared retry of a transient file error at an open (LevelDbOpen), with a Warn line per retry.
        private DB OpenDb(string path, Options options) =>
            LevelDbOpen.Open(path, options, _open, _pause, ex => _logger.Warn($"Table batch database {path}: an open retried after a transient file error: {ex.Message}"));

        // === Paths, keys, values ===

        private string ProductPath(Guid productId) => Path.Combine(_root, productId.ToString("N"));

        internal string WeekPath(Guid productId, long weekStartTicks, int periodWeeks) =>
            Path.Combine(ProductPath(productId), WeekName(weekStartTicks, periodWeeks));

        internal static string WeekName(long weekStartTicks, int periodWeeks) =>
            $"{new DateTime(weekStartTicks):yyyyMMdd}_P{periodWeeks.ToString(CultureInfo.InvariantCulture)}";

        internal static bool TryParseWeek(string name, out TableBatchWeek week)
        {
            week = default;
            var parts = name.Split("_P");

            if (parts.Length != 2 || !DateTime.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var period) || start.DayOfWeek != DayOfWeek.Monday)
                return false;

            week = new TableBatchWeek(start.Ticks, period);
            return WeekName(week.WeekStartTicks, period) == name;
        }

        private Options Options() => new()
        {
            CreateIfMissing = true,
            CompressionLevel = CompressionLevel.NoCompression,
            WriteBufferSize = _writeBufferSize,
            // Week databases of a year stay within the process's handle budget: a closed week is read rarely.
            MaxOpenFiles = 100,
        };

        // hour | tag (the mark: no more)
        internal static byte[] HourKey(long hour, byte tag)
        {
            var key = new byte[9];
            BinaryPrimitives.WriteInt64BigEndian(key, hour);
            key[8] = tag;
            return key;
        }

        // hour | 0x00 | sensor (16, big-endian Guid bytes) | first time | ordinal (i32)
        internal static byte[] IndexKey(long hour, Guid sensor, long first, int ordinal)
        {
            var key = new byte[37];
            BinaryPrimitives.WriteInt64BigEndian(key, hour);
            key[8] = TagIndex;
            sensor.TryWriteBytes(key.AsSpan(9), bigEndian: true, out _);
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(25), first);
            BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(33), ordinal);
            return key;
        }

        private static byte[] IndexPrefix(long hour, Guid sensor) => IndexKey(hour, sensor, 0, 0)[..25];

        // hour | 0x01 | ordinal (i32)
        internal static byte[] BatchKey(long hour, int ordinal)
        {
            var key = new byte[13];
            BinaryPrimitives.WriteInt64BigEndian(key, hour);
            key[8] = TagBatch;
            BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(9), ordinal);
            return key;
        }

        // 0xFF | sensor | hour | 0x00 | first time | ordinal: the index record of a moved batch
        internal static byte[] MovedIndexKey(Guid sensor, long hour, long first, int ordinal)
        {
            var key = new byte[38];
            key[0] = MovedRegion;
            sensor.TryWriteBytes(key.AsSpan(1), bigEndian: true, out _);
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(17), hour);
            key[25] = TagIndex;
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(26), first);
            BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(34), ordinal);
            return key;
        }

        // 0xFF | sensor | hour | 0x01 | ordinal: a moved batch
        internal static byte[] MovedBatchKey(Guid sensor, long hour, int ordinal)
        {
            var key = new byte[30];
            key[0] = MovedRegion;
            sensor.TryWriteBytes(key.AsSpan(1), bigEndian: true, out _);
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(17), hour);
            key[25] = TagBatch;
            BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(26), ordinal);
            return key;
        }

        private static byte[] MovedPrefix(Guid sensor) => MovedBatchKey(sensor, 0, 0)[..17];

        private static byte[] MovedPrefix(Guid sensor, long hour) => MovedBatchKey(sensor, hour, 0)[..25];

        private static byte[] CommitKey(long hour)
        {
            var key = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(key, hour);
            return key;
        }

        // u8 version, i64 last time, i32 rows, i32 stored length: 17 bytes.
        internal static byte[] EncodeIndex(TableBatchToWrite batch)
        {
            var value = new byte[17];
            value[0] = IndexVersion;
            BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(1), batch.LastTime);
            BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(9), batch.RowCount);
            BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(13), batch.Batch.Length);
            return value;
        }

        // The record of a merged hour: the commit in Hours and the mark in each of its week databases. Little-endian:
        // u8 version, i64 hour, i64 first block sequence, i64 last block sequence, i32 blocks merged,
        // u16 classes, per class (u16 period weeks, i64 min time, i64 max time, i32 batches, i64 rows),
        // i32 unreadable blocks, per block i64 sequence, i32 watermarks, per watermark (16 producer id, i64 sequence),
        // i64 commit time (UTC ticks; last, so that a rebuild can set it).
        internal static byte[] EncodeCommit(TableBatchHourCommit commit, IReadOnlyList<TableBatchClassRange> classes, long commitTicks)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(CommitVersion);
            writer.Write(commit.HourTicks);
            writer.Write(commit.FirstBlockSequence);
            writer.Write(commit.LastBlockSequence);
            writer.Write(commit.BlocksMerged);
            writer.Write(checked((ushort)classes.Count));
            foreach (var c in classes)
            {
                writer.Write(checked((ushort)c.PeriodWeeks));
                writer.Write(c.MinTime);
                writer.Write(c.MaxTime);
                writer.Write(c.Batches);
                writer.Write(c.Rows);
            }
            writer.Write(commit.UnreadableBlocks.Count);
            foreach (var s in commit.UnreadableBlocks)
                writer.Write(s);
            writer.Write(commit.Watermarks.Count);
            foreach (var (producer, sequence) in commit.Watermarks.OrderBy(w => w.Key))
            {
                writer.Write(producer.ToByteArray());
                writer.Write(sequence);
            }
            writer.Write(commitTicks);

            writer.Flush();
            return stream.ToArray();
        }

        internal static (TableBatchHour Hour, IReadOnlyDictionary<Guid, long> Watermarks) DecodeCommit(long hourKey, byte[] value)
        {
            try
            {
                using var reader = new BinaryReader(new MemoryStream(value));

                if (reader.ReadByte() != CommitVersion)
                    throw new FormatException($"merged hour record version {value[0]}");

                long hour = reader.ReadInt64();
                if (hour != hourKey)
                    throw new FormatException("the merged hour record is not the hour of its key");

                long first = reader.ReadInt64(), last = reader.ReadInt64();
                int blocks = reader.ReadInt32();
                var classes = new TableBatchClassRange[reader.ReadUInt16()];
                for (int i = 0; i < classes.Length; i++)
                    classes[i] = new TableBatchClassRange(reader.ReadUInt16(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt64());

                int unreadableCount = reader.ReadInt32();
                if (unreadableCount < 0 || unreadableCount > value.Length / 8)
                    throw new FormatException($"{unreadableCount} unreadable blocks");
                var unreadable = new long[unreadableCount];
                for (int i = 0; i < unreadable.Length; i++)
                    unreadable[i] = reader.ReadInt64();

                int watermarkCount = reader.ReadInt32();
                if (watermarkCount < 0 || watermarkCount > value.Length / 24)
                    throw new FormatException($"{watermarkCount} watermarks");
                var watermarks = new Dictionary<Guid, long>(watermarkCount);
                for (int i = 0; i < watermarkCount; i++)
                    watermarks[new Guid(reader.ReadBytes(16))] = reader.ReadInt64();

                long commitTicks = reader.ReadInt64();

                if (reader.BaseStream.Position != value.Length)
                    throw new FormatException("bytes after the merged hour record");

                return (new TableBatchHour(hour, classes, first, last, blocks, unreadable, commitTicks), watermarks);
            }
            catch (EndOfStreamException ex)
            {
                throw new FormatException("the merged hour record is cut short", ex);
            }
        }


        // A product's place in the store: Gate serialises its open, State is set (under _productsLock) once it opened.
        private sealed class ProductSlot
        {
            public readonly object Gate = new();
            public ProductState State;
        }

        private sealed class ProductState(Guid id)
        {
            public readonly Guid Id = id;
            public readonly object Lock = new();
            public readonly SortedDictionary<long, TableBatchHour> Hours = [];
            public readonly HashSet<TableBatchWeek> Weeks = [];
            public HashSet<TableBatchWeek> Dropped = [];
            public DB HoursDb;
            public long PinnedWeek = long.MinValue;
            public IReadOnlyDictionary<Guid, long> Watermarks = new Dictionary<Guid, long>();
            public IReadOnlyList<TableBatchHour> Snapshot;
        }


        private sealed class Handle((Guid Product, long Week, int Period) key, DB db)
        {
            public readonly (Guid Product, long Week, int Period) Key = key;
            public readonly DB Db = db;
            public int Leases;
            public bool Pinned;
            public bool Dirty;
            public LinkedListNode<Handle> Node;
        }
    }


    /// <summary>Thrown by a test's checkpoint to stand for a crash; never wrapped as a store fault.</summary>
    internal sealed class CheckpointException(string point) : Exception($"crash at {point}");
}
