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
