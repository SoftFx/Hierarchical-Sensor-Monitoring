using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.DatabaseImplementations;
using System;

namespace HSMDatabase.LevelDB
{
    public static class LevelDBManager
    {
        public static IEnvironmentDatabase GetEnvitonmentDatabaseInstance(string name) =>
            new EnvironmentDatabaseWorker(name);

        public static ISensorsDatabase GetSensorDatabaseInstance(string name, DateTime minTime, DateTime maxTime) =>
            new SensorsDatabaseWorker(name, minTime, maxTime);

        public static ISensorValuesDatabase GetSensorValuesDatabaseInstance(long from, long to) =>
            new SensorValuesDatabaseWorker(from, to);
    }
}
