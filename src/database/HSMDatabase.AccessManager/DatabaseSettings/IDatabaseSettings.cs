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


        public string GetPathToSensorValueDatabase(long from, long to) => BuildPath(DatabaseFolder, BuildValueDataBaseName(SensorValuesDatabaseName, from, to));

        public string GetPathToJournalValueDatabase(long from, long to) => BuildPath(BuildPath(DatabaseFolder, "Journals"), BuildValueDataBaseName(JournalValuesDatabaseName, from, to));


        private static string BuildValueDataBaseName(string dbName, long from, long to) => $"{dbName}_{from}_{to}";

        private static string BuildPath(string folder, string name) => Path.Combine(folder, name);
    }
}
