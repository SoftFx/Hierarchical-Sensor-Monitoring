using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System.Collections.Generic;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class SensorValuesDatabaseDictionary
    {
        private readonly List<ISensorValuesDatabase> _sensorDbs = new();


        internal ISensorValuesDatabase InitializeAndGetDatabases(long from, long to)
        {
            var databases = LevelDBManager.GetSensorValuesDatabaseInstance(from, to);

            _sensorDbs.Add(databases);

            return databases;
        }

        internal ISensorValuesDatabase GetLatestDatabases(long time)
        {
            var latestDbs = _sensorDbs.LastOrDefault();
            if (latestDbs != null && latestDbs.From <= time && latestDbs.To >= time)
                return latestDbs;

            var from = DateTimeMethods.GetMinDateTimeTicks(time);
            var to = DateTimeMethods.GetMaxDateTimeTicks(time);

            return InitializeAndGetDatabases(from, to);
        }

        internal List<ISensorValuesDatabase> GetAllDatabases() => _sensorDbs.ToList();

        internal List<ISensorValuesDatabase> GetSortedDatabases()
        {
            var databases = GetAllDatabases();
            databases.Reverse();

            return databases;
        }
    }
}
