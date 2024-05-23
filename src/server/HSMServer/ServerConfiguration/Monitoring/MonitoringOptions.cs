namespace HSMServer.ServerConfiguration
{
    public sealed class MonitoringOptions
    {
        public const int DefaultDatabaseStatisticsPeriodDays = 1;
        public const int DefaultTopHeaviestSensorsCount = 10;
        public const bool DefaultIsMonitoringEnabled = true;


        public int DatabaseStatisticsPeriodDays { get; set; } = DefaultDatabaseStatisticsPeriodDays;

        public int TopHeaviestSensorsCount { get; set; } = DefaultTopHeaviestSensorsCount;

        public bool IsMonitoringEnabled { get; set; } = DefaultIsMonitoringEnabled;
    }
}
