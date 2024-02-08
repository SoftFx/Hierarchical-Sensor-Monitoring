using HSMDatabase.AccessManager;

namespace HSMDatabase.Settings
{
    public sealed record DatabaseSettings : IDatabaseSettings
    {
        private const string DefaultDatabaseBackupsFolder = "DatabasesBackups";
        private const string DefaultSnaphotsDatabaseName = "Snapshots";
        private const string DefaultDatabaseFolder = "Databases";
        private const string DefaultJournalFolder = "Journals";

        private const string DefaultEnvironmentDatabaseName = "EnvironmentData";
        private const string DefaultSensorValuesDatabaseName = "SensorValues";
        private const string DefaultJournalValuesDatabaseName = "JournalValues";
        private const string DefaultServerLayoutDatabaseName = "ServerLayout";


        public string DatabaseFolder { get; init; } = DefaultDatabaseFolder;

        public string DatabaseBackupsFolder { get; init; } = DefaultDatabaseBackupsFolder;

        public string JournalFolder { get; init; } = DefaultJournalFolder;


        public string SnaphotsDatabaseName { get; init; } = DefaultSnaphotsDatabaseName;

        public string EnvironmentDatabaseName { get; init; } = DefaultEnvironmentDatabaseName;

        public string SensorValuesDatabaseName { get; init; } = DefaultSensorValuesDatabaseName;

        public string JournalValuesDatabaseName { get; init; } = DefaultJournalValuesDatabaseName;

        public string ServerLayoutDatabaseName { get; init; } = DefaultServerLayoutDatabaseName;
    }
}