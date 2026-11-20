using System;
using System.Collections.Generic;
using System.IO;
using HSMDatabase.AccessManager;
using HSMDatabase.SealedHistory;
using LevelDB;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// Writes the chunk database of a sealed week and then its <see cref="SealedWeekMarker"/> (ADR-0020, #1803).
    /// Until <see cref="Complete"/> returns, the directory is not a sealed week: the store ignores a sibling
    /// directory without the marker, so a crash at any point before it leaves the week served from its rows.
    /// Every batch is written with a sync, so the marker never exists over chunks a power loss could take back.
    /// The caller writes chunks while the week's row database still holds their source rows, and deletes those
    /// rows only after <see cref="Complete"/> (the sealing job, #1804, after verification and a keep period).
    /// </summary>
    public sealed class SealedWeekWriter : IDisposable
    {
        private static readonly WriteOptions _syncWrite = new() { Sync = true };

        private readonly string _path;
        private LevelDBDatabaseAdapter _database;


        private SealedWeekWriter(string path, string sealingNote)
        {
            _path = path;

            // hsm-sealing goes in before the database exists: a chunk directory without it was not left by a seal and is
            // never deleted by the next one (#1804, SealedWeekStatus.MayDeleteChunkDirectory).
            Directory.CreateDirectory(path);
            DurableFile.WriteAtomic(Path.Combine(path, SealedWeekStages.SealingFileName), sealingNote, strict: true);
            DurableFile.SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(path)), strict: true);

            _database = new LevelDBDatabaseAdapter(path);
        }


        /// <summary>
        /// Starts the chunk database of a week that is not sealed yet, always empty. An existing directory is deleted
        /// only when it is provably the remains of an interrupted seal (<see cref="SealedWeekStatus.MayDeleteChunkDirectory"/>:
        /// the source rows are still in the row database, and the leftover is stale if they changed since). Any other
        /// existing directory is refused untouched: it may hold the only copy of the week (#1804, review S1).
        /// </summary>
        public static SealedWeekWriter Open(string sealedPath, string rowPath = null, string sealingNote = null)
        {
            // Open deletes the directory it is given: anything but a sealed week's chunk directory is refused before
            // it is touched (the week's row database, the database folder).
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sealedPath)));
            if (!name.StartsWith(SealedWeekMarker.DirectoryPrefix + "_", StringComparison.Ordinal))
                throw new ArgumentException($"{sealedPath} is not a sealed week directory ({SealedWeekMarker.DirectoryPrefix}_<from>_<to>)", nameof(sealedPath));

            if (SealedWeekMarker.ExistsIn(sealedPath))
                throw new InvalidOperationException($"{sealedPath} is already a complete sealed week");

            if (Directory.Exists(sealedPath))
            {
                var status = SealedWeekStages.Read(rowPath ?? string.Empty, sealedPath);

                if (status.Stage != SealedWeekStage.IncompleteSeal || !status.MayDeleteChunkDirectory)
                    throw new WeekNeedsAttentionException(sealedPath, status.Attention ?? $"{sealedPath} is in stage {status.Stage}; it is not deleted");

                Directory.Delete(sealedPath, recursive: true);
            }

            return new SealedWeekWriter(sealedPath, sealingNote ?? $"Sealing started at {DateTime.UtcNow:O}.{Environment.NewLine}");
        }


        /// <summary>
        /// Stores the chunks of one sensor, as <see cref="ChunkCodec.Encode"/> returned them, under
        /// <c>sensorId | firstKey</c>, in one synced batch. The chunks of a sensor must not overlap
        /// the ones already stored for it.
        /// </summary>
        public void WriteChunks(Guid sensorId, IReadOnlyList<byte[]> chunks)
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            using var batch = new WriteBatch();

            foreach (var chunk in chunks)
                batch.Put(new DbKey(sensorId, ChunkCodec.ReadHeader(chunk).FirstKey).ToBytes(), chunk);

            _database.Native.Write(batch, _syncWrite);
        }

        /// <summary>
        /// The chunks stored for <paramref name="sensorId"/>, as they are on disk now, in key order: the sealing job
        /// (#1804) reads back what it wrote and verifies it before the week is marked.
        /// </summary>
        public IEnumerable<(byte[] key, byte[] chunk)> ReadChunks(Guid sensorId)
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            var prefix = sensorId.ToByteArray();

            using var options = new ReadOptions();
            using var iterator = _database.Native.CreateIterator(options);

            for (iterator.Seek(new DbKey(sensorId, 0L).ToBytes()); iterator.IsValid; iterator.Next())
            {
                var key = iterator.Key();

                if (key.Length < prefix.Length || !key.AsSpan(0, prefix.Length).SequenceEqual(prefix))
                    yield break;

                yield return (key, iterator.Value());
            }
        }

        /// <summary>Removes every chunk of the sensors in <paramref name="sensorIds"/>, in one synced batch: they stay rows.</summary>
        public void DeleteChunks(IEnumerable<Guid> sensorIds)
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            using var batch = new WriteBatch();
            var any = false;

            foreach (var sensorId in sensorIds)
                foreach (var (key, _) in ReadChunks(sensorId))
                {
                    batch.Delete(key);
                    any = true;
                }

            if (any)
                _database.Native.Write(batch, _syncWrite);
        }

        /// <summary>
        /// Compacts the chunk database (#1805): the write-ahead log the chunk batches went through is flushed into a
        /// table file and the tables are merged, so the directory is about the size of its chunks. A sealed week's chunk
        /// directory is then read-only for a long time, and the background compaction does not visit it before its
        /// source rows are removed (the week's row database keeps its compaction marker: a seal changes no row). Changes
        /// no chunk: a seal stopped during or after it is restarted from scratch like any interrupted seal.
        /// </summary>
        public void Compact()
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            _database.Compact();
        }

        /// <summary>
        /// Compacts and closes the chunk database and writes the marker that makes the directory a complete sealed
        /// week: a temporary file, flushed to disk, then renamed over nothing.
        /// </summary>
        public void Complete(string description)
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            Compact();
            CloseDatabase();
            WriteMarker(description);
        }

        /// <summary>Closes the chunk database (every batch is already synced).</summary>
        public void CloseDatabase()
        {
            _database?.Dispose();
            _database = null;
        }

        /// <summary>
        /// Hands the open chunk database over (the sealing job gives it to the sealed store, #1804, so the database is not
        /// closed and opened again inside the swap); the writer no longer uses or disposes it.
        /// </summary>
        public LevelDBDatabaseAdapter DetachDatabase()
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            var database = _database;
            _database = null;

            return database;
        }

        /// <summary>
        /// The marker, once the database is closed or handed over: a temporary file flushed to disk, renamed over nothing
        /// (with write-through on Windows) and the directory synced (Linux, macOS). Throws if the rename cannot be made
        /// durable: the caller then removes the marker again and the week stays rows.
        /// </summary>
        public void WriteMarker(string description)
        {
            if (_database is not null)
                throw new InvalidOperationException("The chunk database must be closed or handed over before the marker is written");

            DurableFile.WriteAtomic(Path.Combine(_path, SealedWeekMarker.FileName),
                $"Sealed chunk format v{ChunkCodec.FormatVersion}, encoder r{ChunkCodec.EncoderRevision} at {DateTime.UtcNow:O}{Environment.NewLine}{description}{Environment.NewLine}",
                strict: true, overwrite: false);
        }

        public void Dispose()
        {
            _database?.Dispose();
            _database = null;
        }
    }
}
