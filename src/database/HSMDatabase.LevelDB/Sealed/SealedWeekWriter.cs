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
        /// Closes the chunk database and writes the marker that makes the directory a complete sealed week:
        /// a temporary file, flushed to disk, then renamed over nothing.
        /// </summary>
        public void Complete(string description)
        {
            ObjectDisposedException.ThrowIf(_database is null, this);

            _database.Dispose();
            _database = null;

            var marker = Path.Combine(_path, SealedWeekMarker.FileName);
            var temporary = marker + ".tmp";

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write($"Sealed chunk format v{ChunkCodec.FormatVersion} at {DateTime.UtcNow:O}{Environment.NewLine}{description}{Environment.NewLine}");
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, marker, overwrite: false);
        }

        public void Dispose()
        {
            _database?.Dispose();
            _database = null;
        }
    }
}
