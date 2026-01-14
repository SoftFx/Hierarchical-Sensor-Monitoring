using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class SensorValuesDatabaseDictionary : ValuesDatabaseDictionary<ISensorValuesDatabase>
    {
        public SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings) : base(dbSettings)
        {
        }

        protected override Func<string, long, long, ISensorValuesDatabase> CreateDb => LevelDBManager.GetSensorValuesDatabaseInstance;

        protected override Func<long, long, string> GetDbPath => _dbSettings.GetPathToSensorValueDatabase;
        protected override string _folderTemplate => $"{_dbSettings.SensorValuesDatabaseName}_*";
    }
}