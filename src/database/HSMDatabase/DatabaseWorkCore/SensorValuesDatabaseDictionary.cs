using System;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;


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