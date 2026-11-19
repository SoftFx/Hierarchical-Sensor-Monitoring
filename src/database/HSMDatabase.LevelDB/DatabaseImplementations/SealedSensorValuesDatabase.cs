using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.LevelDB.Extensions;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.SealedHistory;
using LevelDB;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    /// <summary>
    /// A sealed week (ADR-0020, #1803): the week's row database, now its overlay, plus the sibling chunk
    /// database <c>SensorValuesSealedV1_&lt;from&gt;_&lt;to&gt;</c>. Every read sees one stream in key order
    /// merged from both, where an overlay row hides a chunk row with the same key; the read methods run the
    /// algorithms of <see cref="LevelDBDatabaseAdapter"/> over that stream, so they answer exactly what a row
    /// database holding the same rows would. Writes go to the overlay. A range delete rewrites the chunks it
    /// touches in one synced batch and then deletes the overlay rows.
    ///
    /// Consistency without a read lock: a read opens the overlay iterator first and the chunk iterator second
    /// (each reads the snapshot taken when it is created), and a range delete changes the chunks first and the
    /// overlay second. A read therefore sees either the old chunks with the old overlay, or the new chunks with
    /// an overlay the delete has partly or fully gone through: every row it returns is the row the week held
    /// before the delete, or the delete has removed it. It never sees a chunk row an overlay row used to hide.
    /// </summary>
    internal sealed class SealedSensorValuesDatabase : IntervalDataseBase, ISensorValuesDatabase
    {
        private static readonly WriteOptions _syncWrite = new() { Sync = true };

        private readonly ReadOptions _readOptions = new();
        private readonly MemoryPackFormatter _formatter = new();
        private readonly LevelDBDatabaseAdapter _chunks;
        private readonly SealedChunkReader _reader;

        // Serializes the writers of the chunk database: a range delete plans its rewrite from the chunks it reads.
        private readonly object _chunkWriteLock = new();


        public string SealedName { get; }

        /// <summary>Chunks of this week that failed to read since it was opened.</summary>
        public long ChunkReadFailures => _reader.Failures;

        // Test seam: runs after a range delete has rewritten the chunks and before it deletes the overlay rows.
        internal Action AfterChunkRewrite { get; set; }

        // Test seam: runs when a read has opened its overlay iterator and not yet its chunk iterator.
        internal Action BetweenReadIterators { get; set; }


        public SealedSensorValuesDatabase(string name, string sealedName, long from, long to) : base(name, from, to)
        {
            SealedName = sealedName;
            _reader = new SealedChunkReader(sealedName);

            try
            {
                _chunks = new LevelDBDatabaseAdapter(sealedName);
            }
            catch
            {
                base.Dispose();
                throw;
            }

            _logger.Info($"Opened sealed week {sealedName} with its overlay {name}");
        }


        #region Writes

        public void PutSensorValue(byte[] key, byte[] value)
        {
            try
            {
                OnBeforeChange();
                _openedDb.Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {key.GetString()}");
            }
        }

        public void PutSensorValueOrThrow(byte[] key, byte[] value)
        {
            OnBeforeChange();
            _openedDb.Put(key, value);
        }

        // Chunks first, overlay second (see the class comment). A delete whose chunk rewrite fails changes
        // nothing: the overlay rows stay, so no chunk row they hide comes back.
        public void RemoveSensorValues(byte[] from, byte[] to)
        {
            try
            {
                if (!RewriteChunks(from, to))
                    return;

                _openedDb.DeleteValueFromTo(from, to, OnBeforeChange);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed removing values [{from.GetString()}, {to.GetString()}] - {e.Message}");
            }
        }

        #endregion

        #region Point reads (mirror LevelDBDatabaseAdapter and SensorValuesDatabaseWorker)

        public void FillLatestValues(Dictionary<byte[], (long, byte[], byte[])> keyValuePairs)
        {
            try
            {
                using var cursor = OpenCursor();

                foreach (var (key, value) in keyValuePairs)
                {
                    var (from, toKey, latestValue) = value;

                    if (latestValue != null || To < from)
                        continue;

                    if (toKey.Length == KeyOrder.KeyLength && toKey.StartsWith(key))
                    {
                        // The row database walks every row from the sensor's first one up to toKey and keeps the
                        // last; with toKey inside the sensor's key range that is the last row before toKey.
                        cursor.Seek(toKey);

                        if (cursor.IsValid)
                            cursor.Prev();
                        else
                            cursor.SeekToLast();

                        if (cursor.IsValid && cursor.Key.StartsWith(key) && cursor.Key.IsSmaller(toKey))
                            keyValuePairs[key] = (from, toKey, cursor.Value);
                    }
                    else
                    {
                        for (cursor.Seek(key); cursor.IsValid && cursor.Key.StartsWith(key) && cursor.Key.IsSmaller(toKey); cursor.Next())
                            keyValuePairs[key] = (from, toKey, cursor.Value);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to fill sensors latest values");
            }
        }

        public byte[] Get(byte[] key, byte[] sensorId)
        {
            try
            {
                using var cursor = OpenCursor();

                cursor.Seek(key);

                return cursor.IsValid && cursor.Key.StartsWith(sensorId) ? cursor.Value : null;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting value {key.GetString()} - {e.Message}");

                return [];
            }
        }

        public byte[] GetLatest(byte[] key, byte[] sensorId)
        {
            try
            {
                using var cursor = OpenCursor();

                bool CheckValue() => cursor.IsValid && cursor.Key.StartsWith(sensorId) && cursor.Key.IsSmallerOrEquals(key);

                cursor.Seek(key);

                if (CheckValue())
                    return cursor.Value;

                if (!cursor.IsValid)
                    cursor.SeekToLast();
                else
                    cursor.Prev();

                return CheckValue() ? cursor.Value : null;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting latest value {key.GetString()} - {e.Message}");

                return null;
            }
        }

        public byte[] GetFirst(byte[] key, byte[] sensorId)
        {
            try
            {
                using var cursor = OpenCursor();

                cursor.Seek(key);

                return cursor.IsValid && cursor.Key.StartsWith(sensorId) ? cursor.Value : null;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting first value {key.GetString()} - {e.Message}");

                return null;
            }
        }

        public Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds, Dictionary<Guid, (byte[] lastValue, byte[] firstValue)> results = null)
        {
            try
            {
                results ??= new Dictionary<Guid, (byte[], byte[])>();

                if (!sensorIds.Any())
                    return results;

                using var cursor = OpenCursor();

                foreach (var sensorId in sensorIds)
                {
                    byte[] currentFirstValue = null;
                    byte[] currentLastValue = null;

                    var minKey = new DbKey(sensorId, DateTime.MinValue);
                    var maxKey = new DbKey(sensorId, DateTime.MaxValue.Ticks);
                    var prefixBytes = minKey.ToPrefixBytes();

                    var firstAlreadyKnown = results.TryGetValue(sensorId, out var existing) && existing.Item1 != null;

                    if (!firstAlreadyKnown)
                    {
                        cursor.Seek(minKey.ToBytes());

                        if (cursor.IsValid && cursor.Key.StartsWith(prefixBytes))
                            currentFirstValue = cursor.Value;
                    }

                    // As in the row database: a seek past the end finds no last value (it does not fall back to the last key).
                    cursor.Seek(maxKey.ToBytes());

                    if (cursor.IsValid && cursor.Key.StartsWith(prefixBytes))
                    {
                        currentLastValue = cursor.Value;
                    }
                    else if (cursor.IsValid)
                    {
                        cursor.Prev();

                        if (cursor.IsValid && cursor.Key.StartsWith(prefixBytes))
                            currentLastValue = cursor.Value;
                    }

                    if (results.TryGetValue(sensorId, out existing))
                        results[sensorId] = (existing.Item1 ?? currentFirstValue, currentLastValue ?? existing.Item2);
                    else if (currentFirstValue != null || currentLastValue != null)
                        results[sensorId] = (currentFirstValue ?? currentLastValue, currentLastValue ?? currentFirstValue);
                }

                return results;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting latest values - {e.Message}");
                return new Dictionary<Guid, (byte[], byte[])>();
            }
        }

        #endregion

        #region Range reads (lazy; a chunk that fails to read throws from the enumeration)

        public IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to) => ValuesFromTo(from, to);

        public IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to) => KeyValuesToFrom(from, to).Select(pair => pair.value);

        public IEnumerable<(byte[] key, byte[] value)> GetKeysValuesTo(byte[] from, byte[] to) => KeyValuesToFrom(from, to);

        public IEnumerable<(byte[] key, byte[] value)> GetAll()
        {
            using var cursor = OpenCursor();

            for (cursor.SeekToFirst(); cursor.IsValid; cursor.Next())
                yield return (cursor.Key, cursor.Value);
        }

        private IEnumerable<byte[]> ValuesFromTo(byte[] from, byte[] to)
        {
            using var cursor = OpenCursor();

            for (cursor.Seek(from); cursor.IsValid && cursor.Key.IsSmallerOrEquals(to); cursor.Next())
                yield return cursor.Value;
        }

        // LevelDBDatabaseAdapter.GetValueKeyPairToFrom over the merged stream.
        private IEnumerable<(byte[] key, byte[] value)> KeyValuesToFrom(byte[] from, byte[] to)
        {
            using var cursor = OpenCursor();

            cursor.Seek(to);

            if (!cursor.IsValid)
                cursor.SeekToLast();

            while (cursor.IsValid && cursor.Key.IsGreater(to))
            {
                cursor.Prev();

                if (!cursor.IsValid || cursor.Key.IsSmaller(from))
                    yield break;
            }

            for (; cursor.IsValid && cursor.Key.IsGreaterOrEquals(from); cursor.Prev())
                yield return (cursor.Key, cursor.Value);
        }

        #endregion

        #region Size and maintenance

        public (long count, long keyBytes, long valueBytes) CalculateHistorySize(byte[] from, byte[] to)
        {
            long count = 0, keyBytes = 0, valueBytes = 0;

            using (var cursor = OpenCursor())
                for (cursor.Seek(from); cursor.IsValid && cursor.Key.IsSmallerOrEquals(to); cursor.Next())
                    count++;

            foreach (var (key, value) in _openedDb.GetValueKeyPairFromTo(from, to))
            {
                keyBytes += key.Length;
                valueBytes += value.Length;
            }

            using var chunks = _chunks.Native.CreateIterator(_readOptions);

            foreach (var (key, chunk, _) in ChunksTouching(chunks, from, to))
            {
                keyBytes += key.Length;
                valueBytes += chunk.Length;
            }

            return (count, keyBytes, valueBytes);
        }

        public void Compact()
        {
            _openedDb.Compact();
            _chunks.Compact();
        }

        public override void Dispose()
        {
            try
            {
                _chunks?.Dispose();
                _readOptions.Dispose();
            }
            finally
            {
                base.Dispose();
            }
        }

        #endregion


        // The overlay iterator first, the chunk iterator second: see the class comment.
        private MergedRowCursor OpenCursor()
        {
            var overlay = _openedDb.Native.CreateIterator(_readOptions);
            Iterator chunks = null;

            try
            {
                BetweenReadIterators?.Invoke();

                chunks = _chunks.Native.CreateIterator(_readOptions);

                return new MergedRowCursor(new NativeRowCursor(overlay), new ChunkRowCursor(chunks, _reader, _formatter));
            }
            catch
            {
                chunks?.Dispose();
                overlay.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Removes the chunk rows in [from, to]: chunks wholly inside are deleted, a chunk on a border is
        /// re-encoded with the rows outside the range and stored under the key of its new first row. Everything
        /// is one synced write batch, so a crash leaves either every chunk as it was or every chunk rewritten.
        /// False, with the failure logged and counted, when a chunk on the border cannot be rewritten.
        /// </summary>
        private bool RewriteChunks(byte[] from, byte[] to)
        {
            if (KeyOrder.Compare(from, to) > 0)
                return true;

            lock (_chunkWriteLock)
            {
                var deletes = new List<byte[]>();
                var puts = new List<(byte[] key, byte[] chunk)>();

                try
                {
                    using var chunks = _chunks.Native.CreateIterator(_readOptions);

                    foreach (var (key, chunk, header) in ChunksTouching(chunks, from, to))
                    {
                        if (header is not { } read)
                        {
                            // Unreadable, and every row it can hold is in the range: the delete removes it.
                            _logger.Error($"Range delete [{from.GetString()}, {to.GetString()}] on sealed week {SealedName} removes an unreadable chunk {KeyOrder.Tick(key)} that lies wholly inside it");
                            deletes.Add(key);
                            continue;
                        }

                        var lastRow = KeyOrder.RowKey(key, read.LastKey);

                        if (KeyOrder.Compare(key, from) >= 0 && KeyOrder.Compare(lastRow, to) <= 0)
                        {
                            deletes.Add(key);
                            continue;
                        }

                        var rows = _reader.Decode(key, chunk);
                        var kept = rows.FindAll(row => !InRange(KeyOrder.RowKey(key, row.Key), from, to));

                        if (kept.Count == rows.Count)
                            continue;

                        deletes.Add(key);

                        if (kept.Count > 0)
                            puts.AddRange(Reencode(key, kept));
                    }
                }
                catch (Exception e)
                {
                    SealedHistoryDiagnostics.CountChunkRewriteFailure();
                    _logger.Error(e, $"Range delete [{from.GetString()}, {to.GetString()}] on sealed week {SealedName} refused: a chunk on its border cannot be rewritten; nothing was deleted");
                    return false;
                }

                if (deletes.Count > 0)
                {
                    OnBeforeChange();

                    using var batch = new WriteBatch();

                    // Deletes first: a re-encoded chunk can keep the key of the chunk it replaces.
                    foreach (var key in deletes)
                        batch.Delete(key);
                    foreach (var (key, chunk) in puts)
                        batch.Put(key, chunk);

                    _chunks.Native.Write(batch, _syncWrite);
                }
            }

            AfterChunkRewrite?.Invoke();

            return true;
        }

        private List<(byte[] key, byte[] chunk)> Reencode(byte[] oldKey, List<HistoryRow> kept)
        {
            var encoded = ChunkCodec.Encode(kept);

            if (!encoded.IsSuccess)
                throw new InvalidOperationException($"re-encoding {kept.Count} rows of chunk {KeyOrder.Tick(oldKey)} refused: {encoded.Refusal}");

            var result = new List<(byte[], byte[])>(encoded.Chunks.Count);
            var verified = 0;

            // Verified like a seal: the new chunks decode to exactly the rows they replace.
            foreach (var chunk in encoded.Chunks)
            {
                var header = ChunkCodec.ReadHeader(chunk);
                var decoded = ChunkCodec.Decode(chunk);

                foreach (var row in decoded)
                {
                    var difference = verified < kept.Count ? RowFidelity.FindDifference(kept[verified], row) : "row count";

                    if (difference is not null)
                        throw new InvalidOperationException($"a re-encoded chunk of {KeyOrder.Tick(oldKey)} does not match its rows (row {verified}, {difference})");

                    verified++;
                }

                result.Add((KeyOrder.RowKey(oldKey, header.FirstKey), chunk));
            }

            if (verified != kept.Count)
                throw new InvalidOperationException($"re-encoded chunks of {KeyOrder.Tick(oldKey)} hold {verified} of {kept.Count} rows");

            return result;
        }

        // The chunks with a row in [from, to], in key order, with their verified headers. A chunk that cannot be
        // read comes with a null header when it lies wholly inside the range for sure (see ProvablyInside); any
        // other unreadable chunk throws.
        private IEnumerable<(byte[] key, byte[] chunk, ChunkHeader? header)> ChunksTouching(Iterator iterator, byte[] from, byte[] to)
        {
            // The chunk before `from` reaches into the range when its last row does.
            iterator.Seek(from);

            if (iterator.IsValid)
                iterator.Prev();
            else
                iterator.SeekToLast();

            if (!iterator.IsValid)
                iterator.SeekToFirst();

            for (; iterator.IsValid; iterator.Next())
            {
                var key = iterator.Key();

                if (KeyOrder.Compare(key, to) > 0)
                    yield break;

                var chunk = iterator.Value();
                ChunkHeader? header = null;

                try
                {
                    header = _reader.ReadHeader(key, chunk);
                }
                catch (SealedChunkReadException) when (ProvablyInside(iterator, key, from, to))
                {
                }

                if (header is { } read && KeyOrder.Compare(KeyOrder.RowKey(key, read.LastKey), from) < 0)
                    continue;

                yield return (key, chunk, header);
            }
        }

        // Whether every row of the chunk under `key` lies in [from, to] without reading it: its rows start at its
        // key, belong to the key's sensor, are DateTime ticks, and end before the next chunk's key (chunks never
        // overlap). Leaves the iterator where it was.
        private static bool ProvablyInside(Iterator iterator, byte[] key, byte[] from, byte[] to)
        {
            if (key.Length != KeyOrder.KeyLength || KeyOrder.Compare(key, from) < 0)
                return false;

            if (KeyOrder.Compare(KeyOrder.RowKey(key, DateTime.MaxValue.Ticks), to) <= 0)
                return true;

            iterator.Next();

            var next = iterator.IsValid ? iterator.Key() : null;

            if (iterator.IsValid)
                iterator.Prev();
            else
                iterator.SeekToLast();

            return next is not null && KeyOrder.Compare(next, to) <= 0;
        }

        private static bool InRange(byte[] key, byte[] from, byte[] to) => KeyOrder.Compare(key, from) >= 0 && KeyOrder.Compare(key, to) <= 0;
    }
}
