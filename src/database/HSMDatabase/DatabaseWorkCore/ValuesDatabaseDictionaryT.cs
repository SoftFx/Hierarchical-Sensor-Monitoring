using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMDatabase.AccessManager;


namespace HSMDatabase.DatabaseWorkCore
{
    internal abstract class ValuesDatabaseDictionary<T> : IEnumerable<T> where T : IDisposable
    {
        private readonly ConcurrentDictionary<long, T> _dbs = new();


        protected readonly IDatabaseSettings _dbSettings;

        protected abstract Func<string, long, long, T> CreateDb { get; }
        protected abstract Func<long, long, string> GetDbPath { get; }
        protected abstract string _folderTemplate { get; }

        internal ValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            _dbSettings = dbSettings;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);
                var db = CreateDb.Invoke(directory, from, to);
                _dbs.TryAdd(from, db);
            }
        }

        internal T GetDatabaseByTime(long time)
        {
            var from = DateTimeMethods.GetStartOfWeekTicks(time);

            var to = DateTimeMethods.GetEndOfWeekTicks(time);

            return _dbs.GetOrAdd(from, key =>
            {
                var to = DateTimeMethods.GetEndOfWeekTicks(time);
                string name = GetDbPath.Invoke(key, to);
                return CreateDb.Invoke(name, key, to);
            });
        }


        private string[] GetSensorValuesDirectories()
        {
            if (!Directory.Exists(_dbSettings.DatabaseFolder))
                return [];

            return Directory.GetDirectories(
                _dbSettings.DatabaseFolder,
                _folderTemplate,
                SearchOption.TopDirectoryOnly
            );
        }

        private static (long from, long to) GetDatesFromFolderName(string folder)
        {
            var splitResults = folder.Split('_');
            long from = 0L, to = 0L;

            if (splitResults.Length >= 3)
            {
                if (long.TryParse(splitResults[1], out long fromTicks))
                    from = new DateTime(fromTicks).Date.Ticks;

                if (long.TryParse(splitResults[2], out long toTicks))
                    to = new DateTime(toTicks).Date.Ticks;
            }

            return (from, to);
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var db in _dbs.Values.ToList())
            {
                yield return db;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}