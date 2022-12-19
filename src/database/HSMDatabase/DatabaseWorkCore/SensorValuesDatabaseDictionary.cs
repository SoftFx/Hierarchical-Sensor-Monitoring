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
        private readonly ConcurrentQueue<ISensorValuesDatabase> _sensorDbs = new();

        private readonly IDatabaseSettings _dbSettings;

        private ISensorValuesDatabase _lastDb;


        internal SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            _dbSettings = dbSettings;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);
                AddNewDb(directory, from, to);
            }
        }


        internal ISensorValuesDatabase GetNewestDatabases(long time)
        {
            if (_lastDb == null || _lastDb.To < time)
            {
                var from = DateTimeMethods.GetMinDateTimeTicks(time);
                var to = DateTimeMethods.GetMaxDateTimeTicks(time);

                return AddNewDb(_dbSettings.GetPathToSensorValueDatabase(from, to), from, to);
            }

            return _lastDb;
        }

        internal ISensorValuesDatabase AddNewDb(string name, long from, long to)
        {
            _lastDb = LevelDBManager.GetSensorValuesDatabaseInstance(name, from, to);

            _sensorDbs.Enqueue(_lastDb);

            return _lastDb;
        }

        private List<string> GetSensorValuesDirectories()
        {
            var sensorValuesDirectories =
               Directory.GetDirectories(_dbSettings.DatabaseFolder, $"{_dbSettings.SensorValuesDatabaseName}*", SearchOption.TopDirectoryOnly);

            return sensorValuesDirectories.OrderBy(d => d).ToList();
        }

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

        public List<ISensorValuesDatabase> Dbs => _sensorDbs.ToList();

        public IEnumerator<ISensorValuesDatabase> GetEnumerator() => _sensorDbs.Reverse().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
