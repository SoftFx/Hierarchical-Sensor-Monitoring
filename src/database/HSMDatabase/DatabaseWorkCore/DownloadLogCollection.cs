using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMDatabase.LevelDB;
using NLog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.Json;

namespace HSMDatabase.DatabaseWorkCore
{
    // The download log database (ADR-0022 point 15). Key: UTC ticks (8 bytes, big-endian) then the record id (16 bytes), so
    // a time range is a key range and the newest records are the last keys; value: the record as JSON.
    //
    // Like ChangeSetCollection, and for the same reason, WRITE failures propagate: a download that cannot be recorded is
    // refused, so the caller must see the failure. A READ of one corrupt row is skipped so one damaged row cannot hide the
    // rest of the log.
    //
    // The database opens on its first use, not in the constructor: a server that never gives out a large file does not
    // create the folder, and the many test servers that share one folder do not race for its lock. An open that fails is
    // tried again by the next call.
    internal sealed class DownloadLogCollection : IDownloadLogStore, IDisposable
    {
        internal const int KeySize = 24;

        // The number of keys one delete batch removes; RemoveOlderThan repeats until a batch is short.
        private const int RemoveBatchSize = 10_000;

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly object _sync = new();
        private readonly string _path;

        private LevelDBDatabaseAdapter _database;
        private bool _disposed;


        internal DownloadLogCollection(string path)
        {
            _path = path;
        }


        public void Put(DownloadRecordEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            var key = BuildKey(entity.TimeTicks, entity.Id);
            var value = JsonSerializer.SerializeToUtf8Bytes(entity);

            lock (_sync)
            {
                GetDatabase().Put(key, value);
            }
        }

        public List<DownloadRecordEntity> Read(long fromTicks, long toTicks, Guid? productId, int limit)
        {
            var records = new List<DownloadRecordEntity>();

            if (limit <= 0 || fromTicks > toTicks)
                return records;

            var from = BuildBound(fromTicks, 0x00);
            var to = BuildBound(toTicks, 0xFF);

            lock (_sync)
            {
                foreach (var (key, value) in GetDatabase().GetValueKeyPairToFrom(from, to))
                {
                    DownloadRecordEntity entity;

                    try
                    {
                        entity = JsonSerializer.Deserialize<DownloadRecordEntity>(value);
                    }
                    catch (JsonException)
                    {
                        _logger.Error($"Download log: the row at {ReadTicks(key)} ticks cannot be read, skipped");
                        continue;
                    }

                    if (entity is null || (productId is { } product && entity.ProductId != product))
                        continue;

                    records.Add(entity);

                    if (records.Count >= limit)
                        break;
                }
            }

            return records;
        }

        public int RemoveOlderThan(long ticksExclusive)
        {
            var bound = BuildBound(Math.Max(ticksExclusive, 0), 0x00);
            var total = 0;

            lock (_sync)
            {
                var database = GetDatabase();

                while (true)
                {
                    var removed = database.DeleteStartingWithBefore([], bound, RemoveBatchSize);

                    total += removed;

                    if (removed < RemoveBatchSize)
                        break;
                }
            }

            return total;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _database?.Dispose();
                _database = null;
            }
        }


        internal static byte[] BuildKey(long ticks, Guid id)
        {
            var key = new byte[KeySize];

            BinaryPrimitives.WriteInt64BigEndian(key, ticks);
            id.TryWriteBytes(key.AsSpan(sizeof(long)));

            return key;
        }

        // The lowest (fill 0x00) or the highest (fill 0xFF) key of a tick: same length as a record key, so the iterator's
        // length-first comparisons order them as LevelDB does.
        private static byte[] BuildBound(long ticks, byte fill)
        {
            var key = new byte[KeySize];

            BinaryPrimitives.WriteInt64BigEndian(key, ticks);
            key.AsSpan(sizeof(long)).Fill(fill);

            return key;
        }

        private static long ReadTicks(byte[] key) => key.Length >= sizeof(long) ? BinaryPrimitives.ReadInt64BigEndian(key) : -1;

        // Under _sync.
        private LevelDBDatabaseAdapter GetDatabase()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _database ??= new LevelDBDatabaseAdapter(_path);
        }
    }
}
