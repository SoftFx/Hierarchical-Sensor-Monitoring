using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.SealedHistory;
using HSMServer.Core.DataLayer;
using LevelDB;
using NLog;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>The points where a test can interrupt the sealing job (#1804) to stand for a crash.</summary>
    public static class SealingCheckpoints
    {
        public const string SealOpened = "seal/opened";
        public const string SealSensorWritten = "seal/sensor-written";
        public const string SealEncoded = "seal/encoded";
        public const string SealMarked = "seal/marked";
        public const string SealBeforeTail = "seal/before-tail";
        public const string SealSwapped = "seal/swapped";

        public const string RemoveBatch = "remove/batch";
        public const string RemoveMarked = "remove/marked";

        public const string FoldEncoded = "fold/encoded";
        public const string FoldCommitted = "fold/committed";
        public const string FoldBatch = "fold/batch";
        public const string FoldRecorded = "fold/recorded";

        public const string UnsealStarted = "unseal/started";
        public const string UnsealCopied = "unseal/copied";
        public const string UnsealMarked = "unseal/unsealed-marker";
        public const string UnsealSwapped = "unseal/swapped";
        public const string UnsealDone = "unseal/done";
    }


    /// <summary>
    /// The work of the sealing job on one week (ADR-0020, #1804): seal it, remove its source rows after the keep
    /// period, fold its overlay into its chunks, unseal it. Each method can stop or fail at any point and leaves the week
    /// correct; the next call resumes. The caller (DatabaseCore) runs at most one of them at a time across the whole
    /// database, never during a compaction, and is the only one that swaps a week (see <see cref="SensorValuesWeek"/>).
    /// </summary>
    public sealed class WeekSealingJob
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly WriteOptions _syncWrite = new() { Sync = true };


        // Test seam: changes the chunks of a sensor between encoding and writing, so a test can make a seal's verification fail.
        public Func<Guid, List<byte[]>, List<byte[]>> ChunksBeforeWrite { get; set; }

        private readonly MemoryPackFormatter _formatter = new();
        private readonly SensorValuesWeek _week;
        private readonly HistorySealingPassReport _report;
        private readonly Func<bool> _shouldStop;
        private readonly Action<string> _checkpoint;


        private string RowPath => _week.Name;

        private string SealedPath => _week.SealedName;


        public WeekSealingJob(SensorValuesWeek week, HistorySealingPassReport report, Func<bool> shouldStop, Action<string> checkpoint)
        {
            _week = week;
            _report = report;
            _shouldStop = shouldStop ?? (() => false);
            _checkpoint = checkpoint ?? (_ => { });
        }


        #region Seal

        /// <summary>
        /// Seals a week served as rows: per sensor, encode its rows from one snapshot, write the chunks, read them back
        /// and compare them field by field with the source rows. Then, under the week's exclusive gate, drop the
        /// chunks of every sensor a range delete touched meanwhile, write the markers (the keep period, the sensors left
        /// as rows, finally <c>hsm-sealed</c>) and swap the week to the sealed store. False when stopped for a shutdown
        /// (the chunk directory is then incomplete: never read, started again by the next seal).
        /// </summary>
        public bool Seal(DateTime utcNow, DateTime keepSourceRowsUntilUtc)
        {
            if (_week.Current is not SensorValuesDatabaseWorker rows)
                throw new InvalidOperationException($"{RowPath} is not served as rows; it cannot be sealed");

            // Refuses (untouched) a chunk directory that is not provably the remains of an interrupted seal.
            using var writer = SealedWeekWriter.Open(SealedPath, RowPath,
                $"Sealing started at {utcNow:O} by the sealing job (#1804).{Environment.NewLine}" +
                $"Until {SealedWeekMarker.FileName} appears this directory is not read, and the next seal starts it again.{Environment.NewLine}");

            _checkpoint(SealingCheckpoints.SealOpened);

            var log = _week.OpenDeleteLog();

            try
            {
                var rowsLeft = new RowsLeftRecord();
                var sealedSensors = new Dictionary<Guid, (long rows, long sourceBytes, long chunkBytes, int chunks)>();

                using (var snapshot = rows.RowDatabase.Native.CreateSnapshot())
                using (var options = new ReadOptions { Snapshot = snapshot })
                {
                    foreach (var sensorId in Sensors(rows.RowDatabase.Native, options))
                    {
                        if (_shouldStop())
                            return false;

                        if (SealSensor(writer, rows.RowDatabase.Native, options, sensorId, rowsLeft) is { } stats)
                            sealedSensors[sensorId] = stats;

                        _checkpoint(SealingCheckpoints.SealSensorWritten);
                    }
                }

                _checkpoint(SealingCheckpoints.SealEncoded);

                if (_shouldStop())
                    return false;

                // Everything that does not have to be atomic with the delete-log check happens before the gate: the record of
                // the sensors left as rows, and the sealed store, built over the writer's still open chunk database (it is
                // not closed and opened again) and over the week's row database. Not current yet, so nobody reads it.
                rowsLeft.Write(SealedPath);

                var store = new SealedSensorValuesDatabase(RowPath, SealedPath, _week.From, _week.To, rows.RowDatabase, writer.DetachDatabase());
                var swapped = false;
                var marker = Path.Combine(SealedPath, SealedWeekMarker.FileName);

                try
                {
                    // Held only for what must be atomic with the check: the check (and the rare drop of the chunks a delete
                    // touched), the marker, the swap.
                    Exclusive("seal commit", () =>
                    {
                        // A delete that started after the log was opened may have removed rows these chunks hold: those
                        // sensors stay rows (their rows are complete, the delete applied to them) and are retried later.
                        var touched = sealedSensors.Keys.Where(log.Touches).ToList();

                        if (touched.Count > 0)
                        {
                            DeleteChunksOf(store.ChunkDatabase.Native, touched);

                            foreach (var sensorId in touched)
                            {
                                sealedSensors.Remove(sensorId);
                                _report.LeaveAsRows(LeftAsRows.DeletedDuringSeal);
                            }
                        }

                        foreach (var (_, stats) in sealedSensors)
                        {
                            _report.RowsSealed += stats.rows;
                            _report.SourceBytes += stats.sourceBytes;
                            _report.ChunkBytes += stats.chunkBytes;
                            _report.SensorsSealed++;
                        }

                        try
                        {
                            writer.WriteMarker($"Sealed by the sealing job (#1804): {_report.RowsSealed} rows of {sealedSensors.Count} sensors " +
                                               $"in {sealedSensors.Values.Sum(s => s.chunks)} chunks ({_report.SourceBytes} -> {_report.ChunkBytes} bytes); " +
                                               $"{_report.SensorsLeftAsRowsCount} sensors left as rows.{Environment.NewLine}" +
                                               $"{SealedWeekStages.KeepLinePrefix}{keepSourceRowsUntilUtc:O}");
                        }
                        catch
                        {
                            // A marker whose rename could not be made durable is taken back: the week stays rows, and the
                            // directory (still with hsm-sealing) is an interrupted seal that the next attempt restarts.
                            if (File.Exists(marker))
                                File.Delete(marker);

                            throw;
                        }

                        _checkpoint(SealingCheckpoints.SealMarked);

                        _week.Swap(_ => store);
                        swapped = true;
                    });
                }
                finally
                {
                    if (!swapped)
                    {
                        store.ReleaseRowDatabase();
                        store.Dispose();
                    }
                }

                _checkpoint(SealingCheckpoints.SealBeforeTail);

                // The tail: the keep file (until it exists the keep time is read from the marker's own keep line), then
                // hsm-sealing goes. Whatever is not done here is done by the next pass, and nothing deletes an overlay row
                // before it is done (see CompleteSealTail).
                CompleteSealTail(SealedPath, RowPath);

                _checkpoint(SealingCheckpoints.SealSwapped);

                return true;
            }
            finally
            {
                _week.CloseDeleteLog(log);
            }
        }

        // One sensor of a seal: encode from the snapshot, write, read back, verify. Null when the sensor stays rows.
        private (long rows, long sourceBytes, long chunkBytes, int chunks)? SealSensor(SealedWeekWriter writer, DB rows, ReadOptions options, Guid sensorId, RowsLeftRecord rowsLeft)
        {
            long count = 0;

            foreach (var _ in SensorRows(rows, options, sensorId))
                count++;

            IEnumerable<SourceRow> Source() => OverlayRows(rows, options, sensorId);

            var encoded = Encode(sensorId, Source, count, exactCount: true, rowsLeft, () => Fingerprint(rows, options, sensorId));

            if (encoded is not { } result)
                return null;

            var chunks = ChunksBeforeWrite?.Invoke(sensorId, result.chunks) ?? result.chunks;

            writer.WriteChunks(sensorId, chunks);

            // Read back what was written and compare it with the source rows, field by field.
            var written = writer.ReadChunks(sensorId).ToList();
            var difference = written.Count != chunks.Count
                ? $"{written.Count} chunks read back, {chunks.Count} written"
                : written.Select((chunk, i) => chunk.chunk.AsSpan().SequenceEqual(chunks[i]) ? null : $"chunk {i} reads back different bytes").FirstOrDefault(d => d is not null)
                  ?? Verify(sensorId, written, Source());

            if (difference is not null)
            {
                writer.DeleteChunks([sensorId]);
                _report.VerificationFailures++;
                Leave(rowsLeft, sensorId, LeftAsRows.VerificationFailed, Fingerprint(rows, options, sensorId), difference);

                return null;
            }

            return (count, result.sourceBytes, written.Sum(c => (long)c.key.Length + c.chunk.Length), written.Count);
        }

        #endregion

        #region Source rows

        /// <summary>
        /// The keep period of a sealed week ended: every overlay row that still equals its chunk row (by the fidelity
        /// rule) is deleted, chunk by chunk, each step under the week's exclusive gate. A row that differs (written
        /// after the seal) stays; a chunk that cannot be read keeps all its rows and the week stays in this stage.
        /// The marker <c>hsm-source-rows-removed</c> is written only when every chunk was read. False when stopped.
        /// </summary>
        public bool RemoveSourceRows(DateTime utcNow)
        {
            var store = SealedStore();

            // The marker read from disk when the store was opened (here or by the seal's swap) is what this relies on.
            if (!SealedWeekMarker.ExistsIn(SealedPath))
                throw new InvalidOperationException($"{SealedPath} has no {SealedWeekMarker.FileName}; no source row is removed");

            CompleteSealTail(SealedPath, RowPath);

            var (completed, everyChunkRead) = RemoveOverlayRowsEqualToChunks(store, null, SealingCheckpoints.RemoveBatch);

            if (!completed)
                return false;

            if (!everyChunkRead)
                throw new WeekNeedsAttentionException(RowPath, $"{_report.UnreadableChunks} chunks of {SealedPath} could not be read (see the chunk read errors in the log); their source rows stay and the removal is retried daily. Clear the history range of the sensor named in the error to remove the chunk.");

            DurableFile.WriteAtomic(Path.Combine(SealedPath, SealedWeekStages.SourceRowsRemovedFileName),
                $"Source rows removed at {utcNow:O} by the sealing job (#1804): {_report.OverlayRowsRemoved} rows that equalled their chunk rows.{Environment.NewLine}");

            _checkpoint(SealingCheckpoints.RemoveMarked);

            return true;
        }

        // Deletes the overlay rows equal to the rows of the chunks of `sensorId` (every chunk when null), one chunk per
        // step under the exclusive gate, reading the chunk again inside the step: a write or delete between steps is
        // never undone.
        private (bool completed, bool everyChunkRead) RemoveOverlayRowsEqualToChunks(SealedSensorValuesDatabase store, Guid? sensorId, string checkpoint)
        {
            byte[] last = null;
            var start = sensorId is { } id ? new DbKey(id, 0L).ToBytes() : null;
            var prefix = sensorId?.ToByteArray();
            var everyChunkRead = true;

            while (true)
            {
                if (_shouldStop())
                    return (false, everyChunkRead);

                // Checked from disk before every step that deletes overlay rows (#1804 review U1).
                EnsureNotAnInterruptedSeal();

                var finished = Exclusive(sensorId is null ? "source-row removal step" : "fold overlay delete step", () =>
                {
                    using var options = new ReadOptions();
                    using var iterator = store.ChunkDatabase.Native.CreateIterator(options);

                    if (last is not null)
                    {
                        iterator.Seek(last);

                        if (iterator.IsValid && KeyOrder.Equal(iterator.Key(), last))
                            iterator.Next();
                    }
                    else if (start is not null)
                        iterator.Seek(start);
                    else
                        iterator.SeekToFirst();

                    if (!iterator.IsValid)
                        return true;

                    var key = iterator.Key();

                    if (prefix is not null && !HasPrefix(key, prefix))
                        return true;

                    last = key;

                    List<HistoryRow> chunkRows;

                    try
                    {
                        chunkRows = store.ChunkReader.Decode(key, iterator.Value());
                    }
                    catch (SealedChunkReadException)
                    {
                        // Logged and counted by the reader. Its overlay rows stay: they may be all the week has left.
                        _report.UnreadableChunks++;
                        everyChunkRead = false;
                        return false;
                    }

                    using var batch = new WriteBatch();
                    long removed = 0;

                    foreach (var row in chunkRows)
                    {
                        var rowKey = KeyOrder.RowKey(key, row.Key);

                        if (!store.RowDatabase.TryRead(rowKey, out var bytes))
                            continue;

                        BaseValue overlay;

                        try
                        {
                            overlay = _formatter.Deserialize(bytes);
                        }
                        catch (InvalidOperationException)
                        {
                            continue;
                        }

                        if (RowFidelity.FindDifference(new HistoryRow(row.Key, overlay), row) is null)
                        {
                            batch.Delete(rowKey);
                            removed++;
                        }
                    }

                    if (removed > 0)
                    {
                        store.MarkChanged();
                        store.RowDatabase.Native.Write(batch);
                        _report.OverlayRowsRemoved += removed;
                    }

                    return false;
                });

                if (finished)
                    return (true, everyChunkRead);

                _checkpoint(checkpoint);
            }
        }

        #endregion

        #region Fold

        /// <summary>
        /// The overlay rows of a sealed week that a fold would take, and the sensors they belong to: rows of sensors
        /// that are not File sensors and not left as rows with the same rows as when they were left. Only after the
        /// source rows are gone; during the keep period the overlay holds every row anyway.
        /// </summary>
        public (long rows, List<Guid> sensors)? CountFoldableRows()
        {
            var store = SealedStore();
            var rowsLeft = RowsLeftRecord.Read(SealedPath);
            var sensors = new List<Guid>();
            long foldable = 0;

            using var options = new ReadOptions();

            foreach (var sensorId in Sensors(store.RowDatabase.Native, options))
            {
                // A large overlay must not hold a shutdown: the count is then unknown and done again later.
                if (_shouldStop())
                    return null;

                long count = 0;
                var first = true;
                var file = false;
                using var fingerprint = new RowsFingerprint();

                foreach (var (key, value) in SensorRows(store.RowDatabase.Native, options, sensorId))
                {
                    if (first)
                    {
                        first = false;
                        file = IsFileValue(value);

                        if (file)
                            break;
                    }

                    fingerprint.Add(key, value);
                    count++;
                }

                if (file || count == 0 || rowsLeft.Matches(sensorId, fingerprint.Finish()))
                    continue;

                sensors.Add(sensorId);
                foldable += count;
            }

            return (foldable, sensors);
        }

        /// <summary>
        /// Folds the overlay rows of <paramref name="sensors"/> into their chunks: each sensor's rows merged with its
        /// chunks (overlay wins) are encoded from snapshots, verified by decoding, and committed under the exclusive
        /// gate in one synced batch that replaces the sensor's chunks, unless a range delete started on that sensor
        /// meanwhile. Then its overlay rows that equal the new chunk rows are deleted. False when stopped.
        /// </summary>
        public bool Fold(IReadOnlyList<Guid> sensors)
        {
            var store = SealedStore();

            CompleteSealTail(SealedPath, RowPath);
            var rowsLeft = RowsLeftRecord.Read(SealedPath);
            var log = _week.OpenDeleteLog();
            var completed = true;

            try
            {
                using var overlaySnapshot = store.RowDatabase.Native.CreateSnapshot();
                using var overlayOptions = new ReadOptions { Snapshot = overlaySnapshot };
                using var chunkSnapshot = store.ChunkDatabase.Native.CreateSnapshot();
                using var chunkOptions = new ReadOptions { Snapshot = chunkSnapshot };

                foreach (var sensorId in sensors)
                {
                    if (_shouldStop() || !FoldSensor(store, sensorId, overlayOptions, chunkOptions, log, rowsLeft))
                    {
                        completed = false;
                        break;
                    }
                }
            }
            finally
            {
                _week.CloseDeleteLog(log);
            }

            // Written when stopped too: a sensor folded or refused here is not redone for nothing. A crash before it
            // only costs one more attempt at the sensors it would have listed.
            rowsLeft.Write(SealedPath);

            if (completed)
                _checkpoint(SealingCheckpoints.FoldRecorded);

            return completed;
        }

        private bool FoldSensor(SealedSensorValuesDatabase store, Guid sensorId, ReadOptions overlayOptions, ReadOptions chunkOptions, DeleteLog log, RowsLeftRecord rowsLeft)
        {
            var overlay = store.RowDatabase.Native;
            var chunkDb = store.ChunkDatabase.Native;

            var oldChunks = SensorRows(chunkDb, chunkOptions, sensorId).ToList();
            long estimate = 0, oldBytes = 0;

            try
            {
                foreach (var (key, chunk) in oldChunks)
                {
                    estimate += store.ChunkReader.ReadHeader(key, chunk).RowCount;
                    oldBytes += key.Length + chunk.Length;
                }
            }
            catch (SealedChunkReadException)
            {
                _report.UnreadableChunks++;
                Leave(rowsLeft, sensorId, LeftAsRows.UnreadableChunk, Fingerprint(overlay, overlayOptions, sensorId), "a chunk of the sensor cannot be read");
                return true;
            }

            foreach (var _ in SensorRows(overlay, overlayOptions, sensorId))
                estimate++;

            IEnumerable<SourceRow> Source() => MergedRows(store, sensorId, overlayOptions, chunkOptions);

            (List<byte[]> chunks, long sourceBytes)? encoded;

            try
            {
                encoded = Encode(sensorId, Source, estimate, exactCount: false, rowsLeft, () => Fingerprint(overlay, overlayOptions, sensorId));
            }
            catch (SealedChunkReadException)
            {
                _report.UnreadableChunks++;
                Leave(rowsLeft, sensorId, LeftAsRows.UnreadableChunk, Fingerprint(overlay, overlayOptions, sensorId), "a chunk of the sensor cannot be read");
                return true;
            }

            if (encoded is not { } result)
                return true;

            // Verified before anything is replaced: the old chunks are gone once the batch is written.
            var newChunks = result.chunks.Select(chunk => (key: new DbKey(sensorId, ChunkCodec.ReadHeader(chunk).FirstKey).ToBytes(), chunk)).ToList();

            if (Verify(sensorId, newChunks, Source()) is { } difference)
            {
                _report.VerificationFailures++;
                Leave(rowsLeft, sensorId, LeftAsRows.VerificationFailed, Fingerprint(overlay, overlayOptions, sensorId), difference);
                return true;
            }

            _checkpoint(SealingCheckpoints.FoldEncoded);

            if (_shouldStop())
                return false;

            var committed = Exclusive("fold commit", () =>
            {
                // A delete that started on this sensor after the snapshot: the new chunks may hold rows it removed.
                if (log.Touches(sensorId))
                    return false;

                lock (store.ChunkWriteLock)
                {
                    Replace(chunkDb, oldChunks, newChunks);

                    var readBack = SensorRows(chunkDb, null, sensorId).ToList();

                    if (readBack.Count != newChunks.Count || readBack.Where((c, i) => !c.key.AsSpan().SequenceEqual(newChunks[i].key) || !c.value.AsSpan().SequenceEqual(newChunks[i].chunk)).Any())
                    {
                        // Put the old chunks back: the overlay rows are still all there.
                        Replace(chunkDb, readBack, oldChunks);
                        throw new IOException($"the folded chunks of {sensorId} in {SealedPath} read back different; the old chunks were restored");
                    }

                    store.MarkChanged();
                }

                return true;
            });

            if (!committed)
            {
                _report.LeaveAsRows(LeftAsRows.DeletedDuringFold);
                return true;
            }

            rowsLeft.Remove(sensorId);

            _report.RowsSealed += newChunks.Sum(c => (long)ChunkCodec.ReadHeader(c.chunk).RowCount);
            _report.SourceBytes += oldBytes + result.sourceBytes;
            _report.ChunkBytes += newChunks.Sum(c => (long)c.key.Length + c.chunk.Length);
            _report.SensorsSealed++;

            _checkpoint(SealingCheckpoints.FoldCommitted);

            return RemoveOverlayRowsEqualToChunks(store, sensorId, SealingCheckpoints.FoldBatch).completed;
        }

        private static void Replace(DB chunks, IEnumerable<(byte[] key, byte[] chunk)> remove, IEnumerable<(byte[] key, byte[] chunk)> put)
        {
            using var batch = new WriteBatch();

            // Deletes first: a new chunk can have the key of an old one.
            foreach (var (key, _) in remove)
                batch.Delete(key);
            foreach (var (key, chunk) in put)
                batch.Put(key, chunk);

            chunks.Write(batch, _syncWrite);
        }

        #endregion

        #region Unseal

        /// <summary>
        /// Turns the week back into rows, from whatever stage an earlier unseal reached: mark the unseal; copy every
        /// chunk row the overlay does not hold into it (an overlay row wins), one chunk per step under the exclusive
        /// gate, synced; then, under the gate, mark the row directory as unsealed, delete <c>hsm-sealed</c> and swap
        /// the week to rows; last, delete the chunk directory once no read uses it. The marker goes before any chunk,
        /// so a sealed marker never stands over a partly deleted chunk directory. False only when stopped for a shutdown.
        /// The unseal is done once the week is rows and <c>hsm-sealed</c> is gone; the chunk directory is then a leftover,
        /// deleted here if no read still uses it, otherwise by <see cref="DeleteLeftoverChunks"/> later (never waited for).
        /// </summary>
        public bool Unseal(DateTime utcNow)
        {
            var status = SealedWeekStages.Read(RowPath, SealedPath);

            if (status.Stage == SealedWeekStage.UnsealedLeftover)
            {
                DeleteLeftoverChunks();
                return true;
            }

            if (status.Stage is not (SealedWeekStage.SealedKeepingSourceRows or SealedWeekStage.SourceRowsRemoved or SealedWeekStage.Unsealing))
                throw new WeekNeedsAttentionException(RowPath, status.Attention ?? $"{RowPath} is in stage {status.Stage}; there is nothing to unseal");

            if (_week.Current is not SealedSensorValuesDatabase)
                throw new InvalidOperationException($"{SealedPath} is marked sealed but the week is served as rows; restart the server and unseal again");

            // hsm-sealing must be gone before hsm-unsealing appears (a directory with both is never an unsealed leftover).
            CompleteSealTail(SealedPath, RowPath);

            var unsealingMarker = Path.Combine(SealedPath, SealedWeekStages.UnsealingFileName);

            if (!File.Exists(unsealingMarker))
            {
                DurableFile.WriteAtomic(unsealingMarker,
                    $"Unseal started at {utcNow:O} (#1804). The week is sealed while {SealedWeekMarker.FileName} is here; " +
                    $"the unseal copies every chunk row into the row database first.{Environment.NewLine}");

                _checkpoint(SealingCheckpoints.UnsealStarted);
            }

            var store = (SealedSensorValuesDatabase)_week.Current;

            if (!CopyChunkRowsToOverlay(store))
                return false;

            Exclusive("unseal commit", () =>
            {
                // hsm-unsealed before hsm-sealed goes: together with hsm-unsealing it proves the copy finished, which is
                // what lets the leftover directory be deleted (SealedWeekStatus.MayDeleteChunkDirectory).
                WriteUnsealedMarker(utcNow);

                _checkpoint(SealingCheckpoints.UnsealMarked);

                DurableFile.Delete(Path.Combine(SealedPath, SealedWeekMarker.FileName));

                _week.Swap(rowDatabase => new SensorValuesDatabaseWorker(RowPath, _week.From, _week.To, rowDatabase));
            });

            _checkpoint(SealingCheckpoints.UnsealSwapped);

            DeleteLeftoverChunks();

            return true;
        }

        /// <summary>
        /// Deletes the chunk directory a finished unseal left, if the one rule allows it
        /// (<see cref="SealedWeekStatus.MayDeleteChunkDirectory"/>) and no read uses the replaced sealed store any more.
        /// Checks once and never waits: true when deleted, false when a read still holds it (the caller retries later).
        /// </summary>
        public bool DeleteLeftoverChunks()
        {
            var status = SealedWeekStages.Read(RowPath, SealedPath);

            if (status.Stage != SealedWeekStage.UnsealedLeftover || !status.MayDeleteChunkDirectory)
                throw new WeekNeedsAttentionException(RowPath, status.Attention ?? $"{SealedPath} is in stage {status.Stage}; it is not deleted");

            if (_week.ReplacedInUse > 0)
            {
                _report.Note = $"the week is rows again; {Path.GetFileName(SealedPath)} is still used by a read and is deleted by a later pass";
                _logger.Info($"The unseal of {RowPath} is done; {SealedPath} is still used by a read and is deleted by a later pass");
                return false;
            }

            Directory.Delete(SealedPath, recursive: true);
            DurableFile.SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(SealedPath)));

            _checkpoint(SealingCheckpoints.UnsealDone);

            return true;
        }

        private bool CopyChunkRowsToOverlay(SealedSensorValuesDatabase store)
        {
            byte[] last = null;

            while (true)
            {
                if (_shouldStop())
                    return false;

                var finished = Exclusive("unseal copy step", () =>
                {
                    using var options = new ReadOptions();
                    using var iterator = store.ChunkDatabase.Native.CreateIterator(options);

                    if (last is null)
                        iterator.SeekToFirst();
                    else
                    {
                        iterator.Seek(last);

                        if (iterator.IsValid && KeyOrder.Equal(iterator.Key(), last))
                            iterator.Next();
                    }

                    if (!iterator.IsValid)
                        return true;

                    var key = iterator.Key();
                    last = key;

                    // An unreadable chunk stops the unseal: its rows exist nowhere else.
                    var rows = store.ChunkReader.Decode(key, iterator.Value());

                    using var batch = new WriteBatch();
                    long restored = 0;

                    foreach (var row in rows)
                    {
                        var rowKey = KeyOrder.RowKey(key, row.Key);

                        if (store.RowDatabase.TryRead(rowKey, out _))
                            continue;

                        batch.Put(rowKey, _formatter.Serialize(row.Value));
                        restored++;
                    }

                    if (restored > 0)
                    {
                        store.MarkChanged();
                        store.RowDatabase.Native.Write(batch, _syncWrite);
                        _report.RowsRestored += restored;
                    }

                    return false;
                });

                if (finished)
                    return true;

                _checkpoint(SealingCheckpoints.UnsealCopied);
            }
        }

        private void WriteUnsealedMarker(DateTime utcNow)
        {
            var path = Path.Combine(RowPath, SealedWeekStages.UnsealedFileName);

            if (!File.Exists(path))
                DurableFile.WriteAtomic(path,
                    $"Unsealed at {utcNow:O} (#1804). The sealing job leaves this week as rows; delete this file to let it be sealed again.{Environment.NewLine}");
        }

        #endregion

        #region Encoding and verification

        /// <summary>The reasons a sensor stays rows, as counted in the pass report and written in the record.</summary>
        public static class LeftAsRows
        {
            public const string VerificationFailed = "VerificationFailed";
            public const string UnreadableSourceRow = "UnreadableSourceRow";
            public const string UnreadableChunk = "UnreadableChunk";
            public const string UnexpectedKey = "UnexpectedKey";
            public const string DeletedDuringSeal = "DeletedDuringSeal";
            public const string DeletedDuringFold = "DeletedDuringFold";

            public static string Refused(ChunkRefusalReason reason) => $"Refused:{reason}";
        }


        private readonly record struct SourceRow(byte[] Key, long Tick, BaseValue Value, long Bytes);

        private sealed class UnusableRowException(string reason, string message) : Exception(message)
        {
            public string Reason { get; } = reason;
        }


        // Encodes the rows of one sensor in batches of at most one chunk each, so only one batch of rows is in memory.
        // Null when the sensor stays rows (recorded and counted), or is a File sensor (skipped silently).
        private (List<byte[]> chunks, long sourceBytes)? Encode(Guid sensorId, Func<IEnumerable<SourceRow>> source, long count, bool exactCount,
            RowsLeftRecord rowsLeft, Func<string> fingerprint)
        {
            if (count == 0)
                return null;

            var chunks = new List<byte[]>();
            var batch = new List<HistoryRow>(ChunkCodec.MaxRowsPerChunk);
            var sizes = BatchSizes(count, exactCount).GetEnumerator();
            long sourceBytes = 0;
            var first = true;

            sizes.MoveNext();

            try
            {
                foreach (var row in source())
                {
                    if (first)
                    {
                        first = false;

                        // A File sensor is never sealed (ADR-0020, decision 7): skipped by type, not counted.
                        if (row.Value is FileValue)
                            return null;
                    }

                    batch.Add(new HistoryRow(row.Tick, row.Value));
                    sourceBytes += row.Bytes;

                    if (batch.Count == sizes.Current)
                    {
                        if (!EncodeBatch(sensorId, batch, chunks, rowsLeft, fingerprint))
                            return null;

                        batch.Clear();
                        sizes.MoveNext();
                    }
                }

                if (batch.Count > 0 && !EncodeBatch(sensorId, batch, chunks, rowsLeft, fingerprint))
                    return null;
            }
            catch (UnusableRowException e)
            {
                Leave(rowsLeft, sensorId, e.Reason, fingerprint(), e.Message);
                return null;
            }

            return chunks.Count == 0 ? null : (chunks, sourceBytes);
        }

        private bool EncodeBatch(Guid sensorId, List<HistoryRow> batch, List<byte[]> chunks, RowsLeftRecord rowsLeft, Func<string> fingerprint)
        {
            var encoded = ChunkCodec.Encode(batch);

            if (!encoded.IsSuccess)
            {
                Leave(rowsLeft, sensorId, LeftAsRows.Refused(encoded.Refusal.Reason), fingerprint(), encoded.Refusal.ToString());
                return false;
            }

            chunks.AddRange(encoded.Chunks);
            return true;
        }

        // Sizes of the batches: balanced like the codec's own split when the count is exact (sizes differ by at most
        // one row), otherwise of the size the estimate gives (the last one smaller).
        private static IEnumerable<long> BatchSizes(long count, bool exactCount)
        {
            var chunks = (count + ChunkCodec.MaxRowsPerChunk - 1) / ChunkCodec.MaxRowsPerChunk;

            if (exactCount)
            {
                for (long i = 0; i < chunks; i++)
                    yield return count / chunks + (i < count % chunks ? 1 : 0);
            }
            else
            {
                var size = (count + chunks - 1) / chunks;

                while (true)
                    yield return size;
            }

            while (true)
                yield return long.MaxValue;
        }

        // The decoded chunks, in order, against the source rows: null when every row matches by the fidelity rule
        // and the counts are equal, otherwise what differs.
        private static string Verify(Guid sensorId, IReadOnlyList<(byte[] key, byte[] chunk)> chunks, IEnumerable<SourceRow> source)
        {
            using var rows = source.GetEnumerator();
            long index = 0;

            try
            {
                foreach (var (key, chunk) in chunks)
                {
                    var header = ChunkCodec.ReadHeader(chunk);

                    if (!key.AsSpan().SequenceEqual(new DbKey(sensorId, header.FirstKey).ToBytes()))
                        return $"chunk {header.FirstKey} is stored under another key";

                    foreach (var decoded in ChunkCodec.Decode(chunk))
                    {
                        if (!rows.MoveNext())
                            return $"the chunks hold more rows than the source ({index})";

                        if (RowFidelity.FindDifference(new HistoryRow(rows.Current.Tick, rows.Current.Value), decoded) is { } field)
                            return $"row {index} ({rows.Current.Tick}): {field}";

                        index++;
                    }
                }

                return rows.MoveNext() ? $"the chunks hold {index} rows, the source more" : null;
            }
            catch (ChunkFormatException e)
            {
                return $"a written chunk does not decode: {e.Message}";
            }
        }

        private void Leave(RowsLeftRecord rowsLeft, Guid sensorId, string reason, string fingerprint, string detail)
        {
            _report.LeaveAsRows(reason);
            rowsLeft.Set(sensorId, reason, fingerprint, detail);

            _logger.Warn($"Sealing {RowPath}: sensor {sensorId} stays rows ({reason}): {detail}");
        }

        #endregion

        #region Row streams

        // The rows of one sensor from the overlay, as values; a row that cannot be used stops the sensor.
        private IEnumerable<SourceRow> OverlayRows(DB database, ReadOptions options, Guid sensorId)
        {
            foreach (var (key, value) in SensorRows(database, options, sensorId))
                yield return ToSourceRow(key, value);
        }

        private SourceRow ToSourceRow(byte[] key, byte[] value)
        {
            if (key.Length != KeyOrder.KeyLength)
                throw new UnusableRowException(LeftAsRows.UnexpectedKey, $"a key of {key.Length} bytes");

            BaseValue decoded;

            try
            {
                decoded = _formatter.Deserialize(value);
            }
            catch (InvalidOperationException e)
            {
                throw new UnusableRowException(LeftAsRows.UnreadableSourceRow, $"row {KeyOrder.Tick(key)}: {e.InnerException?.Message ?? e.Message}");
            }

            if (decoded is null)
                throw new UnusableRowException(LeftAsRows.UnreadableSourceRow, $"row {KeyOrder.Tick(key)} is empty");

            return new SourceRow(key, KeyOrder.Tick(key), decoded, key.Length + value.Length);
        }

        // The merged rows of one sensor in a sealed week, from snapshots: overlay and chunk rows in key order, the
        // overlay row winning an equal key. A chunk that cannot be read throws SealedChunkReadException.
        private IEnumerable<SourceRow> MergedRows(SealedSensorValuesDatabase store, Guid sensorId, ReadOptions overlayOptions, ReadOptions chunkOptions)
        {
            using var overlay = OverlayRows(store.RowDatabase.Native, overlayOptions, sensorId).GetEnumerator();
            using var chunks = ChunkRows(store, chunkOptions, sensorId).GetEnumerator();

            var hasOverlay = overlay.MoveNext();
            var hasChunk = chunks.MoveNext();

            while (hasOverlay || hasChunk)
            {
                if (hasOverlay && (!hasChunk || overlay.Current.Tick <= chunks.Current.Tick))
                {
                    if (hasChunk && overlay.Current.Tick == chunks.Current.Tick)
                        hasChunk = chunks.MoveNext();

                    yield return overlay.Current;
                    hasOverlay = overlay.MoveNext();
                }
                else
                {
                    yield return chunks.Current;
                    hasChunk = chunks.MoveNext();
                }
            }
        }

        private static IEnumerable<SourceRow> ChunkRows(SealedSensorValuesDatabase store, ReadOptions options, Guid sensorId)
        {
            foreach (var (key, chunk) in SensorRows(store.ChunkDatabase.Native, options, sensorId))
                foreach (var row in store.ChunkReader.Decode(key, chunk))
                    yield return new SourceRow(null, row.Key, row.Value, 0);
        }

        // Every sensor with a key in the database, in key order: each id once, from the first 16 bytes of its keys.
        private static IEnumerable<Guid> Sensors(DB database, ReadOptions options)
        {
            using var iterator = database.CreateIterator(options);

            iterator.SeekToFirst();

            while (iterator.IsValid)
            {
                var key = iterator.Key();

                if (key.Length < KeyOrder.SensorIdLength)
                {
                    iterator.Next();
                    continue;
                }

                var prefix = key.AsSpan(0, KeyOrder.SensorIdLength).ToArray();

                yield return new Guid(prefix);

                if (!Increment(prefix))
                    yield break;

                iterator.Seek(prefix);
            }
        }

        // The keys of one sensor: those that start with its 16 bytes, from `sensor | 0` on.
        private static IEnumerable<(byte[] key, byte[] value)> SensorRows(DB database, ReadOptions options, Guid sensorId)
        {
            var prefix = sensorId.ToByteArray();
            var ownOptions = options is null ? new ReadOptions() : null;

            try
            {
                using var iterator = database.CreateIterator(options ?? ownOptions);

                for (iterator.Seek(new DbKey(sensorId, 0L).ToBytes()); iterator.IsValid; iterator.Next())
                {
                    var key = iterator.Key();

                    if (!HasPrefix(key, prefix))
                        yield break;

                    yield return (key, iterator.Value());
                }
            }
            finally
            {
                ownOptions?.Dispose();
            }
        }

        private static string Fingerprint(DB database, ReadOptions options, Guid sensorId)
        {
            using var fingerprint = new RowsFingerprint();

            foreach (var (key, value) in SensorRows(database, options, sensorId))
                fingerprint.Add(key, value);

            return fingerprint.Finish();
        }

        private bool IsFileValue(byte[] value)
        {
            try
            {
                return _formatter.Deserialize(value) is FileValue;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool HasPrefix(byte[] key, byte[] prefix) => key.Length >= prefix.Length && key.AsSpan(0, prefix.Length).SequenceEqual(prefix);

        // The next 16-byte prefix in key order; false after the last one.
        private static bool Increment(byte[] prefix)
        {
            for (var i = prefix.Length - 1; i >= 0; i--)
                if (++prefix[i] != 0)
                    return true;

            return false;
        }

        #endregion


        // A step under the week's exclusive gate; how long the gate was held goes into the report (longest per kind).
        private void Exclusive(string what, Action action) => Exclusive(what, () => { action(); return 0; });

        private T Exclusive<T>(string what, Func<T> action)
        {
            var result = _week.Exclusive(what, action, out var held);

            if (!_report.LongestExclusiveHold.TryGetValue(what, out var longest) || held > longest)
                _report.LongestExclusiveHold[what] = held;

            return result;
        }

        private static void DeleteChunksOf(DB chunks, IReadOnlyCollection<Guid> sensors)
        {
            using var batch = new WriteBatch();

            foreach (var sensorId in sensors)
                foreach (var (key, _) in SensorRows(chunks, null, sensorId))
                    batch.Delete(key);

            chunks.Write(batch, _syncWrite);
        }

        /// <summary>
        /// Finishes the tail of a seal (#1804 review U1): a sealed week whose chunk directory still has <c>hsm-sealing</c> or
        /// lacks the keep file (a crash or a failed delete right after the swap) gets the keep file, from the keep line of
        /// <c>hsm-sealed</c>, and loses <c>hsm-sealing</c>, both made durable. Until then the directory would read as an
        /// interrupted seal the moment <c>hsm-sealed</c> were lost. Never throws: a tail that cannot be finished is logged
        /// and the guard of every overlay delete (<see cref="EnsureNotAnInterruptedSeal"/>) refuses to go on.
        /// </summary>
        public static bool CompleteSealTail(string sealedPath, string rowPath)
        {
            var sealing = Path.Combine(sealedPath, SealedWeekStages.SealingFileName);
            var keep = Path.Combine(sealedPath, SealedWeekStages.KeepSourceRowsUntilFileName);

            if (!SealedWeekMarker.ExistsIn(sealedPath) || !File.Exists(sealing) && File.Exists(keep))
                return true;

            try
            {
                if (!File.Exists(keep))
                {
                    var (until, problem) = SealedWeekStages.ReadKeepUntil(sealedPath);

                    if (until is not { } time)
                    {
                        _logger.Error($"Cannot finish the seal of {sealedPath}: {problem}");
                        return false;
                    }

                    DurableFile.WriteAtomic(keep,
                        $"{time:O}{Environment.NewLine}" +
                        $"The source rows of this week stay in {Path.GetFileName(rowPath)} until this UTC time; then the sealing job (#1804) " +
                        $"removes those that still equal their chunk rows. Change the first line to keep them longer.{Environment.NewLine}", strict: true);
                }

                if (File.Exists(sealing))
                    DurableFile.Delete(sealing, strict: true);

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.Error(e, $"Cannot finish the seal of {sealedPath}; no overlay row of the week is deleted until it is finished");
                return false;
            }
        }

        // No overlay row of a sealed week is deleted while its chunk directory could still read as an interrupted seal
        // should hsm-sealed be lost: hsm-sealing must be gone and the keep file present, on disk, now.
        private void EnsureNotAnInterruptedSeal()
        {
            if (File.Exists(Path.Combine(SealedPath, SealedWeekStages.SealingFileName)) ||
                !File.Exists(Path.Combine(SealedPath, SealedWeekStages.KeepSourceRowsUntilFileName)))
                throw new WeekNeedsAttentionException(RowPath,
                    $"{SealedPath} still has {SealedWeekStages.SealingFileName} or lacks {SealedWeekStages.KeepSourceRowsUntilFileName}, and the seal's tail " +
                    "could not be finished (see the error before this one); no overlay row of the week is deleted until it is");
        }

        private SealedSensorValuesDatabase SealedStore() =>
            _week.Current as SealedSensorValuesDatabase ?? throw new InvalidOperationException($"{RowPath} is not served as a sealed week");
    }
}
