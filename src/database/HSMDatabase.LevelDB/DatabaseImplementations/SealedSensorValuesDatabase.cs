using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// database holding the same rows would. Writes go to the overlay. A range delete changes the chunks in one
    /// synced batch and then deletes the overlay rows: a prefix of one sensor (retention, clear history, sensor
    /// removal) deletes the chunks below its cutoff and raises the sensor's delete watermark (<see cref="SensorWatermark"/>,
    /// #1805), any other range rewrites the chunks it touches.
    ///
    /// Consistency without a read lock: a read opens the overlay iterator first and the chunk iterator second
    /// (each reads the snapshot taken when it is created), and a range delete changes the chunks first and the
    /// overlay second. A read therefore sees either the old chunks with the old overlay, or the new chunks with
    /// an overlay the delete has partly or fully gone through: every row it returns is the row the week held
    /// before the delete, or the delete has removed it. It never sees a chunk row an overlay row used to hide.
    /// The watermarks are read from the same snapshot as the chunks, so a prefix delete is seen whole or not at all.
    ///
    /// <para><b>Overlay-only reads</b> (#1805 follow-up). While the overlay holds every row the chunks hold (the keep period
    /// of a seal, <see cref="SealedWeekStages.OverlayHoldsEveryRow"/>), the merged stream is exactly the overlay: every live
    /// chunk row has an overlay row with its key, and the overlay row wins. Reads then use the overlay alone, as the row
    /// worker does; the chunk database stays open for the deletes, which must change the chunks too. The sealing job ends this state with <see cref="StopOverlayOnlyReads"/> before it
    /// deletes its first overlay row. A read decides once, after it has created its overlay iterator: if the state still
    /// held then, the iterator's snapshot was taken before that first delete and holds every row.</para>
    /// </summary>
    internal sealed class SealedSensorValuesDatabase : IntervalDataseBase, ISensorValuesDatabase
    {
        private static readonly WriteOptions _syncWrite = new() { Sync = true };

        private readonly ReadOptions _readOptions = new();
        private readonly MemoryPackFormatter _formatter = new();
        private readonly SealedChunkReader _reader;

        private readonly LevelDBDatabaseAdapter _chunks;

        // 1 while the overlay holds every row (reads use it alone); set to 0 once, never back.
        private int _overlayOnly;

        // Serializes the writers of the chunk database: a range delete plans its rewrite from the chunks it reads.
        private readonly object _chunkWriteLock = new();


        public string SealedName { get; }

        /// <summary>Chunks of this week that failed to read since it was opened.</summary>
        public long ChunkReadFailures => _reader.Failures;

        /// <summary>Reads use the overlay alone: it holds every row the chunks hold (see the class comment).</summary>
        internal bool ReadsOverlayOnly => Volatile.Read(ref _overlayOnly) == 1;

        /// <summary>Chunk iterators the reads created since the week was opened (tests: an overlay-only read creates none).</summary>
        internal long ChunkIteratorsOpened => Interlocked.Read(ref _chunkIteratorsOpened);

        private long _chunkIteratorsOpened;

        // Test seam: runs after a range delete has rewritten the chunks and before it deletes the overlay rows.
        internal Action AfterChunkRewrite { get; set; }

        // Test seam: runs when a read has opened its overlay iterator, before it decides whether it needs the chunks and
        // before it opens its chunk iterator.
        internal Action BetweenReadIterators { get; set; }


        /// <param name="overlay">The open row database of the instance this one replaces in a live swap (#1804).</param>
        /// <param name="chunks">The open chunk database of the seal that built the week (#1804).</param>
        /// <param name="overlayHoldsEveryRow">Whether reads may use the overlay alone; null: as the files of the chunk
        /// directory say (<see cref="SealedWeekStages.OverlayHoldsEveryRow"/>). A seal passes true: it built the chunks from
        /// the overlay and deleted nothing.</param>
        /// <param name="utcNow">The clock that question is asked with; null: the system clock (the database core passes its own).</param>
        public SealedSensorValuesDatabase(string name, string sealedName, long from, long to, LevelDBDatabaseAdapter overlay = null,
            LevelDBDatabaseAdapter chunks = null, bool? overlayHoldsEveryRow = null, DateTime? utcNow = null)
            : base(name, from, to, overlay)
        {
            SealedName = sealedName;
            _reader = new SealedChunkReader(sealedName);
            _overlayOnly = overlayHoldsEveryRow ?? SealedWeekStages.OverlayHoldsEveryRow(sealedName, utcNow ?? DateTime.UtcNow) ? 1 : 0;

            try
            {
                _chunks = chunks ?? new LevelDBDatabaseAdapter(sealedName); // chunks: the open database of the seal that built it (#1804)
            }
            catch
            {
                // A row database handed over by a live swap still belongs to the instance it came from.
                if (overlay is not null)
                    ReleaseRowDatabase();

                base.Dispose();
                throw;
            }

            _logger.Info(ReadsOverlayOnly
                ? $"Opened sealed week {sealedName} with its overlay {name}; the overlay holds every row, so reads use it alone"
                : $"Opened sealed week {sealedName} with its overlay {name}");
        }


        /// <summary>
        /// Ends overlay-only reads for good: from now on every read merges the overlay with the chunks. The sealing job calls
        /// it before it deletes the first overlay row of the week, after it made <see cref="SealedWeekStages.SourceRowsRemovingFileName"/>
        /// durable.
        /// </summary>
        internal void StopOverlayOnlyReads()
        {
            if (Interlocked.Exchange(ref _overlayOnly, 0) == 1)
                _logger.Info($"Sealed week {SealedName}: overlay rows are about to be deleted, reads merge the overlay with the chunks from now on");

            OverlayRowDeletesAnnounced = true;
        }

        /// <summary>Whether <see cref="StopOverlayOnlyReads"/> ran on this instance (the job then need not announce again).</summary>
        internal bool OverlayRowDeletesAnnounced { get; private set; }

        #region The sealing job (#1804)

        /// <summary>The chunk database, for the sealing job: source-row removal, folding, unsealing.</summary>
        internal LevelDBDatabaseAdapter ChunkDatabase => _chunks;

        internal SealedChunkReader ChunkReader => _reader;

        /// <summary>Taken by every writer of the chunk database: range deletes here, folds in the sealing job.</summary>
        internal object ChunkWriteLock => _chunkWriteLock;

        #endregion


        #region Writes

        public void PutSensorValue(byte[] key, byte[] value)
        {
            try
            {
                OnBeforePut();
                _openedDb.Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {key.GetString()}");
            }
        }

        public void PutSensorValueOrThrow(byte[] key, byte[] value)
        {
            OnBeforePut();
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

                _openedDb.DeleteValueFromTo(from, to, OnBeforeDelete);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed removing values [{from.GetString()}, {to.GetString()}] - {e.Message}");
            }
        }

        #endregion

        #region Point reads (mirror LevelDBDatabaseAdapter and SensorValuesDatabaseWorker)

        // Get, GetLatest and GetFirst let an unreadable chunk through as SealedChunkReadException instead of the
        // row worker's "no value" fallback: DatabaseCore reads null as "this week has none" and would answer with
        // another week's value. Any other failure keeps the row worker's fallback.

        public void FillLatestValues(Dictionary<byte[], (long, byte[], byte[])> keyValuePairs)
        {
            IRowCursor cursor = null;

            try
            {
                cursor = OpenCursor();

                foreach (var (key, value) in keyValuePairs)
                {
                    var (from, toKey, latestValue) = value;

                    if (latestValue != null || To < from)
                        continue;

                    byte[] latest = null;

                    try
                    {
                        if (toKey.Length == KeyOrder.KeyLength && toKey.StartsWith(key))
                        {
                            // The row database walks every row from the sensor's first one up to toKey and keeps the
                            // last; with toKey inside the sensor's key range that is the last row before toKey.
                            cursor.Seek(toKey);

                            if (cursor.IsValid)
                                cursor.Prev();
                            else
                                cursor.SeekToLast();

                            if (cursor.IsValid && StartsWith(cursor, key) && Lt(cursor, toKey))
                                latest = cursor.Value;
                        }
                        else
                        {
                            for (cursor.Seek(key); cursor.IsValid && StartsWith(cursor, key) && Lt(cursor, toKey); cursor.Next())
                                latest = cursor.Value;
                        }
                    }
                    catch (SealedChunkReadException)
                    {
                        // Already logged and counted. Only this sensor goes without a value from this week; the
                        // cursor may have stopped anywhere, so the next sensor gets a fresh one.
                        cursor.Dispose();
                        cursor = OpenCursor();
                        continue;
                    }

                    if (latest is not null)
                        keyValuePairs[key] = (from, toKey, latest);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to fill sensors latest values");
            }
            finally
            {
                cursor?.Dispose();
            }
        }

        public byte[] Get(byte[] key, byte[] sensorId)
        {
            try
            {
                using var cursor = OpenCursor();

                cursor.Seek(key);

                return cursor.IsValid && StartsWith(cursor, sensorId) ? cursor.Value : null;
            }
            catch (Exception e) when (e is not SealedChunkReadException)
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

                bool CheckValue() => cursor.IsValid && StartsWith(cursor, sensorId) && Le(cursor, key);

                cursor.Seek(key);

                if (CheckValue())
                    return cursor.Value;

                if (!cursor.IsValid)
                    cursor.SeekToLast();
                else
                    cursor.Prev();

                return CheckValue() ? cursor.Value : null;
            }
            catch (Exception e) when (e is not SealedChunkReadException)
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

                return cursor.IsValid && StartsWith(cursor, sensorId) ? cursor.Value : null;
            }
            catch (Exception e) when (e is not SealedChunkReadException)
            {
                _logger.Error($"Failed getting first value {key.GetString()} - {e.Message}");

                return null;
            }
        }

        public Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds, Dictionary<Guid, (byte[] lastValue, byte[] firstValue)> results = null)
        {
            results ??= new Dictionary<Guid, (byte[], byte[])>();
            IRowCursor cursor = null;

            try
            {
                if (!sensorIds.Any())
                    return results;

                cursor = OpenCursor();

                foreach (var sensorId in sensorIds)
                {
                    byte[] currentFirstValue = null;
                    byte[] currentLastValue = null;

                    var minKey = new DbKey(sensorId, DateTime.MinValue);
                    var maxKey = new DbKey(sensorId, DateTime.MaxValue.Ticks);
                    var prefixBytes = minKey.ToPrefixBytes();

                    var firstAlreadyKnown = results.TryGetValue(sensorId, out var existing) && existing.Item1 != null;

                    try
                    {
                        if (!firstAlreadyKnown)
                        {
                            cursor.Seek(minKey.ToBytes());

                            if (cursor.IsValid && StartsWith(cursor, prefixBytes))
                                currentFirstValue = cursor.Value;
                        }

                        // As in the row database: a seek past the end finds no last value (it does not fall back to the last key).
                        cursor.Seek(maxKey.ToBytes());

                        if (cursor.IsValid && StartsWith(cursor, prefixBytes))
                        {
                            currentLastValue = cursor.Value;
                        }
                        else if (cursor.IsValid)
                        {
                            cursor.Prev();

                            if (cursor.IsValid && StartsWith(cursor, prefixBytes))
                                currentLastValue = cursor.Value;
                        }
                    }
                    catch (SealedChunkReadException)
                    {
                        // Already logged and counted. This sensor keeps what the caller had for it; the others go on
                        // with a fresh cursor.
                        cursor.Dispose();
                        cursor = OpenCursor();
                        continue;
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
                // Unlike the row worker, which returns an empty dictionary here: what the caller accumulated from
                // other weeks is kept.
                _logger.Error($"Failed getting latest values - {e.Message}");
                return results;
            }
            finally
            {
                cursor?.Dispose();
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

            for (cursor.Seek(from); cursor.IsValid && Le(cursor, to); cursor.Next())
                yield return cursor.Value;
        }

        // LevelDBDatabaseAdapter.GetValueKeyPairToFrom over the merged stream.
        private IEnumerable<(byte[] key, byte[] value)> KeyValuesToFrom(byte[] from, byte[] to)
        {
            using var cursor = OpenCursor();

            cursor.Seek(to);

            if (!cursor.IsValid)
                cursor.SeekToLast();

            while (cursor.IsValid && Gt(cursor, to))
            {
                cursor.Prev();

                if (!cursor.IsValid || Lt(cursor, from))
                    yield break;
            }

            for (; cursor.IsValid && Ge(cursor, from); cursor.Prev())
                yield return (cursor.Key, cursor.Value);
        }

        #endregion

        #region Size and maintenance

        // Informational (the history size of a sensor or node in the UI, the delete preview): it never throws for
        // an unreadable chunk. That chunk adds its stored bytes, and the count falls back to a lower bound; the
        // failure is logged and counted where it is found.
        public (long count, long keyBytes, long valueBytes) CalculateHistorySize(byte[] from, byte[] to)
        {
            long count = 0, keyBytes = 0, valueBytes = 0;

            try
            {
                using var cursor = OpenCursor();

                for (cursor.Seek(from); cursor.IsValid && Le(cursor, to); cursor.Next())
                    count++;
            }
            catch (SealedChunkReadException)
            {
                count = LowerBoundCount(from, to);
            }

            foreach (var (key, value) in _openedDb.GetValueKeyPairFromTo(from, to))
            {
                keyBytes += key.Length;
                valueBytes += value.Length;
            }

            using var chunks = _chunks.Native.CreateIterator(_readOptions);

            foreach (var (key, chunk, _) in ChunkEntries(chunks, from, to))
            {
                keyBytes += key.Length;
                valueBytes += chunk.Length;
            }

            return (count, keyBytes, valueBytes);
        }

        // The rows in [from, to] when some chunk cannot be read: every overlay row, the rows of the readable chunks
        // that no overlay row hides, and for an unreadable chunk wholly in the range with a readable header its row
        // count less the overlay rows in its span (each could hide one of its rows). An unreadable chunk without a
        // readable header counts nothing. Never more than a read would return.
        private long LowerBoundCount(byte[] from, byte[] to)
        {
            var overlay = new List<byte[]>();

            foreach (var (key, _) in _openedDb.GetValueKeyPairFromTo(from, to))
                overlay.Add(key);

            var hidden = overlay.Select(Convert.ToHexString).ToHashSet();
            long count = overlay.Count;

            // The chunks and the watermarks from one snapshot (#1805 review W3): a prefix delete or a fold in between
            // would otherwise pair chunk rows with a watermark that no longer applies to them.
            var db = _chunks.Native;

            using var snapshot = db.CreateSnapshot();
            using var options = new ReadOptions { Snapshot = snapshot };
            using var chunks = db.CreateIterator(options);

            foreach (var (key, chunk, header) in ChunkEntries(chunks, from, to))
            {
                if (header is not { } read)
                    continue;

                // Rows at or below the sensor's watermark do not count; a sensor whose watermark cannot be read counts
                // none of its chunk rows.
                long? watermark;

                try
                {
                    watermark = _reader.ReadWatermark(db, options, key);
                }
                catch (SealedChunkReadException)
                {
                    continue;
                }

                try
                {
                    foreach (var row in _reader.Decode(key, chunk))
                    {
                        var rowKey = KeyOrder.RowKey(key, row.Key);

                        if (InRange(rowKey, from, to) && !hidden.Contains(Convert.ToHexString(rowKey)) && (watermark is not { } w || row.Key > w))
                            count++;
                    }
                }
                catch (SealedChunkReadException)
                {
                    var lastRow = KeyOrder.RowKey(key, read.LastKey);

                    // The header's row count includes the rows below the watermark of a chunk that reaches below it.
                    if (KeyOrder.Compare(key, from) >= 0 && KeyOrder.Compare(lastRow, to) <= 0 && (watermark is not { } w || KeyOrder.Tick(key) > w))
                        count += Math.Max(0, read.RowCount - overlay.Count(k => KeyOrder.Compare(k, key) >= 0 && KeyOrder.Compare(k, lastRow) <= 0));
                }
            }

            return count;
        }

        // The chunks that may have a row in [from, to], in key order, never throwing: a readable one with its
        // header (only if it does reach the range), an unreadable one with a null header if its key is in the range.
        // An unreadable chunk before `from` cannot be placed and is left out.
        private IEnumerable<(byte[] key, byte[] chunk, ChunkHeader? header)> ChunkEntries(Iterator iterator, byte[] from, byte[] to)
        {
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

                // A delete watermark (#1805) is not a chunk; its few bytes are not counted.
                if (SensorWatermark.IsKey(key))
                    continue;

                if (KeyOrder.Compare(key, to) > 0)
                    yield break;

                var before = KeyOrder.Compare(key, from) < 0;

                if (before && from.Length >= KeyOrder.SensorIdLength &&
                    !key.AsSpan(0, KeyOrder.SensorIdLength).SequenceEqual(from.AsSpan(0, KeyOrder.SensorIdLength)))
                    continue;

                var chunk = iterator.Value();
                ChunkHeader? header = null;

                try
                {
                    header = _reader.ReadHeader(key, chunk);
                }
                catch (SealedChunkReadException)
                {
                }

                if (header is { } read ? KeyOrder.Compare(KeyOrder.RowKey(key, read.LastKey), from) < 0 : before)
                    continue;

                yield return (key, chunk, header);
            }
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


        // The overlay iterator first, then the decision, then the chunk iterator: see the class comment. The decision must
        // follow the overlay iterator: the job ends overlay-only reads before its first overlay delete, so a read that still
        // finds them on has an overlay snapshot from before that delete, which holds every row. (LevelDB creates an
        // iterator and applies a write under its database mutex, so the order is a happens-before order.)
        private IRowCursor OpenCursor()
        {
            var overlay = _openedDb.Native.CreateIterator(_readOptions);
            Iterator chunks = null;

            SnapShot snapshot = null;
            ReadOptions options = null;

            try
            {
                BetweenReadIterators?.Invoke();

                if (ReadsOverlayOnly)
                    return new NativeRowCursor(overlay);

                var db = _chunks.Native;

                // One explicit snapshot for the chunk iterator and the watermark lookups of the cursor (#1805).
                snapshot = db.CreateSnapshot();
                options = new ReadOptions { Snapshot = snapshot };
                chunks = db.CreateIterator(options);
                Interlocked.Increment(ref _chunkIteratorsOpened);

                return new MergedRowCursor(new NativeRowCursor(overlay), new ChunkRowCursor(chunks, db, options, snapshot, _reader, _formatter));
            }
            catch
            {
                chunks?.Dispose();
                options?.Dispose();
                snapshot?.Dispose();
                overlay.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Removes the chunk rows in [from, to]. A prefix of one sensor is marked (<see cref="TryPrefixDelete"/>, #1805).
        /// Otherwise chunks wholly inside are deleted, a chunk on a border is re-encoded with the rows outside the range
        /// (and above its sensor's watermark) and stored under the key of its new first row. Everything is one synced
        /// write batch, so a crash leaves either every chunk as it was or every chunk rewritten.
        /// False, with the failure logged and counted, when a chunk on the border cannot be rewritten.
        /// </summary>
        private bool RewriteChunks(byte[] from, byte[] to)
        {
            if (KeyOrder.Compare(from, to) > 0)
                return true;

            lock (_chunkWriteLock)
            {
                // A prefix of one sensor (retention, clear history, sensor removal): marked, nothing re-encoded.
                if (TryPrefixDelete(from, to) is { } done)
                {
                    if (done)
                        AfterChunkRewrite?.Invoke();

                    return done;
                }

                var deletes = new List<byte[]>();
                var puts = new List<(byte[] key, byte[] chunk)>();
                byte[] unreadableWatermarkOf = null;

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

                        // Rows at or below the sensor's watermark are already deleted: a rewrite drops them for good,
                        // so no chunk ever lies wholly below the watermark. An unreadable watermark refuses the delete.
                        long? watermark;

                        try
                        {
                            watermark = _reader.ReadWatermark(_chunks.Native, null, key);
                        }
                        catch (SealedChunkReadException)
                        {
                            unreadableWatermarkOf = key;
                            break;
                        }

                        var kept = rows.FindAll(row => !InRange(KeyOrder.RowKey(key, row.Key), from, to) && (watermark is not { } w || row.Key > w));

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

                if (unreadableWatermarkOf is not null)
                {
                    RefuseForUnreadableWatermark(unreadableWatermarkOf, from, to);
                    return false;
                }

                if (deletes.Count > 0)
                {
                    OnBeforeDelete();

                    using var batch = new WriteBatch();

                    // Deletes first: a re-encoded chunk can keep the key of the chunk it replaces.
                    foreach (var key in deletes)
                        batch.Delete(key);
                    foreach (var (key, chunk) in puts)
                    {
                        batch.Put(key, chunk);
                        CountChunkWrite(key, chunk);
                    }

                    _chunks.Native.Write(batch, _syncWrite);
                }
            }

            AfterChunkRewrite?.Invoke();

            return true;
        }

        /// <summary>
        /// A delete of a prefix of one sensor's history in this week (#1805): <paramref name="from"/> and
        /// <paramref name="to"/> are keys of the same sensor and no live chunk row of it lies below <paramref name="from"/>.
        /// That is what retention, clear history and sensor removal send (<c>DatabaseCore.ClearSensorValues</c> with the
        /// sensor's <c>From</c>, which is the previous cutoff or the oldest row, or <c>DateTime.MinValue</c>). Nothing is
        /// decoded or re-encoded: in one synced batch the chunks that lie wholly at or below <paramref name="to"/> are
        /// deleted and, if a chunk still straddles <paramref name="to"/>, the sensor's watermark is raised to it (never
        /// lowered); a watermark no chunk reaches below any more is deleted. Null when the range is not such a prefix
        /// (the caller rewrites); false when refused (an unreadable watermark under a straddling chunk), logged and counted.
        /// Runs under the chunk write lock.
        /// </summary>
        private bool? TryPrefixDelete(byte[] from, byte[] to)
        {
            if (from.Length != KeyOrder.KeyLength || to.Length != KeyOrder.KeyLength ||
                !from.AsSpan(0, KeyOrder.SensorIdLength).SequenceEqual(to.AsSpan(0, KeyOrder.SensorIdLength)))
                return null;

            var db = _chunks.Native;
            var watermarkKey = SensorWatermark.Key(from);
            var toTick = KeyOrder.Tick(to);

            long? watermark;
            var unreadableWatermark = false;

            try
            {
                watermark = _reader.ReadWatermark(db, null, from);
            }
            catch (SealedChunkReadException)
            {
                watermark = null;
                unreadableWatermark = true;
            }

            // The sensor's chunk keys, in order (the keys alone: no chunk is read here).
            var chunks = new List<byte[]>();

            using (var iterator = db.CreateIterator(_readOptions))
            {
                for (iterator.Seek(KeyOrder.RowKey(from, 0)); iterator.IsValid; iterator.Next())
                {
                    var key = iterator.Key();

                    if (key.Length != KeyOrder.KeyLength || !key.AsSpan(0, KeyOrder.SensorIdLength).SequenceEqual(watermarkKey))
                        break;

                    chunks.Add(key);

                    // One chunk past `to` is enough: it bounds the rows of the last chunk at or below `to`.
                    if (KeyOrder.Compare(key, to) > 0)
                        break;
                }
            }

            // No live chunk row below `from`: the first chunk starts at or after it, or it reaches below the watermark
            // and `from` is at most one tick above the watermark (its live rows all are). With an unreadable watermark
            // only the first rule can tell.
            if (chunks.Count > 0)
            {
                var first = chunks[0];
                var firstLiveAtOrAfterFrom = watermark is { } w && KeyOrder.Tick(first) <= w
                    ? KeyOrder.Tick(from) <= w + 1
                    : KeyOrder.Compare(first, from) >= 0;

                if (!firstLiveAtOrAfterFrom)
                    return null;
            }

            PrefixDeletes++;

            var deletes = new List<byte[]>();
            var straddler = false;

            for (int i = 0; i < chunks.Count && KeyOrder.Compare(chunks[i], to) <= 0; i++)
            {
                var key = chunks[i];
                var next = i + 1 < chunks.Count ? chunks[i + 1] : null;

                // The rows of a chunk lie below the next chunk's key, and in this week (see ProvablyInside).
                if (next is not null && KeyOrder.Compare(next, to) <= 0 || To > 0 && To - 1 <= toTick)
                {
                    deletes.Add(key);
                    continue;
                }

                // The last chunk at or below `to`: its verified header says whether its last row is too.
                ChunkHeader? header = null;

                try
                {
                    header = _reader.ReadHeader(key, db.Get(key, _readOptions));
                }
                catch (SealedChunkReadException)
                {
                    // Unreadable: kept, and the watermark covers whatever of it the delete reaches.
                }

                if (header is { } read && read.LastKey <= toTick)
                    deletes.Add(key);
                else
                    straddler = true;
            }

            // The list ends with the first chunk past `to` if there is one, so an empty rest means no chunk is left.
            var remaining = chunks.Where(key => !deletes.Contains(key)).ToList();
            byte[] newWatermark = null;
            var deleteWatermark = false;

            if (unreadableWatermark)
            {
                // No chunk of the sensor left to hide rows of: the unreadable watermark goes with them. Otherwise it
                // stays (it may hide rows of the remaining chunks), and a chunk straddling the cutoff refuses the delete.
                if (remaining.Count == 0)
                    deleteWatermark = true;
                else if (straddler)
                {
                    RefuseForUnreadableWatermark(from, from, to);
                    return false;
                }
            }
            else if (straddler)
            {
                var raised = Math.Max(watermark ?? toTick, toTick);

                if (raised != watermark)
                    newWatermark = SensorWatermark.Encode(watermarkKey, raised);
            }
            else if (watermark is { } current && !remaining.Any(key => KeyOrder.Tick(key) <= current))
            {
                deleteWatermark = true; // no chunk reaches below it any more
            }

            if (deletes.Count == 0 && newWatermark is null && !deleteWatermark)
                return true;

            OnBeforeDelete();

            using var batch = new WriteBatch();

            foreach (var key in deletes)
                batch.Delete(key);

            if (newWatermark is not null)
            {
                batch.Put(watermarkKey, newWatermark);
                CountChunkWrite(watermarkKey, newWatermark);
            }
            else if (deleteWatermark)
                batch.Delete(watermarkKey);

            db.Write(batch, _syncWrite);

            return true;
        }

        // The sensors whose deletes were refused for an unreadable watermark, for the log (once each) and the attention line.
        private readonly SortedSet<Guid> _refusedSensors = [];
        private volatile string _deleteRefusal;

        /// <summary>
        /// Set once a delete of this week was refused because a sensor's delete watermark cannot be read (#1805 review W2):
        /// what an operator has to do, for the sealing job's "weeks needing attention". Null otherwise.
        /// </summary>
        internal string DeleteRefusal => _deleteRefusal;

        // Refusing is the safe default: which rows of the sensor's partly deleted chunk are live is unknown. Every refusal
        // is counted; the error is logged once per sensor and opened week, later ones at Debug. The exit is an unseal,
        // which restores every row of the sensor's chunks (WeekSealingJob.CopyChunkRowsToOverlay).
        private void RefuseForUnreadableWatermark(byte[] sensorKey, byte[] from, byte[] to)
        {
            SealedHistoryDiagnostics.CountChunkRewriteFailure();

            var sensor = new Guid(sensorKey.AsSpan(0, KeyOrder.SensorIdLength));
            bool first;
            string sensors;

            lock (_refusedSensors)
            {
                first = _refusedSensors.Add(sensor);
                sensors = string.Join(", ", _refusedSensors);
            }

            var message = $"Range delete [{from.GetString()}, {to.GetString()}] on sealed week {SealedName} refused: the delete watermark of sensor {sensor} cannot be read, so which of its chunk rows are deleted is unknown; nothing was deleted";

            if (first)
                _logger.Error(message + " (logged once per sensor; every refusal is counted in the chunk rewrite failures)");
            else
                _logger.Debug(message);

            _deleteRefusal = $"the delete watermark of sensor(s) {sensors} cannot be read: retention and clear history of those sensors in this week are refused and their rows stay. " +
                             "Unseal this week: the unseal restores every row of their chunks (also rows the lost watermark hid), and the deletes then go through.";
        }

        /// <summary>Prefix deletes taken by the watermark path since the week was opened (tests).</summary>
        internal long PrefixDeletes { get; private set; }

        /// <summary>Puts into the chunk database by range deletes since the week was opened, and their bytes (tests).</summary>
        internal long ChunkPuts { get; private set; }

        internal long ChunkBytesWritten { get; private set; }

        private void CountChunkWrite(byte[] key, byte[] value)
        {
            ChunkPuts++;
            ChunkBytesWritten += key.Length + value.Length;
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

                if (SensorWatermark.IsKey(key))
                    continue;

                if (KeyOrder.Compare(key, to) > 0)
                    yield break;

                // The chunk before `from` of another sensor: all its rows are below `from`, so it is not read.
                if (KeyOrder.Compare(key, from) < 0 && from.Length >= KeyOrder.SensorIdLength &&
                    !key.AsSpan(0, KeyOrder.SensorIdLength).SequenceEqual(from.AsSpan(0, KeyOrder.SensorIdLength)))
                    continue;

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
        // key, belong to the key's sensor, end before the next chunk's key (chunks never overlap), and lie in this
        // week. The week bound is an invariant of the row database the chunks were sealed from: every row of a
        // weekly database has From <= tick < To, where To is the next Monday 00:00 (DateTimeMethods.GetEndOfWeek),
        // because DatabaseCore.AddSensorValue picks the week by GetDatabaseByTime(value.Time.Ticks) and keys the
        // row with that same tick, and MergeSensorValues copies a row within its week under the same tick; no
        // other code writes a weekly database. Leaves the iterator where it was.
        private bool ProvablyInside(Iterator iterator, byte[] key, byte[] from, byte[] to)
        {
            if (key.Length != KeyOrder.KeyLength || KeyOrder.Compare(key, from) < 0)
                return false;

            if (To > 0 && KeyOrder.Compare(KeyOrder.RowKey(key, To - 1), to) <= 0)
                return true;

            iterator.Next();

            var next = iterator.IsValid ? iterator.Key() : null;

            if (iterator.IsValid)
                iterator.Prev();
            else
                iterator.SeekToLast();

            return next is not null && KeyOrder.Compare(next, to) <= 0;
        }

        // The row database's key checks (ByteArrayExtensions: StartsWith, and length-first order) on the merged
        // cursor. They go through the cursor rather than its Key, so the cursor answers them without deciding which
        // child holds the current row when both children agree (see MergedRowCursor); an overlay row is compared
        // exactly as the row database compares it, quirks included.
        private static bool StartsWith(IRowCursor cursor, byte[] prefix) => cursor.KeyStartsWith(prefix);

        private static bool Le(IRowCursor cursor, byte[] other) => cursor.CompareKeyAsRowDatabase(other) <= 0;

        private static bool Lt(IRowCursor cursor, byte[] other) => cursor.CompareKeyAsRowDatabase(other) < 0;

        private static bool Ge(IRowCursor cursor, byte[] other) => cursor.CompareKeyAsRowDatabase(other) >= 0;

        private static bool Gt(IRowCursor cursor, byte[] other) => cursor.CompareKeyAsRowDatabase(other) > 0;

        private static bool InRange(byte[] key, byte[] from, byte[] to) => KeyOrder.Compare(key, from) >= 0 && KeyOrder.Compare(key, to) <= 0;
    }
}
