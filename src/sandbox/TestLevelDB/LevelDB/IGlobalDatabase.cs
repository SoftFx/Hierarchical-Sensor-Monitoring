namespace TestLevelDB.LevelDB
{
    internal interface IGlobalDatabase : IDisposable
    {
        public void AddValue(string key, int value);

        public string GetFirstValue(string sensorId);

        public string GetLastValue(string sensorId);

        public List<string> GetAllValues(string sensorId);
    }
}
