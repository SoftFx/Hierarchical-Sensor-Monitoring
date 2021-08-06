using HSMDatabase.SensorsDatabase;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class TimeDatabaseDictionary : ITimeDatabaseDictionary
    {
        private readonly object _accessLock = new object();
        private readonly List<ISensorsDatabase> _sensorsDatabases;
        public TimeDatabaseDictionary()
        {
            _sensorsDatabases = new List<ISensorsDatabase>();
        }
        public bool TryGetDatabase(DateTime time, out ISensorsDatabase database)
        {
            long minTicks = DateTimeMethods.GetMinDateTimeTicks(time);
            long maxTicks = DateTimeMethods.GetMaxDateTimeTicks(time);
            ISensorsDatabase correspondingItem;
            lock (_accessLock)
            {
                correspondingItem = _sensorsDatabases.FirstOrDefault(i => 
                    i.DatabaseMinTicks == minTicks && i.DatabaseMaxTicks == maxTicks);

            }

            if (correspondingItem == null)
            {
                database = default(ISensorsDatabase);
                return false;
            }

            database = correspondingItem;
            return true;
        }

        public void AddDatabase(ISensorsDatabase database)
        {
            lock (_accessLock)
            {
                _sensorsDatabases.Add(database);
            }
        }
    }
}