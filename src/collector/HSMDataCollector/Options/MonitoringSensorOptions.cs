using System;

namespace HSMDataCollector.Options
{
    public class SensorOptions
    {
        public string NodePath { get; set; }
    }


    public class MonitoringSensorOptions : SensorOptions
    {
        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);
    }


    public sealed class BarSensorOptions : MonitoringSensorOptions
    {
        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public TimeSpan CollectBarPeriod { get; set; } = TimeSpan.FromSeconds(5);
    }


    public sealed class DiskSensorOptions : MonitoringSensorOptions
    {
        public string TargetPath { get; set; } = @"C:\";

        public int CalibrationRequests { get; set; } = 6;
    }


    public sealed class WindowsSensorOptions : MonitoringSensorOptions
    {
        public TimeSpan AcceptableUpdateInterval { get; set; } = TimeSpan.FromDays(30);
    }


    public sealed class VersionSensorOptions : SensorOptions
    {
        public Version Version { get; set; }

        public string SensorName { get; set; }

        public DateTime StartTime { get; set; }
    }


    public sealed class CollectorInfoOptions : SensorOptions
    {
        internal const string BaseCollectorPath = "Product Info/Collector";
    }


    public sealed class CollectorMonitoringInfoOptions : MonitoringSensorOptions { }
}
