using HSMDatabase.LevelDB.Extensions;
using LevelDB;
using System;
using System.Collections.Generic;
using System.IO;
using Exception = System.Exception;

namespace HSMDatabase.LevelDB
{
    public class LevelDBDatabaseAdapter : IDisposable
    {
        private readonly DB _database;
        private readonly string _name;


        public LevelDBDatabaseAdapter(string name)
        {
            Options databaseOptions = new Options();
            databaseOptions.CreateIfMissing = true;
            databaseOptions.MaxOpenFiles = 100000;
            databaseOptions.CompressionLevel = CompressionLevel.SnappyCompression;
            databaseOptions.BlockSize = 204800;
            databaseOptions.WriteBufferSize = 8388608;

            //databaseOptions.Comparator = Comparator.Create("BytewiseComparator", new ByteArraysComparer());
            Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, name));

            _name = name;
            try
            {
                _database = new DB(name, databaseOptions);
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

        public void DeleteAllStartingWith(byte[] startWithKey)
        {
            try
            {
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey);
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

        public byte[] Read(byte[] key)
        {
            return _database.Get(key);
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
                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey);
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

        public byte[] GetLatestValue()
        {
            try
            {
                var iterator = _database.CreateIterator(new ReadOptions());
                iterator.SeekToLast();

                if (iterator.IsValid)
                    return iterator.Value();
            }
            catch (Exception ex)
            {
                throw new ServerDatabaseException(ex.Message, ex);
            }

            return null;
        }

        public List<byte[]> GetStartingWithRange(byte[] from, byte[] to, byte[] startWithKey)
        {
            try
            {
                List<byte[]> values = new List<byte[]>();
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(from); iterator.IsValid && iterator.Key().IsSmallerOrEquals(to);
                    iterator.Next())
                {
                    if (iterator.Key().StartsWith(startWithKey))
                    {
                        values.Add(iterator.Value());
                    }
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
                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey);
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

        public void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs)
        {
            try
            {
                var iterator = _database.CreateIterator();

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
        }

        public List<byte[]> GetAllStartingWithAndSeek(byte[] startWithKey, byte[] seekKey)
        {
            try
            {
                List<byte[]> values = new List<byte[]>();
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(seekKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey);
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

        public List<byte[]> GetPageStartingWith(byte[] startWithKey, int page, int pageSize)
        {
            int skip = (page - 1) * pageSize;
            int index = 1;
            int lastIndex = page * pageSize;
            try
            {
                List<byte[]> values = new List<byte[]>();
                var iterator = _database.CreateIterator(new ReadOptions());
                for (iterator.Seek(startWithKey); iterator.IsValid && iterator.Key().StartsWith(startWithKey) &&
                    index <= lastIndex; iterator.Next(), ++index)
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
        }

        //private void ArrToDebug(byte[] array)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    for (int i = 0; i < array.Length; ++i)
        //    {
        //        sb.Append($"{array[i]} ");
        //    }
        //    Debug.Print($"Array {sb.ToString()}");
        //}
        public void Dispose()
        {
            _database?.Dispose();
        }

        //private class ByteArraysComparer : IComparer<NativeArray>
        //{
        //    public int Compare(NativeArray x, NativeArray y)
        //    {
        //        unsafe
        //        {
        //            //might need to compare length via bytes too
        //            int* xLengthPtr = (int*) x.byteLength.ToPointer();
        //            int* yLengthPtr = (int*) y.byteLength.ToPointer();
        //            var lengthCompare = (*xLengthPtr).CompareTo(*yLengthPtr);
        //            if (lengthCompare != 0) return lengthCompare;
        //            int len = (*xLengthPtr);
        //            int i = 0;
        //            byte* xStartPointer = (byte*) x.baseAddr.ToPointer();
        //            byte* yStartPointer = (byte*) y.baseAddr.ToPointer();
        //            byte* xStartCopy = &(*xStartPointer);
        //            byte* yStartCopy = &(*yStartPointer);
        //            while (i++ < len)
        //            {
        //                var cmpRes = (*xStartCopy).CompareTo(*yStartCopy);
        //                if (cmpRes != 0)
        //                    return cmpRes;

        //                ++xStartCopy;
        //                ++yStartCopy;
        //            }

        //            return 0;
        //        }
        //    }
        //}

    }
}
