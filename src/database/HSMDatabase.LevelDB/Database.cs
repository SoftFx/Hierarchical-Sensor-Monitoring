using HSMDatabase.LevelDB.Extensions;
using LevelDB;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using Exception = System.Exception;

namespace HSMDatabase.LevelDB
{
    public class LevelDBDatabaseAdapter : IDisposable
    {
        private const int OpenDbMaxAttempts = 10;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly DB _database;
        private readonly ReadOptions _iteratorOptions = new();


        public LevelDBDatabaseAdapter(string name, long bufferSize = 8388608)
        {
            Options databaseOptions = new()
            {
                CreateIfMissing = true,
                MaxOpenFiles = 100000,
                CompressionLevel = CompressionLevel.SnappyCompression,
                BlockSize = 204800,
                WriteBufferSize = bufferSize,
            };

            Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, name));
            var attempts = 0;

            while (++attempts <= OpenDbMaxAttempts) //sometimes Leveldb throws unexpected error when it tries to open db on Windows
            {
                try
                {
                    _database = new DB(name, databaseOptions);

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

        public void DeleteAllStartingWith(byte[] startWithKey)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey); iterator.Next())
                    _database.Delete(iterator.Key());
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

        public long GetSize(byte[] startWithKey)
        {
            Iterator iterator = null;

            try
            {
                long size = 0;
                iterator = _database.CreateIterator();

                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey); iterator.Next())
                {
                    size += iterator.Value().LongLength;
                    //TODO: possibly add startwithKey size
                }

                return size;
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

        internal byte[] GetLatestValue()
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);
                iterator.SeekToLast();

                if (iterator.IsValid)
                    return iterator.Value();
            }
            catch (Exception ex)
            {
                throw new ServerDatabaseException(ex.Message, ex);
            }
            finally
            {
                iterator?.Dispose();
            }

            return null;
        }

        internal List<byte[]> GetValues(byte[] to, int count)
        {
            Iterator iterator = null;
            var values = new List<byte[]>(count);

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.SeekToLast(); iterator.IsValid && values.Count != count; iterator.Prev())
                {
                    if (iterator.Key().IsSmallerOrEquals(to))
                        values.Add(iterator.Value());
                }

                return values;
            }
            catch (Exception ex)
            {
                throw new ServerDatabaseException(ex.Message, ex);
            }
            finally
            {
                iterator?.Dispose();
            }
        }

        public List<byte[]> GetValues(byte[] from, byte[] to)
        {
            Iterator iterator = null;
            var values = new List<byte[]>(1 << 5);

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);
                for (iterator.Seek(from); iterator.IsValid && iterator.Key().IsSmallerOrEquals(to); iterator.Next())
                    values.Add(iterator.Value());

                values.Reverse();

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

        public List<byte[]> GetStartingWithRange(byte[] from, byte[] to, byte[] startWithKey)
        {
            Iterator iterator = null;
            List<byte[]> values = new();

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(from); iterator.IsValid && iterator.Key().IsSmallerOrEquals(to); iterator.Next())
                    if (iterator.Key().StartsWith(startWithKey))
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

        public List<byte[]> GetStartingWithTo(byte[] to, byte[] startWithKey, int count)
        {
            Iterator iterator = null;
            var values = new List<byte[]>(count);

            try
            {
                iterator = _database.CreateIterator(_iteratorOptions);

                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey) && values.Count != count; iterator.Next())
                {
                    if (iterator.Key().IsSmallerOrEquals(to))
                        values.Add(iterator.Value());
                }

                values.Reverse(); // from newest to oldest

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

        public List<byte[]> GetAllStartingWith(byte[] startWithKey)
        {
            Iterator iterator = null;
            List<byte[]> values = new();

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

        public void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs)
        {
            Iterator iterator = null;

            try
            {
                iterator = _database.CreateIterator();

                foreach (var (key, value) in keyValuePairs)
                {
                    if (value.latestValue == null)
                    {
                        for (iterator.Seek(key); iterator.IsValid && iterator.Key().StartsWith(key); iterator.Next())
                            keyValuePairs[key] = (value.sensorId, iterator.Value());
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

        public void Dispose()
        {
            _database?.Dispose();
        }
    }
}
