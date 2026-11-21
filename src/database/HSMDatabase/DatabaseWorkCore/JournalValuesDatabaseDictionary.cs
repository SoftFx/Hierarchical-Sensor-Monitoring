using System;
using System.IO;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;


namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class JournalValuesDatabaseDictionary : ValuesDatabaseDictionary<IJournalValuesDatabase>
    {
        // Read when a shard is opened, which is never before the constructor has run (shards open lazily).
        private readonly Func<DateTime> _utcNow;


        protected override Func<string, long, long, IJournalValuesDatabase> CreateDb =>
            (path, from, to) => LevelDBManager.GetJournalValuesDatabaseInstance(path, from, to, _utcNow);

        protected override Func<long, long, string> GetDbPath => _dbSettings.GetPathToJournalValueDatabase;

        protected override string _folderTemplate => $"{_dbSettings.JournalValuesDatabaseName}*";

        protected override string _databaseFolder => _dbSettings.PathToJournalDb;

        // utcNow: the clock a shard records its last reclaimable change by (#1801); null: the system clock.
        internal JournalValuesDatabaseDictionary(IDatabaseSettings dbSettings, Func<DateTime> utcNow = null) : base(dbSettings)
        {
            _utcNow = utcNow;

            if (!Directory.Exists(_databaseFolder))
                Directory.CreateDirectory(_databaseFolder);
        }

    }
}