using System.IO;

namespace HSMDatabase.AccessManager
{
    public interface IDatabaseSettings
    {
        public string DatabaseFolder { get; init; }

        public string EnvironmentDatabaseName { get; init; }

        public string SnaphotsDatabaseName { get; init; }

        public string SensorValuesDatabaseName { get; init; }

        public string JournalValuesDatabaseName { get; init; }


        public string PathToEnvironmentDb => Path.Combine(DatabaseFolder, EnvironmentDatabaseName);

        public string PathToSnaphotsDb => Path.Combine(DatabaseFolder, SnaphotsDatabaseName);


        public string GetPathToSensorValueDatabase(long from, long to) => BuildWeeklyDbName(DatabaseFolder, SensorValuesDatabaseName, from, to);

        public string GetPathToJournalValueDatabase(long from, long to) => BuildWeeklyDbName(Path.Combine(DatabaseFolder, "Journals"), JournalValuesDatabaseName, from, to);


        private static string BuildWeeklyDbName(string folder, string dbName, long from, long to) => Path.Combine(folder, $"{dbName}_{from}_{to}");
    }
}
