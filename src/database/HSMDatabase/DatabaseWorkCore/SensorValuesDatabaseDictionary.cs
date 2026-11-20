using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.Sealed;
using System;
using System.IO;
using System.Linq;


namespace HSMDatabase.DatabaseWorkCore
{
    // sealedWeeks: false for the dictionary of the pre-V2 databases that MigrateDatabaseV2 reads; sealed
    // weeks (ADR-0020, #1803) belong to the current row databases only.
    // utcNow: the clock a sealed week is opened with (#1805 follow-up); null: the system clock.
    internal sealed class SensorValuesDatabaseDictionary(IDatabaseSettings dbSettings, bool sealedWeeks = true, Func<DateTime> utcNow = null)
        : ValuesDatabaseDictionary<ISensorValuesDatabase>(dbSettings)
    {
        // A field initializer: it runs before the base constructor, which lists the directories.
        private readonly bool _sealedWeeks = sealedWeeks;
        private readonly Func<DateTime> _utcNow = utcNow;


        // A week is sealed when its sibling chunk database is complete. Each week sits behind a handle the sealing job
        // (#1804) can swap from rows to sealed and back while the server runs.
        protected override Func<string, long, long, ISensorValuesDatabase> CreateDb => _sealedWeeks
            ? (path, from, to) => new SensorValuesWeek(path, _dbSettings.GetPathToSealedSensorValueDatabase(from, to), from, to, _utcNow)
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
