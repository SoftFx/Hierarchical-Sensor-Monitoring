using HSMDatabase.AccessManager;
using LevelDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HSMDatabase.DatabaseWorkCore
{
    internal abstract class ValuesDatabaseDictionary<T> : IEnumerable<T> where T : IDisposable
    {
        private readonly SortedDictionary<long, Shard> _dbs = new(Comparer<long>.Create((x, y) => y.CompareTo(x)));
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
                        _dbs.Add(from, CreateShard(from, to));
                }
            }
        }

        internal T GetDatabaseByTime(long time)
        {
            var from = DateTimeMethods.GetStartOfWeekTicks(time);
            var to = DateTimeMethods.GetEndOfWeekTicks(time);

            if (_dbs.TryGetValue(from, out var existingShard))
                return existingShard.Database;

            lock (_lock)
            {
                if (!_dbs.TryGetValue(from, out var shard))
                {
                    shard = CreateShard(from, to);

                    _dbs.Add(from, shard);
                }

                return shard.Database;
            }
        }

        /// <summary>
        /// Every known shard, newest week first, WITHOUT opening the ones not opened yet:
        /// a caller that only needs the name, the week or the marker of a shard must not
        /// pay for opening the LevelDB database behind it (#1801).
        /// </summary>
        internal List<Shard> GetShards()
        {
            lock (_lock)
            {
                return [.. _dbs.Values];
            }
        }

        private Shard CreateShard(long from, long to)
        {
            var path = GetDbPath.Invoke(from, to);

            return new Shard(from, path, new Lazy<T>(() => CreateDb.Invoke(path, from, to), LazyThreadSafetyMode.ExecutionAndPublication));
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
            foreach (var shard in GetShards())
            {
                yield return shard.Database;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        internal sealed class Shard(long from, string path, Lazy<T> database)
        {
            public long From { get; } = from;

            public string Path { get; } = path;

            public bool IsOpened => database.IsValueCreated;

            /// <summary>Opens the shard on first use.</summary>
            public T Database => database.Value;
        }
    }
}