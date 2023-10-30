namespace HSMDatabase.AccessManager
{
    public interface IEntityDatabase
    {
        bool TryRead(byte[] key, out byte[] value);


        public void Put(byte[] key, byte[] value);

        public void Delete(byte[] key);
    }
}