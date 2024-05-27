using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class MonitoringSettingsViewModel
    {
        [Display(Name = "Period of collecting database statistics")]
        public int DatabaseStatisticsPeriodDays { get; set; }

        [Display(Name = "Top heaviest sensors count")]
        public int TopHeaviestSensorsCount { get; set; }

        [Display(Name = "Enable self monitoring")]
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
