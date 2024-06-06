using HSMCommon.TaskResult;

namespace HSMDatabase.AccessManager
{
    public interface IEntityDatabase
    {
        bool TryRead(byte[] key, out byte[] value);


        void Put(byte[] key, byte[] value);

        void Delete(byte[] key);

        TaskResult<string> Backup(string backupPath);
    }
}