using System.Collections.Generic;

namespace HSMDatabase.LevelDB
{
    public interface IDatabase
    {
        void Delete(byte[] key);
        bool Read(byte[] key, out byte[] value);
        void Put(byte[] key, byte[] value);
        long GetSize(byte[] startWithKey);
        List<byte[]> GetRange(byte[] from, byte[] to);
    }
}