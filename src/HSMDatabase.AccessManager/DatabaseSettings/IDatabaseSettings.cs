namespace HSMDatabase.AccessManager
{
    public interface IDatabaseSettings
    {
        public string DatabaseFolder { get; init; }
        public string EnvironmentDatabaseName { get; init; }
        public string MonitoringDatabaseName { get; init; }
    }
}
