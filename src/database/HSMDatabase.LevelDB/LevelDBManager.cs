using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.DatabaseImplementations;

namespace HSMDatabase.LevelDB
{
    public static class LevelDBManager
    {
        public static IEnvironmentDatabase GetEnvitonmentDatabaseInstance(string name) =>
            new EnvironmentDatabaseWorker(name);

        public static ISensorValuesDatabase GetSensorValuesDatabaseInstance(string name, long from, long to) =>
            new SensorValuesDatabaseWorker(name, from, to);
    }
}
