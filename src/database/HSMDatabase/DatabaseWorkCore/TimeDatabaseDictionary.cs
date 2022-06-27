using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class TimeDatabaseDictionary : ITimeDatabaseDictionary
    {
        private readonly object _accessLock = new object();
        private readonly SortedSet<ISensorsDatabase> _sensorsDatabases;
        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly IDatabaseSettings _databaseSettings;


        public TimeDatabaseDictionary(IEnvironmentDatabase environmentDatabase, IDatabaseSettings dbSettings)
        {
            _databaseSettings = dbSettings;
            _environmentDatabase = environmentDatabase;
            _sensorsDatabases = new SortedSet<ISensorsDatabase>(new SensorDatabaseComparer());
        }


        public bool TryGetDatabase(DateTime time, out ISensorsDatabase database)
        {
            long ticks = time.Ticks;
            ISensorsDatabase correspondingItem;
            lock (_accessLock)
            {
                correspondingItem = _sensorsDatabases.FirstOrDefault(i =>
                    i.DatabaseMinTicks <= ticks && i.DatabaseMaxTicks >= ticks);

            }

            if (correspondingItem == null)
            {
                database = default(ISensorsDatabase);
                return false;
            }

            database = correspondingItem;
            return true;
        }

        public ISensorsDatabase GetDatabase(DateTime time)
        {
            long ticks = time.Ticks;
            lock (_accessLock)
            {
                var correspondingItem = _sensorsDatabases.FirstOrDefault(i =>
                    i.DatabaseMinTicks <= ticks && i.DatabaseMaxTicks >= ticks);

                if (correspondingItem != null)
                    return correspondingItem;

                DateTime minDateTime = DateTimeMethods.GetMinDateTime(time);
                DateTime maxDateTime = DateTimeMethods.GetMaxDateTime(time);
                string newDatabaseName = CreateSensorsDatabaseName(minDateTime, maxDateTime);
                ISensorsDatabase newDatabase = LevelDBManager.GetSensorDatabaseInstance(
                    _databaseSettings.GetPathToMonitoringDatabase(newDatabaseName), minDateTime, maxDateTime);
                _sensorsDatabases.Add(newDatabase);
                Task.Run(() => _environmentDatabase.AddMonitoringDatabaseToList(newDatabaseName));
                return newDatabase;
            }

        }

        public void AddDatabase(ISensorsDatabase database)
        {
            lock (_accessLock)
            {
                _sensorsDatabases.Add(database);
            }
        }

        public List<ISensorsDatabase> GetAllDatabases()
        {
            lock (_accessLock)
            {
                return _sensorsDatabases.ToList();
            }
        }

        private string CreateSensorsDatabaseName(DateTime from, DateTime to)
        {
            return $"{_databaseSettings.MonitoringDatabaseName}_{from.Ticks}_{to.Ticks}";
        }
        private class SensorDatabaseComparer : IComparer<ISensorsDatabase>
        {
            public int Compare(ISensorsDatabase? x, ISensorsDatabase? y)
            {
                if (x == null && y == null)
                    return 0;

                if (x == null)
                    return -1;

                if (y == null)
                    return 1;

                long longResult = x.DatabaseMinTicks - y.DatabaseMinTicks;
                if (longResult < 0)
                    return -1;
                if (longResult > 0)
                    return 1;
                return 0;
            }
        }
    }
}