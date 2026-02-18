using HSMDatabase.AccessManager;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;


namespace HSMDatabase.DatabaseWorkCore
{
    internal abstract class ValuesDatabaseDictionary<T> : IEnumerable<T> where T : IDisposable
    {
        private readonly SortedDictionary<long, Lazy<T>> _dbs = new(Comparer<long>.Create((x, y) => y.CompareTo(x)));
        private readonly object _lock = new();


        protected readonly IDatabaseSettings _dbSettings;

        protected abstract Func<string, long, long, T> CreateDb { get; }
        protected abstract Func<long, long, string> GetDbPath { get; }

        protected abstract string _databaseFolder { get; }

        protected abstract string _folderTemplate { get; }

        internal ValuesDatabaseDictionary(IDatabaseSettings dbSettings)
        {
            _dbSettings = dbSettings;
            InitializeDatabases();
        }

        private void InitializeDatabases()
        {
            var sensorValuesDirectories = GetSensorValuesDirectories();

            lock (_lock)
            {
                foreach (var directory in sensorValuesDirectories)
                {
                    (var from, var to) = GetDatesFromFolderName(directory);
                    if (!_dbs.ContainsKey(from))
                    {
                        var db = new Lazy<T>(() => CreateDb.Invoke(directory, from, to));
                        _dbs.Add(from, db);
                    }
                }
            }
        }

        internal T GetDatabaseByTime(long time)
        {
            var from = DateTimeMethods.GetStartOfWeekTicks(time);
            var to   = DateTimeMethods.GetEndOfWeekTicks(time);

            if (_dbs.TryGetValue(from, out var db))
                return db.Value;

            lock (_lock)
            {
                if (!_dbs.TryGetValue(from, out var lazyDb))
                {
                    lazyDb = new Lazy<T>(() =>
                        {
                            string name = GetDbPath.Invoke(from, to);
                            return CreateDb.Invoke(name, from, to);
                        }, LazyThreadSafetyMode.ExecutionAndPublication);

                    _dbs.Add(from, lazyDb);
                }
                return lazyDb.Value;
            }
        }


        private string[] GetSensorValuesDirectories()
        {
            if (!Directory.Exists(_databaseFolder))
                return [];

            return Directory.GetDirectories(
                _databaseFolder,
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
            List<Lazy<T>> dbCopies;

            lock (_lock)
            {
                dbCopies = [.. _dbs.Values];
            }

            foreach (var db in dbCopies)
            {
                yield return db.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}