using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class SensorValuesDatabaseDictionary
    {
        private readonly List<ISensorValuesDatabase> _sensorDbs = new();


        internal SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            var sensorValuesDirectories =
               Directory.GetDirectories(dbSettings.DatabaseFolder, $"{dbSettings.SensorValuesDatabaseName}*", SearchOption.TopDirectoryOnly);

            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);

                var databases = InitializeAndGetDatabases(from, to);
                foreach (var dbPath in Directory.GetDirectories(directory))
                    databases.OpenDatabase(dbPath);
            }

            _sensorDbs = _sensorDbs.OrderByDescending(db => db.From).ToList();
        }


        internal ISensorValuesDatabase GetNewestDatabases(long time)
        {
            var newestDbs = _sensorDbs.FirstOrDefault();
            if (newestDbs != null && newestDbs.From <= time && newestDbs.To >= time)
                return newestDbs;

            var from = DateTimeMethods.GetMinDateTimeTicks(time);
            var to = DateTimeMethods.GetMaxDateTimeTicks(time);

            return InsertAndGetNewDatabases(from, to);
        }

        internal List<ISensorValuesDatabase> GetAllDatabases() => _sensorDbs.ToList();


        private ISensorValuesDatabase InitializeAndGetDatabases(long from, long to)
        {
            var databases = LevelDBManager.GetSensorValuesDatabaseInstance(from, to);

            _sensorDbs.Add(databases);

            return databases;
        }

        private ISensorValuesDatabase InsertAndGetNewDatabases(long from, long to)
        {
            var databases = LevelDBManager.GetSensorValuesDatabaseInstance(from, to);

            _sensorDbs.Insert(0, databases);

            return databases;
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
    }
}
