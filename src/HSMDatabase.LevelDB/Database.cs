using LevelDB;
using System;
using System.Collections.Generic;
using System.Text;
using HSMDatabase.LevelDB.Extensions;
using Exception = System.Exception;

namespace HSMDatabase.LevelDB
{
    public class Database : IDatabase
    {
        private readonly DB _database;
        private readonly string _name;
        public Database(string name)
        {
            Options databaseOptions = new Options();
            databaseOptions.CreateIfMissing = true;
            databaseOptions.MaxOpenFiles = 100000;
            databaseOptions.CompressionLevel = CompressionLevel.SnappyCompression;
            databaseOptions.BlockSize = 204800;
            databaseOptions.WriteBufferSize = 8388608;
            _name = name;
            try
            {
                _database = new DB(databaseOptions, name, Encoding.UTF8);
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException("Failed to open database", e);
            }
        }

        public string Name => _name;

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

        public void RemoveStartingWith(byte[] startWithKey)
        {
            try
            {
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(startWithKey); iterator.IsValid() && iterator.Key().StartsWith(startWithKey);
                    iterator.Next())
                {
                    _database.Delete(iterator.Key());
                }
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public bool Read(byte[] key, out byte[] value)
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
            try
            {
                long size = 0;
                var iterator = _database.CreateIterator();
                for (iterator.Seek(startWithKey); iterator.IsValid() && iterator.Key().StartsWith(startWithKey);
                    iterator.Next())
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
        }

        public List<byte[]> GetRange(byte[] from, byte[] to)
        {
            try
            {
                List<byte[]> values = new List<byte[]>();
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(from); iterator.IsValid() && !iterator.Key().StartsWith(to);
                    iterator.Next())
                {
                    values.Add(iterator.Value());                    
                }

                return values;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public List<byte[]> GetAllStartingWith(byte[] startWithKey)
        {
            try
            {
                List<byte[]> values = new List<byte[]>();
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(startWithKey); iterator.IsValid() && iterator.Key().StartsWith(startWithKey);
                    iterator.Next())
                {
                    values.Add(iterator.Value());
                }

                return values;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public void Dispose()
        {
            _database?.Dispose();
        }
    }
}
