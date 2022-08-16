using System.IO;

namespace HSMDatabase.AccessManager
{
    public interface IDatabaseSettings
    {
        public string DatabaseFolder { get; init; }
        public string EnvironmentDatabaseName { get; init; }
        public string MonitoringDatabaseName { get; init; }
        public string SensorValuesDatabaseName { get; init; }


        public string GetPathToEnvironmentDatabase() =>
            Path.Combine(DatabaseFolder, EnvironmentDatabaseName);

        public string GetPathToMonitoringDatabase(string dbName) =>
            Path.Combine(DatabaseFolder, dbName);

        public string GetPathToSensorValueDatabase(long from, long to) =>
            Path.Combine(DatabaseFolder, $"{SensorValuesDatabaseName}_{from}_{to}");
    }
}
