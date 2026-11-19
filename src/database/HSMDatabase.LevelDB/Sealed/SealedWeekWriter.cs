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


        private SealedWeekWriter(string path)
        {
            _path = path;
            _database = new LevelDBDatabaseAdapter(path);
        }


        /// <summary>
        /// Starts the chunk database of a week that is not sealed yet, always empty. A directory without the marker
        /// is what an interrupted seal left: not data (the source rows are still in the row database), and stale
        /// if those rows changed since, so it is deleted first. A directory with the marker is refused.
        /// </summary>
        public static SealedWeekWriter Open(string sealedPath)
        {
            // Open deletes the directory it is given: anything but a sealed week's chunk directory is refused before
            // it is touched (the week's row database, the database folder).
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sealedPath)));
            if (!name.StartsWith(SealedWeekMarker.DirectoryPrefix + "_", StringComparison.Ordinal))
                throw new ArgumentException($"{sealedPath} is not a sealed week directory ({SealedWeekMarker.DirectoryPrefix}_<from>_<to>)", nameof(sealedPath));

            if (SealedWeekMarker.ExistsIn(sealedPath))
                throw new InvalidOperationException($"{sealedPath} is already a complete sealed week");

            if (Directory.Exists(sealedPath))
                Directory.Delete(sealedPath, recursive: true);

            return new SealedWeekWriter(sealedPath);
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
        /// Closes the chunk database and writes the marker that makes the directory a complete sealed week:
        /// a temporary file, flushed to disk, then renamed over nothing.
        /// </summary>
        public void Complete(string description)
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            CloseDatabase();
            WriteMarker(description);
        }

        /// <summary>
        /// The first half of <see cref="Complete"/>: closes the chunk database (every batch is already synced). The
        /// sealing job (#1804) opens the sealed store over it before it writes the marker, so a store that cannot be
        /// opened leaves no marker behind.
        /// </summary>
        public void CloseDatabase()
        {
            _database?.Dispose();
            _database = null;
        }

        /// <summary>The second half of <see cref="Complete"/>: the marker, once the database is closed.</summary>
        public void WriteMarker(string description)
        {
            if (_database is not null)
                throw new InvalidOperationException("The chunk database must be closed before the marker is written");

            var marker = Path.Combine(_path, SealedWeekMarker.FileName);
            var temporary = marker + ".tmp";

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write($"Sealed chunk format v{ChunkCodec.FormatVersion}, encoder r{ChunkCodec.EncoderRevision} at {DateTime.UtcNow:O}{Environment.NewLine}{description}{Environment.NewLine}");
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, marker, overwrite: false);

            // The rename and the directory itself must survive a power loss before any source row may go (#1804).
            DurableFile.SyncDirectory(_path);
            DurableFile.SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
        }

        public void Dispose()
        {
            _database?.Dispose();
            _database = null;
        }
    }
}
