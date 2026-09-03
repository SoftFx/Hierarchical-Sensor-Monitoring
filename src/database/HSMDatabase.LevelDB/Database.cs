using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.Extensions;
using LevelDB;
using NLog;
using CompressionLevel = LevelDB.CompressionLevel;
using Exception = System.Exception;

namespace HSMDatabase.LevelDB
{
    public class LevelDBDatabaseAdapter : IEntityDatabase, IDisposable
    {
        private const int OpenDbMaxAttempts = 10;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly DB _database;
        private readonly ReadOptions _iteratorOptions = new();
        private readonly Options _databaseOptions = new()
        {
            CreateIfMissing = true,
            MaxOpenFiles = 100000,
            CompressionLevel = CompressionLevel.SnappyCompression,
            BlockSize = 200 * 1024,
            WriteBufferSize = 8 * 1024 * 1024,
        };

        private string _databaseName;

        public LevelDBDatabaseAdapter(string name)
        {
            Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, name));
            var attempts = 0;

            _databaseName = name;

            while (++attempts <= OpenDbMaxAttempts) //sometimes Leveldb throws unexpected error when it tries to open db on Windows
            {
                try
                {
                    _database = new DB(name, _databaseOptions);

                    return;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error opening database {name} (attempt: {attempts}). {ex.Message}");

                    if (attempts == OpenDbMaxAttempts)
                        throw;
                }
            }
        }

        // Opens an already-existing LevelDB at an absolute path for read-only use (restore flow).
        // Distinct from the ctor above on purpose: this path must never create a missing DB —
        // a wrong path should surface as a LevelDB open error rather than silently producing an
        // empty database that "successfully" reads zero templates. Also skips the CreateDirectory
        // no-op the read/write ctor performs. Tunables mirror the read/write ctor above.
        private LevelDBDatabaseAdapter(string absolutePath, bool readOnly)
        {
            if (!readOnly)
                throw new ArgumentException("This ctor is for the read-only path only.");

            var readOptions = new Options
            {
                CreateIfMissing = false,
                MaxOpenFiles = 100000,
                CompressionLevel = CompressionLevel.SnappyCompression,
                BlockSize = 200 * 1024,
                WriteBufferSize = 8 * 1024 * 1024,
            };

            _databaseName = absolutePath;

            var attempts = 0;
            while (++attempts <= OpenDbMaxAttempts)
            {
                try
                {
                    _database = new DB(absolutePath, readOptions);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error opening read-only database {absolutePath} (attempt: {attempts}). {ex.Message}");

                    if (attempts == OpenDbMaxAttempts)
                        throw;
                }
            }
        }

        public static LevelDBDatabaseAdapter ForReadOnly(string absolutePath) => new(absolutePath, readOnly: true);

        public void Delete(byte[] key)
        {
            try
            {
                _database.Delete(key);
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public void DeleteValueFromTo(byte[] from, byte[] to)
        {
            using var iterator = _database.CreateIterator(_iteratorOptions);

            iterator.Seek(from);
            while (iterator.IsValid && iterator.Key().IsSmallerOrEquals(to))
            {
                _database.Delete(iterator.Key());
                iterator.Next();
            }
        }

        // Bounded prefix-range delete: removes up to `limit` keys that start with
        // `prefix` and sort strictly before `exclusiveUpperBound`, in one atomic write
        // batch, and returns how many were removed. The bound keeps a single pass cheap
        // on a large table (retention sweeps in batches); `limit` <= 0 is a no-op
        // returning 0, so callers can pass configuration straight through.
        public int DeleteStartingWithBefore(byte[] prefix, byte[] exclusiveUpperBound, int limit)
        {
            if (limit <= 0)
                return 0;

            Iterator iterator = null;
            var removed = 0;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                using var batch = new WriteBatch();

                // One Key() per row: each call materializes a fresh byte[] copy of the
                // key, and this loop runs at retention-sweep scale.
                iterator.Seek(prefix);

                while (removed < limit && iterator.IsValid)
                {
                    var key = iterator.Key();

                    // The bound starts with the prefix, so the bytewise check subsumes the
                    // StartsWith guard after Seek(prefix); it stays as a belt-and-braces
                    // invariant on the range.
                    if (!key.StartsWith(prefix) || CompareBytewise(key, exclusiveUpperBound) >= 0)
                        break;

                    batch.Delete(key);
                    removed++;

                    iterator.Next();
                }

                if (removed > 0)
                    _database.Write(batch);

                return removed;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        // LevelDB orders keys bytewise; the ByteArrayExtensions order helpers compare
        // LENGTH first (their callers only ever compare same-length keys), so a
        // prefix-plus-suffix key would never order below its shorter prefix bound. This
        // is the bytewise comparison the iterator actually guarantees.
        private static int CompareBytewise(byte[] left, byte[] right)
        {
            var shared = Math.Min(left.Length, right.Length);

            for (var i = 0; i < shared; i++)
            {
                var comparison = left[i].CompareTo(right[i]);

                if (comparison != 0)
                    return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }

        public bool TryRead(byte[] key, out byte[] value)
        {
            try
            {
                value = _database.Get(key);
                return value != null;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public void Put(byte[] key, byte[] value)
        {
            try
            {
                _database.Put(key, value);
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        // Atomically applies all puts and deletes as a single LevelDB write batch. Used for
        // multi-row token lifecycle transitions (e.g. rotation) that must not be observed
        // half-applied. Throws ServerDatabaseException on failure, leaving the batch unapplied.
        public void PutBatch(IReadOnlyList<(byte[] key, byte[] value)> puts, IReadOnlyList<byte[]> deletes = null)
        {
            // Puts are the point of the batch; deletes are optional. Say so instead of the
            // NullReferenceException the foreach would otherwise throw.
            if (puts is null)
                throw new ArgumentNullException(nameof(puts));

            using var batch = new WriteBatch();

            // Population inside the try as well: a bad key/value must surface as the
            // ServerDatabaseException the rest of the adapter guarantees, not a raw one.
            try
            {
                foreach (var (key, value) in puts)
                    batch.Put(key, value);

                if (deletes is not null)
                    foreach (var key in deletes)
                        batch.Delete(key);

                _database.Write(batch);
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public byte[] Get(byte[] key, byte[] prefix)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                iterator.Seek(key);

                return iterator.IsValid && iterator.Key().StartsWith(prefix) ? iterator.Value() : null;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public byte[] GetLatest(byte[] key, byte[] prefix)
        {
            Iterator iterator = null;

            bool CheckValue() => iterator.IsValid && iterator.Key().StartsWith(prefix) && iterator.Key().IsSmallerOrEquals(key);

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                iterator.Seek(key);

                if (CheckValue())
                    return iterator.Value();

                if (!iterator.IsValid)
                    return null;

                iterator.Prev();

                return CheckValue() ? iterator.Value() : null;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public byte[] GetFirst(byte[] key, byte[] prefix)
        {

            using var iterator = _database.CreateIterator(_iteratorOptions);

            iterator.Seek(key);

            if (iterator.IsValid && iterator.Key().StartsWith(prefix))
                return iterator.Value();

            return null;
        }

        public Dictionary<Guid, (byte[] firstValue, byte[] lastValue)> GetLastAndFirstValues(
            IEnumerable<Guid> sensorIds,
            Dictionary<Guid, (byte[] firstValue, byte[] lastValue)> results = null)
        {
            results ??= new Dictionary<Guid, (byte[] firstValue, byte[] lastValue)>();

            if (!sensorIds.Any())
                return results;

            using var iterator = _database.CreateIterator(_iteratorOptions);

            foreach (var sensorId in sensorIds)
            {
                _logger.Info(sensorId);

                byte[] currentFirstValue = null;
                byte[] currentLastValue = null;

                // Строим диапазон через createKeyFunc
                DbKey minKey = new DbKey(sensorId, DateTime.MinValue);
                DbKey maxKey = new DbKey(sensorId, DateTime.MaxValue.Ticks);

                byte[] prefixBytes = minKey.ToPrefixBytes();

                // Проверяем, есть ли уже firstValue из предыдущей (старой) базы
                bool firstAlreadyKnown =
                    results.TryGetValue(sensorId, out var existing) &&
                    existing.firstValue != null;

                // ---------- 1. FIRST (оптимизация: пропускаем, если уже найден ранее) ----------
                if (!firstAlreadyKnown)
                {
                    iterator.Seek(minKey.ToBytes());

                    if (iterator.IsValid && iterator.Key().StartsWith(prefixBytes))
                    {
                        currentFirstValue = iterator.Value();
                    }
                }

                // ---------- 2. LAST (всегда нужно искать) ----------
                iterator.Seek(maxKey.ToBytes());

                if (iterator.IsValid && iterator.Key().StartsWith(prefixBytes))
                {
                    currentLastValue = iterator.Value();
                }
                else if (iterator.IsValid)
                {
                    iterator.Prev();

                    if (iterator.IsValid && iterator.Key().StartsWith(prefixBytes))
                    {
                        currentLastValue = iterator.Value();
                    }
                }

                // ---------- 3. MERGE результатов ----------
                if (results.TryGetValue(sensorId, out existing))
                {
                    results[sensorId] = (
                        existing.firstValue ?? currentFirstValue,  // first не перезаписываем
                        currentLastValue ?? existing.lastValue     // last обновляем
                    );
                }
                else if (currentFirstValue != null || currentLastValue != null)
                {
                    results[sensorId] = (
                        currentFirstValue ?? currentLastValue,
                        currentLastValue ?? currentFirstValue
                    );
                }
            }

            return results;
        }


        public IEnumerable<byte[]> GetValueFromTo(byte[] from, byte[] to)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(from); iterator.IsValid && iterator.Key().IsSmallerOrEquals(to); iterator.Next())
                    yield return iterator.Value();
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public IEnumerable<(byte[], byte[])> GetValueKeyPairFromTo(byte[] from, byte[] to)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(from); iterator.IsValid && iterator.Key().IsSmallerOrEquals(to); iterator.Next())
                    yield return (iterator.Key(), iterator.Value());
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public IEnumerable<byte[]> GetValueToFrom(byte[] from, byte[] to)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                iterator.Seek(to);

                if (!iterator.IsValid)
                    iterator.SeekToLast();

                while (iterator.IsValid && iterator.Key().IsGreater(to))
                {
                    iterator.Prev();

                    if (!iterator.IsValid || iterator.Key().IsSmaller(from))
                        yield break;
                }

                for (; iterator.IsValid && iterator.Key().IsGreaterOrEquals(from); iterator.Prev())
                    yield return iterator.Value();
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public IEnumerable<(byte[], byte[])> GetValueKeyPairToFrom(byte[] from, byte[] to)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                iterator.Seek(to);

                if (!iterator.IsValid)
                    iterator.SeekToLast();

                while (iterator.IsValid && iterator.Key().IsGreater(to))
                {
                    iterator.Prev();

                    if (!iterator.IsValid || iterator.Key().IsSmaller(from))
                        yield break;
                }

                for (; iterator.IsValid && iterator.Key().IsGreaterOrEquals(from); iterator.Prev())
                    yield return (iterator.Key(), iterator.Value());
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public List<byte[]> GetAllStartingWith(byte[] startWithKey)
        {
            Iterator iterator = null;
            List<byte[]> values = new(1 << 4);

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey); iterator.Next())
                    values.Add(iterator.Value());

                return values;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        // Key-bearing sibling of GetAllStartingWith: the caller must be able to check a
        // row's payload against the key it was stored under (a mismatch means damaged
        // storage — the row must be rejected, not silently republished under its payload).
        public List<(byte[] Key, byte[] Value)> GetAllKeyValuePairsStartingWith(byte[] startWithKey)
        {
            Iterator iterator = null;
            var pairs = new List<(byte[] Key, byte[] Value)>(1 << 4);

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey); iterator.Next())
                    pairs.Add((iterator.Key(), iterator.Value()));

                return pairs;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public void FillLatestValues(Dictionary<byte[], (long from, byte[] toKey, byte[] latestValue)> keyValuePairs, long endBase)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator();

                foreach (var (key, value) in keyValuePairs)
                {
                    if (value.latestValue == null && endBase >= value.from)
                    {
                        for (iterator.Seek(key); iterator.IsValid && iterator.Key().StartsWith(key) && iterator.Key().IsSmaller(value.toKey); iterator.Next())
                            keyValuePairs[key] = (value.from, value.toKey, iterator.Value());
                    }
                }
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public List<byte[]> GetPageStartingWith(byte[] startWithKey, int page, int pageSize)
        {
            Iterator iterator = null;
            List<byte[]> values = new();

            int skip = (page - 1) * pageSize;
            int index = 1;
            int lastIndex = page * pageSize;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey) && index <= lastIndex; iterator.Next(), ++index)
                {
                    if (index <= skip)
                        continue;

                    values.Add(iterator.Value());
                }

                return values;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public TaskResult<string> Backup(string backupPath)
        {
            try
            {
                var fileInfo = new FileInfo($"{backupPath}.zip");

                // The SoftFX LevelDB wrapper does not create the parent directory for a brand-new
                // DB path — without this, new DB(backupPath, ...) fails with
                // "NotFound: <path>/LOCK: The system cannot find the path specified" the first
                // time a backup is taken after the server starts (BackupDatabaseService builds a
                // fresh <dbName>_<timestamp> path per run).
                Directory.CreateDirectory(backupPath);

                using (var backupDb = new DB(backupPath, _databaseOptions))
                {
                    using (var snapshot = _database.CreateSnapshot())
                    {
                        using (var readOptions = new ReadOptions() { Snapshot = snapshot })
                        {
                            using (var snapshotIterator = _database.CreateIterator(readOptions))
                            {

                                snapshotIterator.SeekToFirst();
                                while (snapshotIterator.IsValid)
                                {
                                    backupDb.Put(snapshotIterator.Key(), snapshotIterator.Value());
                                    snapshotIterator.Next();
                                }
                            }
                        }
                    }
                }

                if (File.Exists(fileInfo.FullName))
                    File.Delete(fileInfo.FullName);

                ZipFile.CreateFromDirectory(backupPath, fileInfo.FullName);
                Directory.Delete(backupPath, true);

                return TaskResult<string>.FromValue(fileInfo.FullName);
            }
            catch (Exception ex)
            {
                var msg = $"Backup database {backupPath} error: {ex}";
                _logger.Error(msg);
                return TaskResult<string>.FromError(msg);
            }
        }

        public void Compact()
        {
            _database.Compact();
        }


        public IEnumerable<(byte[], byte[])> GetAll()
        {
            using (var snapshot = _database.CreateSnapshot())
            {
                using (var readOptions = new ReadOptions() { Snapshot = snapshot })
                {
                    using (var snapshotIterator = _database.CreateIterator(readOptions))
                    {
                        snapshotIterator.SeekToFirst();
                        while (snapshotIterator.IsValid)
                        {
                            yield return (snapshotIterator.Key(), snapshotIterator.Value());
                            snapshotIterator.Next();
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _database?.Dispose();
        }

    }
}
