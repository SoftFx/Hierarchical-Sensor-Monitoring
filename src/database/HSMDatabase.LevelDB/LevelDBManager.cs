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

        public static ISensorValuesDatabase GetSensorValuesDatabaseInstance(string name, long from, long to) =>
            new SensorValuesDatabaseWorker(name, from, to);
    }
}
