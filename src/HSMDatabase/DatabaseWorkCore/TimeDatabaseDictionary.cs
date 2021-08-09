﻿using HSMDatabase.SensorsDatabase;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class TimeDatabaseDictionary : ITimeDatabaseDictionary
    {
        private readonly object _accessLock = new object();
        private readonly SortedSet<ISensorsDatabase> _sensorsDatabases;
        public TimeDatabaseDictionary()
        {
            _sensorsDatabases = new SortedSet<ISensorsDatabase>(new SensorDatabaseComparer());
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

        public List<ISensorsDatabase> GetAllDatabases()
        {
            lock (_accessLock)
            {
                return _sensorsDatabases.ToList();
            }
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

                return (int)(x.DatabaseMinTicks - y.DatabaseMinTicks);
            }
        }
    }
}