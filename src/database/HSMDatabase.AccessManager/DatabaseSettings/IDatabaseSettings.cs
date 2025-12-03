using System.IO;

namespace HSMDatabase.AccessManager
{
    public interface IDatabaseSettings
    {
        public string DatabaseBackupsFolder { get; init; }

        public string DatabaseFolder { get; init; }

        public string JournalFolder { get; init; }

        public string ExportFolder  { get; init; }

        public string JournalValuesDatabaseName { get; init; }

        public string SensorValuesDatabaseName { get; init; }

        public string ServerLayoutDatabaseName { get; init; }

        public string EnvironmentDatabaseName { get; init; }

        public string SnaphotsDatabaseName { get; init; }


        public string PathToServerLayoutDb => Path.Combine(DatabaseFolder, ServerLayoutDatabaseName);

        public string PathToEnvironmentDb => Path.Combine(DatabaseFolder, EnvironmentDatabaseName);

        public string PathToSnaphotsDb => Path.Combine(DatabaseFolder, SnaphotsDatabaseName);

        public string PathToJournalDb => Path.Combine(DatabaseFolder, JournalFolder);

        public string PathToExport => Path.Combine(DatabaseFolder, ExportFolder);


        public string GetPathToJournalValueDatabase(long from, long to) => BuildWeeklyDbName(PathToJournalDb, JournalValuesDatabaseName, from, to);

        public string GetPathToSensorValueDatabase(long from, long to) => BuildWeeklyDbName(DatabaseFolder, SensorValuesDatabaseName, from, to);


        private static string BuildWeeklyDbName(string folder, string dbName, long from, long to) => Path.Combine(folder, $"{dbName}_{from}_{to}");
    }
}
