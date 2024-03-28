namespace HSMServer.ServerConfiguration
{
    public sealed class MonitoringOptions
    {
        public int DatabaseStatisticsPeriodDays { get; set; } = 1;

        public int TopHeaviestSensorsCount { get; set; } = 10;

        public bool UseDatabaseMonitoring { get; set; }

        public bool UseRequestMonitoring { get; set; }
    }
}
