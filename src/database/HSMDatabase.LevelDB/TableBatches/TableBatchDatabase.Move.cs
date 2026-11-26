using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.Sealed;
using LevelDB;

namespace HSMDatabase.LevelDB.TableBatches
{
    // The store's primitives of the move between period classes and of the removal of a sensor (#1850 parts 4c and 4d):
    // a sensor's hours copied into the region of moved batches of another class, deleted from a database, counted from the
    // index; the deleted-bytes counter of a week database and its compaction; the durable files of a product's compaction
    // list and of its move in flight. No job calls them yet (feature doc, "Moved batches and an hour merged again").
    public sealed partial class TableBatchDatabase
    {
        /// <summary>
        /// The product's step of the move in flight (#1850 part 4c): a text file of one line, written atomically
        /// (<see cref="DurableFile.WriteAtomic"/>, strict) and deleted when the step is done. A file and not a key in
        /// <c>Placements</c>: a rebuild of the placements deletes every key there, and an older build ignores the files of a
        /// product's folder.
        /// </summary>
        public const string MoveName = "Move";

        /// <summary>The product's week databases due for a compaction (#1850 part 4c): one name <c>&lt;yyyyMMdd&gt;_P&lt;weeks&gt;</c> per line, written as <see cref="DroppedName"/> is.</summary>
        public const string CompactName = "Compact";

        private const string MoveVersion = "1";

        // The deleted-bytes counter of a week database: i64 little-endian, the stored bytes of the batches deleted by
        // DeleteSensorHours and CopySensorHours since the last CompactWeek. Above every hour key (they start with 0x00..0x2B)
        // and below the region of moved batches (0xFF): no prefix any scan of a week database seeks (an hour's eight or nine
        // bytes, 0xFF | hour) starts it. Absent: 0.
        internal static readonly byte[] DeletedKey = [0xFE, (byte)'D'];

        // The bounds of a compaction of a whole week database: at or below every key (each starts with a byte of 0x00 or
        // more and is longer than one byte) and above every key (the region of moved batches is 0xFF | hour, whose first
        // byte is at most 0x2B).
        private static readonly byte[] FirstKey = [0x00];
        private static readonly byte[] LastKey = [0xFF, 0xFF];


        public TableBatchSensorHours CopySensorHours(Guid productId, Guid sensorId, TableBatchWeek from, int toClass, long firstHour, long endHour)
        {
            CheckHours(from, firstHour, endHour);
            if (toClass < 0 || toClass > ushort.MaxValue || toClass == from.PeriodWeeks)
                throw new ArgumentException($"A copy goes into another period class of 0 to {ushort.MaxValue} weeks, not P{toClass} from P{from.PeriodWeeks}.", nameof(toClass));

            var to = new TableBatchWeek(from.WeekStartTicks, toClass);

            // A product without a folder holds nothing to copy, and its target nothing to delete.
            var product = Product(productId, create: false);
            if (product is null)
                return default;

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                try
                {
                    // A refusal, not a fault: a repeat cannot write into a database retention dropped (its period has
                    // passed: the caller deletes the source as expired instead).
                    if (product.Dropped.Contains(to))
                        throw new TableBatchRefusedException($"The table batch database {WeekName(to.WeekStartTicks, to.PeriodWeeks)} of product {productId} " +
                                                             "was dropped by retention: nothing is copied into it.");

                    // A copy never touches the target for an hour the source has nothing of: there, the target's moved rows
                    // may be the sensor's only copy (a step repeated after it completed: copied, switched, its source
                    // deleted). Only the hours the source holds records of are copied, each replacing the target's moved
                    // keys of the sensor of that hour; with none, nothing is written at all.
                    var hours = ReadSensorHours(product, sensorId, from, firstHour, endHour).Where(h => h.Records.Count > 0).ToList();
                    if (hours.Count == 0)
                        return default;

                    // Pinned as a part write is when it is the newest week: flushed when the next week starts.
                    var handle = Acquire((productId, to.WeekStartTicks, to.PeriodWeeks), create: true, pin: to.WeekStartTicks >= product.PinnedWeek);

                    try
                    {
                        // The target holds rows of the sensor of these hours in its hour region: either they are the
                        // sensor's rows there (the target is the class that holds the hour: a copy into it would make a
                        // read return them twice) or a source a move left behind. The store cannot tell which, so it
                        // refuses, with nothing written; the caller, which knows from the placement that the target is not
                        // the class of those hours, deletes them first (DeleteSensorHours). Never deleted here: in the class
                        // that holds the hour they are the only copy.
                        RefuseHourRegionRecords(handle.Db, productId, sensorId, to, hours.Select(h => h.Hour));

                        // A database created here is on disk from now on, whether or not the write below succeeds. It holds no
                        // row of another sensor (the source is a database of the same week, so the week had one already, and
                        // another sensor's rows of the week went into databases that existed then): only this sensor's
                        // generation changes, also for a database created (with nothing in the source, no database is
                        // created, nothing is written and no generation changes: returned above).
                        if (product.Weeks.Add(to))
                            product.Snapshot = null;

                        using var batch = new WriteBatch();
                        long deletedBytes = 0;
                        int keys = 0;
                        long batches = 0, rows = 0, bytes = 0;

                        using (var iterator = handle.Db.CreateIterator())
                            foreach (var hour in hours)
                            {
                                // Every moved key of the sensor of the hour (one the source holds records of) goes first: a repeat
                                // leaves exactly one copy, with the ordinals of this write, however the earlier copy was cut.
                                deletedBytes += DeleteMovedSensorKeys(iterator, sensorId, hour.Hour, batch, ref keys).Bytes;

                                for (int i = 0; i < hour.Records.Count; i++)
                                {
                                    var record = hour.Records[i];
                                    batch.Put(MovedIndexKey(sensorId, hour.Hour, record.FirstTime, i), record.Value);
                                    if (record.Batch is not null)
                                        batch.Put(MovedBatchKey(sensorId, hour.Hour, i), record.Batch);

                                    var (recordRows, recordBytes) = ReadIndexValue(record.Value);
                                    batches++;
                                    rows += recordRows;
                                    bytes += recordBytes;
                                    keys++;
                                }

                                // Moved batches of an hour never sit in a database without the hour's mark; a mark already
                                // there is the hour's own record, the same bytes.
                                if (handle.Db.Get(HourKey(hour.Hour, TagMark)) is null)
                                    batch.Put(HourKey(hour.Hour, TagMark), hour.Mark);
                            }

                        // Before the write: a database it created is on disk whether the write succeeds or not.
                        product.ChangeSensor(sensorId);

                        if (deletedBytes > 0)
                            batch.Put(DeletedKey, EncodeDeleted(ReadDeleted(handle.Db) + deletedBytes));

                        handle.Db.Write(batch, _syncWrite);
                        handle.Dirty = true;

                        Statistics.Add(ref Statistics.BatchesWritten, batches);
                        Statistics.Add(ref Statistics.BytesWritten, bytes);
                        Checkpoint?.Invoke("sensor-copied");
                        return new TableBatchSensorHours(batches, rows, bytes);
                    }
                    finally
                    {
                        Release(handle);
                    }
                }
                catch (Exception ex) when (ex is not TableBatchStoreException and not TableBatchRefusedException and not CheckpointException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The table batches of sensor {sensorId} of product {productId} could not be copied from " +
                                                       $"{WeekName(from.WeekStartTicks, from.PeriodWeeks)} into P{toClass}: {ex.Message}", ex);
                }
            }
        }

        public TableBatchSensorHours DeleteSensorHours(Guid productId, Guid sensorId, TableBatchWeek week, long firstHour, long endHour)
        {
            CheckHours(week, firstHour, endHour);

            var product = Product(productId, create: false);
            if (product is null)
                return default;

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                try
                {
                    var handle = Acquire((productId, week.WeekStartTicks, week.PeriodWeeks), create: false, pin: week.WeekStartTicks >= product.PinnedWeek);
                    if (handle is null)
                        return default;

                    try
                    {
                        using var batch = new WriteBatch();
                        int keys = 0, damaged = 0;
                        long batches = 0, rows = 0, bytes = 0;

                        using (var iterator = handle.Db.CreateIterator())
                            for (long hour = firstHour; hour < endHour; hour += TimeSpan.TicksPerHour)
                            {
                                // The hour region: the sensor's index records, and the batch each one names by its ordinal. A
                                // batch key of the hour region carries no sensor id, so no other key of it is touched.
                                var prefix = IndexPrefix(hour, sensorId);
                                for (iterator.Seek(prefix); iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix); iterator.Next())
                                {
                                    var key = iterator.Key();
                                    batch.Delete(key);
                                    keys++;

                                    // A key of another length names no ordinal: deleted all the same (it carries the sensor's
                                    // id), its batch, if any, left behind and not counted (the copy refuses such a key).
                                    if (key.Length == 37)
                                        batch.Delete(BatchKey(hour, BinaryPrimitives.ReadInt32BigEndian(key.AsSpan(33))));
                                    else
                                        damaged++;

                                    var (recordRows, recordBytes) = ReadIndexValue(iterator.Value());
                                    batches++;
                                    rows += recordRows;
                                    bytes += recordBytes;
                                }

                                // The region of moved batches: every key there carries the sensor's id.
                                var moved = DeleteMovedSensorKeys(iterator, sensorId, hour, batch, ref keys);
                                batches += moved.Batches;
                                rows += moved.Rows;
                                bytes += moved.Bytes;
                            }

                        if (keys == 0)
                            return default;

                        // The counter in the same write as the delete: it never misses bytes deleted, nor counts bytes that are not.
                        if (bytes > 0)
                            batch.Put(DeletedKey, EncodeDeleted(ReadDeleted(handle.Db) + bytes));

                        handle.Db.Write(batch, _syncWrite);
                        handle.Dirty = true;
                        product.ChangeSensor(sensorId);

                        if (damaged > 0)
                            _logger.Warn($"{damaged} index records of sensor {sensorId} in {WeekName(week.WeekStartTicks, week.PeriodWeeks)} of product {productId} had a damaged key " +
                                         "and were deleted without the batch they may name: such a batch stays, uncounted, until the database is dropped");

                        Checkpoint?.Invoke("sensor-deleted");
                        return new TableBatchSensorHours(batches, rows, bytes);
                    }
                    finally
                    {
                        Release(handle);
                    }
                }
                catch (Exception ex) when (ex is not TableBatchStoreException and not CheckpointException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The table batches of sensor {sensorId} of product {productId} could not be deleted from " +
                                                       $"{WeekName(week.WeekStartTicks, week.PeriodWeeks)}: {ex.Message}", ex);
                }
            }
        }

        public TableBatchSensorHours GetSensorHourBytes(Guid productId, Guid sensorId, TableBatchWeek week, long firstHour, long endHour)
        {
            CheckHours(week, firstHour, endHour);

            if (Product(productId, create: false) is null)
                return default;

            var handle = Acquire((productId, week.WeekStartTicks, week.PeriodWeeks), create: false, pin: false);
            if (handle is null)
                return default;

            try
            {
                long batches = 0, rows = 0, bytes = 0;
                using var iterator = handle.Db.CreateIterator();

                for (long hour = firstHour; hour < endHour; hour += TimeSpan.TicksPerHour)
                    foreach (var prefix in new[] { IndexPrefix(hour, sensorId), MovedIndexPrefix(hour, sensorId) })
                        for (iterator.Seek(prefix); iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix); iterator.Next())
                        {
                            var (recordRows, recordBytes) = ReadIndexValue(iterator.Value());
                            batches++;
                            rows += recordRows;
                            bytes += recordBytes;
                        }

                return new TableBatchSensorHours(batches, rows, bytes);
            }
            finally
            {
                Release(handle);
            }
        }


        // === The deleted-bytes counter and the compaction ===

        public TableBatchDeletedShare GetDeletedShare(Guid productId, TableBatchWeek week)
        {
            if (Product(productId, create: false) is null)
                return default;

            var handle = Acquire((productId, week.WeekStartTicks, week.PeriodWeeks), create: false, pin: false);
            if (handle is null)
                return default;

            try
            {
                return new TableBatchDeletedShare(ReadDeleted(handle.Db), FolderBytes(WeekPath(productId, week.WeekStartTicks, week.PeriodWeeks)));
            }
            finally
            {
                Release(handle);
            }
        }

        public void CompactWeek(Guid productId, TableBatchWeek week)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return;

            var key = (productId, week.WeekStartTicks, week.PeriodWeeks);
            long before;

            // A lease and nothing more: reads and writes of the product go on meanwhile (LevelDB compacts under them), and the
            // product's lock is not held while it compacts, so a drop, which takes that lock and then waits for the leases of
            // the week, never waits for this compaction while this compaction waits for the lock. Others do wait for it while
            // they hold the product's lock: a DropWeek of this database (for the lease), a flush of this database pinned as the
            // newest week (LevelDB runs one manual compaction at a time), and a Dispose (for the lease, unbounded). Hence the
            // job's rule: never compact the newest (pinned) week, and compact on the job's own thread, never beside a drop of
            // the same database.
            var handle = Acquire(key, create: false, pin: false);
            if (handle is null)
                return;

            try
            {
                before = ReadDeleted(handle.Db);
                Checkpoint?.Invoke("compacting");
                handle.Db.CompactRange(FirstKey, LastKey);
                Checkpoint?.Invoke("compacted");
            }
            catch (Exception ex) when (ex is not CheckpointException)
            {
                Statistics.Add(ref Statistics.WriteFailures);
                throw new TableBatchStoreException($"The table batch database {WeekName(week.WeekStartTicks, week.PeriodWeeks)} of product {productId} could not be compacted: {ex.Message}", ex);
            }
            finally
            {
                Release(handle);
            }

            if (before == 0)
                return;

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                // Lowered by what it counted before the compaction, not set to 0: bytes a delete counted meanwhile may not
                // have been compacted.
                var after = Acquire(key, create: false, pin: week.WeekStartTicks >= product.PinnedWeek);
                if (after is null)
                    return;

                try
                {
                    long left = Math.Max(0, ReadDeleted(after.Db) - before);
                    if (left == 0)
                        after.Db.Delete(DeletedKey, _syncWrite);
                    else
                        after.Db.Put(DeletedKey, EncodeDeleted(left), _syncWrite);

                    after.Dirty = true;
                }
                catch (Exception ex) when (ex is not CheckpointException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The deleted-bytes counter of the table batch database {WeekName(week.WeekStartTicks, week.PeriodWeeks)} of product {productId} could not be written: {ex.Message}", ex);
                }
                finally
                {
                    Release(after);
                }
            }
        }

        public IReadOnlyList<TableBatchWeek> GetCompactList(Guid productId)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return [];

            lock (product.Lock)
                return product.Compact.OrderBy(w => w.WeekStartTicks).ThenBy(w => w.PeriodWeeks).ToArray();
        }

        public void AddToCompactList(Guid productId, TableBatchWeek week) => ChangeCompactList(productId, week, add: true);

        public void RemoveFromCompactList(Guid productId, TableBatchWeek week) => ChangeCompactList(productId, week, add: false);

        private void ChangeCompactList(Guid productId, TableBatchWeek week, bool add)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return;

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                if (product.Compact.Contains(week) == add)
                    return;

                var entries = add ? product.Compact.Append(week) : product.Compact.Where(w => w != week);
                var list = entries.OrderBy(w => w.WeekStartTicks).ThenBy(w => w.PeriodWeeks).ToList();
                var path = Path.Combine(ProductPath(productId), CompactName);

                try
                {
                    Checkpoint?.Invoke("compact-listing");

                    if (list.Count > 0)
                        DurableFile.WriteAtomic(path, string.Concat(list.Select(w => WeekName(w.WeekStartTicks, w.PeriodWeeks) + "\n")), strict: true);
                    else if (File.Exists(path))
                        DurableFile.Delete(path, strict: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The compaction list of the table batches of product {productId} could not be written: {ex.Message}", ex);
                }

                product.Compact = [.. list];
            }
        }


        // === The move in flight ===

        public TableMoveInFlight GetMoveInFlight(Guid productId)
        {
            var product = Product(productId, create: false);
            if (product is null)
                return null;

            lock (product.Lock)
                return product.Move;
        }

        public void SetMoveInFlight(Guid productId, TableMoveInFlight record)
        {
            if (record is not null)
            {
                if (record.FromClass < 0 || record.FromClass > ushort.MaxValue || record.ToClass < 0 || record.ToClass > ushort.MaxValue || record.FromClass == record.ToClass)
                    throw new ArgumentException($"A move goes from one period class of 0 to {ushort.MaxValue} weeks into another, not P{record.FromClass} to P{record.ToClass}.", nameof(record));

                CheckHours(new TableBatchWeek(record.WeekStartTicks, record.FromClass), record.FirstHour, record.EndHour);
            }

            var product = Product(productId, create: false);
            if (product is null)
            {
                if (record is null)
                    return;

                throw new TableBatchStoreException($"The table batches of product {productId} have no folder: no move is in flight there.");
            }

            lock (product.Lock)
            {
                if (product.HoursDb is null)
                    throw new TableBatchStoreException("The table batch store is closed.");

                var path = Path.Combine(ProductPath(productId), MoveName);

                try
                {
                    Checkpoint?.Invoke("move-writing");

                    if (record is not null)
                        DurableFile.WriteAtomic(path, EncodeMove(record), strict: true);
                    else if (File.Exists(path))
                        DurableFile.Delete(path, strict: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Statistics.Add(ref Statistics.WriteFailures);
                    throw new TableBatchStoreException($"The move in flight of the table batches of product {productId} could not be written: {ex.Message}", ex);
                }

                product.Move = record;
            }
        }


        // === Helpers ===

        private readonly record struct CopiedRecord(long FirstTime, byte[] Value, byte[] Batch);

        private readonly record struct CopiedHour(long Hour, byte[] Mark, List<CopiedRecord> Records);

        // Under the product's lock. The sensor's index records of each hour of a source database with their batches, in the
        // order a read gives them (the hour region before the moved region, each by ordinal), and the hour's mark. A source
        // that does not exist gives hours without records.
        private List<CopiedHour> ReadSensorHours(ProductState product, Guid sensorId, TableBatchWeek from, long firstHour, long endHour)
        {
            var result = new List<CopiedHour>();
            var handle = Acquire((product.Id, from.WeekStartTicks, from.PeriodWeeks), create: false, pin: false);

            try
            {
                using var iterator = handle?.Db.CreateIterator();

                for (long hour = firstHour; hour < endHour; hour += TimeSpan.TicksPerHour)
                {
                    var records = new List<(bool Moved, int Ordinal, CopiedRecord Record)>();
                    byte[] mark = null;

                    if (handle is not null)
                    {
                        void Collect(byte[] prefix, bool moved)
                        {
                            for (iterator.Seek(prefix); iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix); iterator.Next())
                            {
                                var key = iterator.Key();
                                if (key.Length != (moved ? 38 : 37))
                                    throw new TableBatchRefusedException($"An index record of sensor {sensorId} of hour {new DateTime(hour, DateTimeKind.Utc):O} in " +
                                                                       $"{WeekName(from.WeekStartTicks, from.PeriodWeeks)} of product {product.Id} has a key of {key.Length} bytes: it is not copied.");

                                int at = moved ? 26 : 25;
                                long first = BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(at));
                                int ordinal = BinaryPrimitives.ReadInt32BigEndian(key.AsSpan(at + 8));
                                // Missing (null) or not a batch: carried as it is, a read of the copy counts it as the source's
                                // read does.
                                var batch = handle.Db.Get(moved ? MovedBatchKey(sensorId, hour, ordinal) : BatchKey(hour, ordinal));
                                records.Add((moved, ordinal, new CopiedRecord(first, iterator.Value(), batch)));
                            }
                        }

                        Collect(IndexPrefix(hour, sensorId), moved: false);
                        Collect(MovedIndexPrefix(hour, sensorId), moved: true);

                        if (records.Count > 0)
                        {
                            mark = handle.Db.Get(HourKey(hour, TagMark));
                            if (mark is null)
                                throw new TableBatchRefusedException($"The hour {new DateTime(hour, DateTimeKind.Utc):O} in {WeekName(from.WeekStartTicks, from.PeriodWeeks)} of product {product.Id} " +
                                                                   $"holds batches of sensor {sensorId} and no mark: it is not copied.");

                            try
                            {
                                DecodeCommit(hour, mark);
                            }
                            catch (FormatException ex)
                            {
                                throw new TableBatchRefusedException($"The mark of hour {new DateTime(hour, DateTimeKind.Utc):O} in {WeekName(from.WeekStartTicks, from.PeriodWeeks)} " +
                                                                   $"of product {product.Id} is not the hour's record ({ex.Message}): it is not copied.", ex);
                            }
                        }
                    }

                    result.Add(new CopiedHour(hour, mark, records.OrderBy(r => r.Moved).ThenBy(r => r.Ordinal).Select(r => r.Record).ToList()));
                }
            }
            finally
            {
                if (handle is not null)
                    Release(handle);
            }

            return result;
        }

        private void RefuseHourRegionRecords(DB db, Guid productId, Guid sensorId, TableBatchWeek target, IEnumerable<long> hours)
        {
            using var iterator = db.CreateIterator();

            foreach (var hour in hours)
            {
                var prefix = IndexPrefix(hour, sensorId);
                iterator.Seek(prefix);

                if (iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix))
                    throw new TableBatchRefusedException($"The table batch database {WeekName(target.WeekStartTicks, target.PeriodWeeks)} of product {productId} holds batches of " +
                                                       $"sensor {sensorId} of hour {new DateTime(hour, DateTimeKind.Utc):O} in its hour region: nothing is copied into it " +
                                                       "until they are deleted (when it is not the class that holds the hour) or the copy is not made (when it is).");
            }
        }

        // Adds to the batch a delete of every key of the sensor in the region of moved batches of one hour (0xFF | hour |
        // sensor: its index records and its batches) and answers what its index records counted; a batch no index record
        // names counts its own bytes.
        private static TableBatchSensorHours DeleteMovedSensorKeys(Iterator iterator, Guid sensorId, long hour, WriteBatch batch, ref int keys)
        {
            var prefix = MovedIndexKey(sensorId, hour, 0, 0)[..25];
            var named = new HashSet<int>();
            long batches = 0, rows = 0, bytes = 0;

            // The index records (0x00) come before the batches (0x01) of the same prefix.
            for (iterator.Seek(prefix); iterator.IsValid && iterator.Key().AsSpan().StartsWith(prefix); iterator.Next())
            {
                var key = iterator.Key();
                batch.Delete(key);
                keys++;

                if (key.Length == 38 && key[25] == TagIndex)
                {
                    named.Add(BinaryPrimitives.ReadInt32BigEndian(key.AsSpan(34)));
                    var (recordRows, recordBytes) = ReadIndexValue(iterator.Value());
                    batches++;
                    rows += recordRows;
                    bytes += recordBytes;
                }
                else if (key.Length == 30 && key[25] == TagBatch && !named.Contains(BinaryPrimitives.ReadInt32BigEndian(key.AsSpan(26))))
                    bytes += iterator.Value().Length;
            }

            return new TableBatchSensorHours(batches, rows, bytes);
        }

        // The rows and the stored length of an index value; nothing for one that does not decode.
        private static (int Rows, int Bytes) ReadIndexValue(byte[] value) =>
            value.Length == 17 && value[0] == IndexVersion
                ? (BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(9)), BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(13)))
                : (0, 0);

        private static long ReadDeleted(DB db)
        {
            var value = db.Get(DeletedKey);
            if (value is null)
                return 0;

            if (value.Length == 8)
                return BinaryPrimitives.ReadInt64LittleEndian(value);

            _logger.Error($"A deleted-bytes counter of a table batch database has {value.Length} bytes; taken as 0");
            return 0;
        }

        private static byte[] EncodeDeleted(long bytes)
        {
            var value = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(value, bytes);
            return value;
        }

        // The size of a database's files; a file LevelDB deletes meanwhile counts nothing.
        private static long FolderBytes(string path)
        {
            long total = 0;

            foreach (var file in Directory.EnumerateFiles(path))
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException) { }

            return total;
        }

        // Hours [first, end) on the hour, inside the week of a Monday-aligned database.
        private static void CheckHours(TableBatchWeek week, long firstHour, long endHour)
        {
            if (StorageWeek.GetStartTicks(week.WeekStartTicks) != week.WeekStartTicks)
                throw new ArgumentException("A week database starts on a Monday.", nameof(week));

            if (firstHour % TimeSpan.TicksPerHour != 0 || endHour % TimeSpan.TicksPerHour != 0 || firstHour < week.WeekStartTicks || firstHour > endHour ||
                endHour > week.WeekStartTicks + 7 * TimeSpan.TicksPerDay)
                throw new ArgumentException($"Hours on the hour, from {new DateTime(firstHour, DateTimeKind.Utc):O} to {new DateTime(endHour, DateTimeKind.Utc):O}, " +
                                            $"inside the week of {new DateTime(week.WeekStartTicks, DateTimeKind.Utc):yyyy-MM-dd}.", nameof(firstHour));
        }

        // "<version> <sensor> <week> <from> <to> <first hour> <end hour>", the hours in ticks.
        private static string EncodeMove(TableMoveInFlight record) =>
            string.Join(' ', MoveVersion, record.SensorId.ToString("N"), WeekName(record.WeekStartTicks, record.FromClass),
                record.FromClass.ToString(CultureInfo.InvariantCulture), record.ToClass.ToString(CultureInfo.InvariantCulture),
                record.FirstHour.ToString(CultureInfo.InvariantCulture), record.EndHour.ToString(CultureInfo.InvariantCulture)) + "\n";

        // A record that does not read (damaged, of another format, or a file that cannot be opened: a sharing violation, an
        // access right) is logged and taken as none, and the product opens: the placement, not this record, says where the
        // rows are, so what it loses is where a copy or a source not yet deleted may be left. Such leftovers stay until the
        // sensor's removal or the drop of their database deletes them; a later step out of that class deletes them there with
        // its source, and a later step into it is refused by CopySensorHours while its hour region holds them, until the job
        // deletes them first.
        private static TableMoveInFlight ReadMove(Guid productId, string folder)
        {
            var path = Path.Combine(folder, MoveName);
            if (!File.Exists(path))
                return null;

            string text;
            try
            {
                text = File.ReadAllText(path).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error($"The move in flight of the table batches of product {productId} could not be read ({ex.Message}); taken as none");
                return null;
            }

            var fields = text.Split(' ');

            if (fields.Length == 7 && fields[0] == MoveVersion && Guid.TryParseExact(fields[1], "N", out var sensor) && TryParseWeek(fields[2], out var week) &&
                int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var fromClass) && fromClass == week.PeriodWeeks &&
                int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var toClass) &&
                long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var first) &&
                long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var end))
            {
                var record = new TableMoveInFlight(sensor, week.WeekStartTicks, fromClass, toClass, first, end);

                try
                {
                    CheckHours(week, first, end);
                    if (fromClass != toClass && toClass <= ushort.MaxValue)
                        return record;
                }
                catch (ArgumentException) { }
            }

            _logger.Error($"The move in flight of the table batches of product {productId} does not read: '{text}'; taken as none");
            return null;
        }

        private HashSet<TableBatchWeek> ReadCompact(Guid productId, string folder)
        {
            var path = Path.Combine(folder, CompactName);
            var weeks = new HashSet<TableBatchWeek>();

            if (!File.Exists(path))
                return weeks;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A hint only (a database left out is compacted when its counter is next found over the threshold).
                _logger.Error($"The compaction list of the table batches of product {productId} could not be read ({ex.Message}); taken as empty");
                return weeks;
            }

            foreach (var line in lines)
                if (TryParseWeek(line, out var week))
                    weeks.Add(week);
                else if (line.Length > 0)
                    _logger.Error($"The compaction list of the table batches of product {productId} has a line that is not a week database: '{line}'; skipped");

            return weeks;
        }
    }
}
