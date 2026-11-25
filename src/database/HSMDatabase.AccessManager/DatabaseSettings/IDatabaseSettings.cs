using System.IO;

namespace HSMDatabase.AccessManager
{
    public interface IDatabaseSettings
    {
        public string DatabaseBackupsFolder { get; init; }

        public string DatabaseRestoreTempFolder { get; init; }

        public string DatabaseFolder { get; init; }

        public string JournalFolder { get; init; }

        public string ExportFolder  { get; init; }

        public string JournalValuesDatabaseName { get; init; }

        public string SensorValuesDatabaseName { get; init; }

        public string ServerLayoutDatabaseName { get; init; }

        public string EnvironmentDatabaseName { get; init; }

        public string SnaphotsDatabaseName { get; init; }

        // The undo-journal table (#1570) — a dedicated LevelDB folder,
        // separate from the sharded presentation Journal.
        public string ChangeSetsDatabaseName { get; init; }

        // The download log (ADR-0022 point 15) — a dedicated LevelDB folder, not the journal.
        public string DownloadLogDatabaseName { get; init; }

        // The folder of the table block store (#1844), under DatabaseFolder.
        public string TableBlocksFolderName { get; init; }


        public string PathToServerLayoutDb => Path.Combine(DatabaseFolder, ServerLayoutDatabaseName);

        public string PathToEnvironmentDb => Path.Combine(DatabaseFolder, EnvironmentDatabaseName);

        public string PathToSnaphotsDb => Path.Combine(DatabaseFolder, SnaphotsDatabaseName);

        public string PathToJournalDb => Path.Combine(DatabaseFolder, JournalFolder);

        public string PathToChangeSetsDb => Path.Combine(DatabaseFolder, ChangeSetsDatabaseName);

        public string PathToDownloadLogDb => Path.Combine(DatabaseFolder, DownloadLogDatabaseName);

        public string PathToExport => Path.Combine(DatabaseFolder, ExportFolder);

        // The block store of table sensors (#1844): the Blocks and BlockIndex databases. Created on first use only.
        public string PathToTableBlocks => Path.Combine(DatabaseFolder, TableBlocksFolderName);


        public string GetPathToJournalValueDatabase(long from, long to) => BuildWeeklyDbName(PathToJournalDb, JournalValuesDatabaseName, from, to);

        public string GetPathToSensorValueDatabase(long from, long to) => BuildWeeklyDbName(DatabaseFolder, SensorValuesDatabaseName, from, to);

        // The chunk database of a sealed week (ADR-0020), next to the week's row database.
        public string GetPathToSealedSensorValueDatabase(long from, long to) => BuildWeeklyDbName(DatabaseFolder, SealedWeekMarker.DirectoryPrefix, from, to);


        private static string BuildWeeklyDbName(string folder, string dbName, long from, long to) => Path.Combine(folder, $"{dbName}_{from}_{to}");
    }
}
