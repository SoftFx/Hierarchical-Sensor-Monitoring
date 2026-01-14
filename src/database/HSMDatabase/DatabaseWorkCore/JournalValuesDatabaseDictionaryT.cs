//using HSMDatabase.AccessManager;
//using HSMDatabase.LevelDB;
//using System;
//using System.Collections;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;

//namespace HSMDatabase.DatabaseWorkCore;

//internal sealed class JournalValuesDatabaseDictionary : ValuesDatabaseDictionary<IJournalValuesDatabase>
//{
//    private string DataBasePath => _dbSettings.PathToJournalDb;

//    protected override Func<string, long, long, IJournalValuesDatabase> CreateDb => LevelDBManager.GetJournalValuesDatabaseInstance;

//    protected override Func<long, long, string> GetDbPath => _dbSettings.GetPathToJournalValueDatabase;

//    protected override string _folderTemplate => $"{_dbSettings.JournalValuesDatabaseName}*";

//    internal JournalValuesDatabaseDictionary(IDatabaseSettings dbSettings) : base(dbSettings)
//    {
//        if (!Directory.Exists(DataBasePath))
//            Directory.CreateDirectory(DataBasePath);
//    }

//}