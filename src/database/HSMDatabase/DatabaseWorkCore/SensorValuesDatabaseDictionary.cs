using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System;
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

            if (CheckoutAndMigration())
                return;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);
                AddNewDb(directory, from, to);
            }
        }

        private bool CheckoutAndMigration()
        {
            bool wereMigrated = false;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                var sensorDirectories = Directory.GetDirectories(directory);
                if (sensorDirectories.Length == 0)
                    continue;

                wereMigrated = true;

                (var from, var to) = GetDatesFromFolderName(directory);

                from -= 1;
                to -= 1;

                bool shouldAddDb = true;

                if (_lastDb != null)
                {
                    var fromDate = new DateTime(from);
                    var lastDbFromDate = new DateTime(_lastDb.From);

                    shouldAddDb = fromDate.Year != lastDbFromDate.Year || fromDate.Month != lastDbFromDate.Month || fromDate.Day != lastDbFromDate.Day;
                }

                if (shouldAddDb)
                    AddNewDb(_dbSettings.GetPathToSensorValueDatabase(from, to), from, to);

                try
                {
                    foreach (var dbPath in sensorDirectories)
                    {
                        try
                        {
                            var sensorDb = new LevelDBDatabaseAdapter(dbPath);
                            var sensorId = Path.GetFileName(dbPath);

                            var allValues = sensorDb.GetAllValues();
                            foreach (var (sensorReceivingTime, value) in allValues)
                                _lastDb.PutSensorValue(sensorId, sensorReceivingTime, value);

                            sensorDb.Dispose();
                        }
                        catch { }
                    }

                    Directory.Delete(directory, true);
                }
                catch { }
            }

            return wereMigrated;
        }

        private List<string> GetSensorValuesDirectories()
        {
            var sensorValuesDirectories =
               Directory.GetDirectories(_dbSettings.DatabaseFolder, $"{_dbSettings.SensorValuesDatabaseName}*", SearchOption.TopDirectoryOnly);

            return sensorValuesDirectories.OrderBy(d => d).ToList();
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


        internal static (long from, long to) GetDatesFromFolderName(string folder)
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

        public IEnumerator<ISensorValuesDatabase> GetEnumerator() => _sensorDbs.Reverse().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
