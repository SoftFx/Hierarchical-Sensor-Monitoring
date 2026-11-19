using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System;
using System.IO;
using System.Linq;


namespace HSMDatabase.DatabaseWorkCore
{
    // sealedWeeks: false for the dictionary of the pre-V2 databases that MigrateDatabaseV2 reads; sealed
    // weeks (ADR-0020, #1803) belong to the current row databases only.
    internal sealed class SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings, bool sealedWeeks = true)
        : ValuesDatabaseDictionary<ISensorValuesDatabase>(dbSettings)
    {
        // A field initializer: it runs before the base constructor, which lists the directories.
        private readonly bool _sealedWeeks = sealedWeeks;


        // A week is sealed when its sibling chunk database is complete.
        protected override Func<string, long, long, ISensorValuesDatabase> CreateDb => _sealedWeeks
            ? (path, from, to) => LevelDBManager.GetSensorValuesDatabaseInstance(path, _dbSettings.GetPathToSealedSensorValueDatabase(from, to), from, to)
            : LevelDBManager.GetSensorValuesDatabaseInstance;

        protected override Func<long, long, string> GetDbPath => _dbSettings.GetPathToSensorValueDatabase;
        protected override string _folderTemplate => $"{_dbSettings.SensorValuesDatabaseName}_*";
        protected override string _databaseFolder => _dbSettings.DatabaseFolder;


        // A complete sealed week is a week even if its row directory is gone: it is listed, and opening it
        // creates an empty overlay. An incomplete sealed directory alone is not a week.
        protected override string[] GetSensorValuesDirectories()
        {
            var directories = base.GetSensorValuesDirectories();

            if (!_sealedWeeks || !Directory.Exists(_databaseFolder))
                return directories;

            var sealedWeeks = Directory.GetDirectories(_databaseFolder, $"{SealedWeekMarker.DirectoryPrefix}_*", SearchOption.TopDirectoryOnly)
                .Where(SealedWeekMarker.ExistsIn);

            return [.. directories, .. sealedWeeks];
        }
    }
}
