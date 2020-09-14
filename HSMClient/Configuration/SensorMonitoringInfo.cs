using System;

namespace HSMClient.Configuration
{
    public class SensorMonitoringInfo
    {
        public string Name { get; set; }
        public string MachineName { get; set; }
        public TimeSpan WarningPeriod { get; set; }
        public TimeSpan ErrorPeriod { get; set; }
        public TimeSpan UpdatePeriod { get; set; }
    }
}
