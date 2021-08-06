using LevelDB;
using System;
using System.Collections.Generic;
using System.Text;
using HSMDatabase.LevelDB.Extensions;

namespace HSMDatabase.LevelDB
{
    internal class Database : IDatabase
    {
        private DB _database;
        public Database(string name)
        {
            Options databaseOptions = new Options();
            databaseOptions.CreateIfMissing = true;
            databaseOptions.MaxOpenFiles = 100000;
            databaseOptions.CompressionLevel = CompressionLevel.SnappyCompression;
            databaseOptions.BlockSize = 204800;
            databaseOptions.WriteBufferSize = 8388608;
            try
            {
                _database = new DB(databaseOptions, name, Encoding.UTF8);
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException("Failed to open database", e);
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
                    //TODO: possibly add key size
                }

                return size;
            }
            catch (Exception e)
            {
                throw new ServerDatabaseException(e.Message, e);
            }
        }

        public List<byte[]> GetRange(byte[] @from, byte[] to)
        {
            
        }
    }
}
