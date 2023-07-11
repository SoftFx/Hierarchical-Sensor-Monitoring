using HSMDatabase.AccessManager;

namespace HSMDatabase.Settings
{
    public sealed record DatabaseSettings : IDatabaseSettings
    {
        private const string DefaultDatabaseFolder = "Databases";
        private const string DefaultSnaphotsDatabaseName = "Shapshots";
        private const string DefaultEnvironmentDatabaseName = "EnvironmentData";
        private const string DefaultSensorValuesDatabaseName = "SensorValues";


        public string DatabaseFolder { get; init; } = DefaultDatabaseFolder;

        public string SnaphotsDatabaseName { get; init; } = DefaultSnaphotsDatabaseName;

        public string EnvironmentDatabaseName { get; init; } = DefaultEnvironmentDatabaseName;

        public string SensorValuesDatabaseName { get; init; } = DefaultSensorValuesDatabaseName;
    }
}
