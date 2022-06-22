using HSMDatabase.AccessManager;

namespace HSMDatabase.Settings
{
    public sealed record DatabaseSettings : IDatabaseSettings
    {
        private const string DefaultDatabaseFolder = "Databases";
        private const string DefaultEnvironmentDatabaseName = "EnvironmentData";
        private const string DefaultMonitoringDatabaseName = "MonitoringData";

        public string DatabaseFolder { get; init; }
        public string EnvironmentDatabaseName { get; init; }
        public string MonitoringDatabaseName { get; init; }

        public DatabaseSettings()
        {
            DatabaseFolder = DefaultDatabaseFolder;
            EnvironmentDatabaseName = DefaultEnvironmentDatabaseName;
            MonitoringDatabaseName = DefaultMonitoringDatabaseName;
        }
    }
}
