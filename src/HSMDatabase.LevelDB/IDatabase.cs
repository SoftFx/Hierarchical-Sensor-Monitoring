using System;
using System.Collections.Generic;

namespace HSMDatabase.LevelDB
{
    public interface IDatabase : IDisposable
    {
        string Name { get; }
        void Delete(byte[] key);
        void RemoveStartingWith(byte[] startWithKey);
        bool Read(byte[] key, out byte[] value);
        void Put(byte[] key, byte[] value);
        long GetSize(byte[] startWithKey);
        List<byte[]> GetRange(byte[] from, byte[] to);
        List<byte[]> GetAllStartingWith(byte[] startWithKey);
        public List<byte[]> GetPageStartingWith(byte[] startWithKey, int page, int pageSize);
    }
}