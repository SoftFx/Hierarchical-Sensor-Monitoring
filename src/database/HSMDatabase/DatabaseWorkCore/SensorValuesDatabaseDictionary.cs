using HSMCommon.Collections;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class SensorValuesDatabaseDictionary : IEnumerable<ISensorValuesDatabase>
    {
        private readonly CPriorityQueue<ISensorValuesDatabase, long> _sensorDbs = new();
        private readonly SortedList<ISensorValuesDatabase, long> _sortedList = new();

        private readonly IDatabaseSettings _dbSettings;


        internal SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            ConcurrentBag
            _dbSettings = dbSettings;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);
                _sensorDbs.Add(BuildNewDatabase(directory, from, to));
            }

            FixDatabaseOrder();
        }


        internal ISensorValuesDatabase GetDatabaseByTime(long time)
        {
            foreach (var db in this)
                if (db.IsInclude(time))
                    return db;
                else if (time < db.From)
                    break;

            var from = DateTimeMethods.GetMinDateTimeTicks(time);
            var to = DateTimeMethods.GetMaxDateTimeTicks(time);

            var newDb = BuildNewDatabase(_dbSettings.GetPathToSensorValueDatabase(from, to), from, to);
            _sensorDbs.Add(newDb);

            FixDatabaseOrder();

            return newDb;
        }

        private void FixDatabaseOrder() => _sensorDbs = new ConcurrentBag<ISensorValuesDatabase>(_sensorDbs.OrderBy(u => u.From));

        private List<string> GetSensorValuesDirectories()
        {
            var sensorValuesDirectories =
               Directory.GetDirectories(_dbSettings.DatabaseFolder, $"{_dbSettings.SensorValuesDatabaseName}*", SearchOption.TopDirectoryOnly);

            return sensorValuesDirectories.OrderBy(d => d).ToList();
        }


        private static ISensorValuesDatabase BuildNewDatabase(string name, long from, long to) => LevelDBManager.GetSensorValuesDatabaseInstance(name, from, to);

        private static (long from, long to) GetDatesFromFolderName(string folder)
        {
            var from = 0L;
            var to = 0L;

            var splitResults = folder.Split('_');

            if (long.TryParse(splitResults[1], out long fromTicks))
                from = fromTicks;

            if (long.TryParse(splitResults[2], out long toTicks))
                to = toTicks;

            return (from, to);
        }

        public IEnumerator<ISensorValuesDatabase> GetEnumerator() => _sensorDbs.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}