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
        private readonly ConcurrentBag<ISensorValuesDatabase> _sensorDbs = new();
        private readonly object _locker = new();

        private ISensorValuesDatabase _lastDb;


        internal SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            var localDbs = new List<ISensorValuesDatabase>(1 << 4);

            var sensorValuesDirectories =
               Directory.GetDirectories(dbSettings.DatabaseFolder, $"{dbSettings.SensorValuesDatabaseName}*", SearchOption.TopDirectoryOnly);

            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);

                foreach (var dbPath in Directory.GetDirectories(directory))
                    AddNewDb(from, to).OpenDatabase(dbPath);
            }

            _sensorDbs = new ConcurrentBag<ISensorValuesDatabase>(localDbs.OrderByDescending(db => db.From));
        }


        internal ISensorValuesDatabase GetNewestDatabases(long time)
        {
            lock (_locker)
            {
                if (_lastDb == null || _lastDb.To < time)
                {
                    var from = DateTimeMethods.GetMinDateTimeTicks(time);
                    var to = DateTimeMethods.GetMaxDateTimeTicks(time);

                    return AddNewDb(from, to);
                }

                return _lastDb;
            }
        }

        private ISensorValuesDatabase AddNewDb(long from, long to)
        {
            _lastDb = LevelDBManager.GetSensorValuesDatabaseInstance(from, to);

            _sensorDbs.Add(_lastDb);

            return _lastDb;
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

        public IEnumerator<ISensorValuesDatabase> GetEnumerator() => _sensorDbs.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
