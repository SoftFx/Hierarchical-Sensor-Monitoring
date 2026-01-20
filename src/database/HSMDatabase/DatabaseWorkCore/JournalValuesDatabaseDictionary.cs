using System;
using System.IO;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;


namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class JournalValuesDatabaseDictionary : ValuesDatabaseDictionary<IJournalValuesDatabase>
    {
        protected override Func<string, long, long, IJournalValuesDatabase> CreateDb => LevelDBManager.GetJournalValuesDatabaseInstance;

        protected override Func<long, long, string> GetDbPath => _dbSettings.GetPathToJournalValueDatabase;

        protected override string _folderTemplate => $"{_dbSettings.JournalValuesDatabaseName}*";

        protected override string _databaseFolder => _dbSettings.PathToJournalDb;

        internal JournalValuesDatabaseDictionary(IDatabaseSettings dbSettings) : base(dbSettings)
        {
            if (!Directory.Exists(_databaseFolder))
                Directory.CreateDirectory(_databaseFolder);
        }

    }
}