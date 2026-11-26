using HSMCommon.TaskResult;
using System;

namespace HSMDatabase.AccessManager
{
    // Disposing it closes the database and releases its files.
    public interface IEntityDatabase : IDisposable
    {
        bool TryRead(byte[] key, out byte[] value);


        void Put(byte[] key, byte[] value);

        void Delete(byte[] key);

        TaskResult<string> Backup(string backupPath);
    }
}