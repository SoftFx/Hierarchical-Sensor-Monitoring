using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMDatabase.AccessManager;


namespace HSMDatabase.DatabaseWorkCore
{
    internal abstract class ValuesDatabaseDictionary<T> : IEnumerable<T> where T : IDisposable
    {
        private readonly Dictionary<long, T> _dbs = new();
        private readonly object _lock = new();


        protected readonly IDatabaseSettings _dbSettings;

        protected abstract Func<string, long, long, T> CreateDb { get; }

        protected abstract Func<long, long, string> GetDbPath { get; }

        protected abstract string _folderTemplate { get;  }

        internal ValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            _dbSettings = dbSettings;

            var sensorValuesDirectories = GetSensorValuesDirectories();
            foreach (var directory in sensorValuesDirectories)
            {
                (var from, var to) = GetDatesFromFolderName(directory);
                AddNewDatabase(directory, from, to);
            }
        }


        internal T GetDatabaseByTime(long time)
        {
            lock (_lock)
            {
                var from = DateTimeMethods.GetStartOfWeekTicks(time);

                if (_dbs.TryGetValue(from, out var db))
                    return db;

                var to = DateTimeMethods.GetEndOfWeekTicks(time);

                return AddNewDatabase(GetDbPath.Invoke(from, to), from, to);
            }
        }

        private T AddNewDatabase(string name, long from, long to)
        {
            lock(_lock)
            {
                T newDb = CreateDb.Invoke(name, from , to);

                _dbs.Add(from, newDb);

                return newDb;
            }
        }

        private string[] GetSensorValuesDirectories()
        {
            if (!Directory.Exists(_dbSettings.DatabaseFolder))
                return [];

            return Directory.GetDirectories(_dbSettings.DatabaseFolder, _folderTemplate, SearchOption.TopDirectoryOnly);

        }

        private static (long from, long to) GetDatesFromFolderName(string folder)
        {
            var from = 0L;
            var to = 0L;

            var splitResults = folder.Split('_');

            if (long.TryParse(splitResults[1], out long fromTicks))
                from = new DateTime(fromTicks).Date.Ticks;

            if (long.TryParse(splitResults[2], out long toTicks))
                to = new DateTime(toTicks).Date.Ticks;

            return (from, to);
        }

        public IEnumerator<T> GetEnumerator()
        {
            List<T> copy;
            lock (_lock)
            {
                copy = _dbs.Values.ToList();
            }

            foreach (var db in copy)
            {
                yield return db;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    }
}