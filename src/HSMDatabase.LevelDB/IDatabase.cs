using System;
using System.Collections.Generic;

namespace HSMDatabase.LevelDB
{
    public interface IDatabase : IDisposable
    {
        string Name { get; }
        void Delete(byte[] key);
        void DeleteAllStartingWith(byte[] startWithKey);
        bool TryRead(byte[] key, out byte[] value);
        byte[] Read(byte[] key);
        void Put(byte[] key, byte[] value);
        long GetSize(byte[] startWithKey);
        List<byte[]> GetRange(byte[] from, byte[] to);
        List<byte[]> GetAllStartingWith(byte[] startWithKey);
        List<byte[]> GetAllStartingWithAndSeek(byte[] startWithKey, byte[] seekKey);
        public List<byte[]> GetPageStartingWith(byte[] startWithKey, int page, int pageSize);
    }
}