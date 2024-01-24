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
        private readonly ConcurrentStack<ISensorValuesDatabase> _youngestDb = new();
        private readonly ConcurrentStack<ISensorValuesDatabase> _sensorDbs = new();
        private readonly IDatabaseSettings _dbSettings;


        internal SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            _dbSettings = dbSettings;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);
                AddNewDatabase(directory, from, to);
            }
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

            return AddNewDatabase(_dbSettings.GetPathToSensorValueDatabase(from, to), from, to);
        }

        private ISensorValuesDatabase AddNewDatabase(string name, long from, long to)
        {
            var newDb = LevelDBManager.GetSensorValuesDatabaseInstance(name, from, to);

            while (_sensorDbs.TryPeek(out var lastDb) && lastDb.From > newDb.To && _sensorDbs.TryPop(out lastDb))
                _youngestDb.Push(lastDb);

            _sensorDbs.Push(newDb);

            while (_youngestDb.TryPop(out var db))
                _sensorDbs.Push(db);

            return newDb;
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

        public IEnumerator<ISensorValuesDatabase> GetEnumerator() => _sensorDbs.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}