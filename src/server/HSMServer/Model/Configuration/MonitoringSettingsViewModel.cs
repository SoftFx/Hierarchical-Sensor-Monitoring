using HSMServer.ServerConfiguration;

namespace HSMServer.Model.Configuration
{
    public class MonitoringSettingsViewModel
    {
        public int DatabaseStatisticsPeriodDays { get; set; }

        public int TopHeaviestSensorsCount { get; set; }

        public bool IsMonitoringEnabled { get; set; }


        public MonitoringSettingsViewModel() { }

        public MonitoringSettingsViewModel(IServerConfig config)
        {
            DatabaseStatisticsPeriodDays = config.MonitoringOptions.DatabaseStatisticsPeriodDays;
            TopHeaviestSensorsCount = config.MonitoringOptions.TopHeaviestSensorsCount;
            IsMonitoringEnabled = config.MonitoringOptions.IsMonitoringEnabled;
        }
    }
}
