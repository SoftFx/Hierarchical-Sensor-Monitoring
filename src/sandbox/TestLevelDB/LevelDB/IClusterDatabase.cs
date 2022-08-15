namespace TestLevelDB.LevelDB
{
    internal interface IClusterDatabase : IDisposable
    {
        public void AddValue(string key, int value);

        public string GetLastValue();

        public string GetFirstValue();

        public List<string> GetAllValues();
    }
}
