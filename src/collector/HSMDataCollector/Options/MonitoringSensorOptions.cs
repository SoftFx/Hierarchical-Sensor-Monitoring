using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;

namespace HSMDataCollector.Options
{
    public class SensorOptions2
    {
        public string Description { get; set; }

        public string Path { get; set; }

        public Unit? SensorUnit { get; set; }


        public TimeSpan? KeepHistory { get; set; }

        public TimeSpan? SelfDestroy { get; set; }

        public TimeSpan? TTL { get; set; }


        public bool EnableForGrafana { get; set; }


        public bool OnlyUniqValues { get; set; }


        internal SensorType Type { get; private set; }

        internal bool HasSettings => KeepHistory.HasValue || SelfDestroy.HasValue || TTL.HasValue;

        internal string SensorName { get; set; } //???

        internal SensorOptions2 SetType(SensorType type)
        {
            Type = type;

            return this;
        }
    }


    public class SensorOptions
    {
        public string NodePath { get; set; }

        internal string SensorName { get; set; }
    }


    public class MonitoringSensorOptions : SensorOptions2
    {
        internal virtual TimeSpan DefaultPostDataPeriod { get; } = TimeSpan.FromSeconds(15);


        public TimeSpan PostDataPeriod { get; set; }


        public MonitoringSensorOptions()
        {
            PostDataPeriod = DefaultPostDataPeriod;
        }
    }


    public class BarSensorOptions : MonitoringSensorOptions
    {
        public TimeSpan CollectBarPeriod { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int Precision { get; set; } = 2;
    }


    public sealed class DiskSensorOptions : MonitoringSensorOptions
    {
        internal override TimeSpan DefaultPostDataPeriod { get; } = TimeSpan.FromMinutes(5);


        public string TargetPath { get; set; } = @"C:\";

        public int CalibrationRequests { get; set; } = 6;
    }


    public sealed class WindowsSensorOptions : MonitoringSensorOptions
    {
        internal override TimeSpan DefaultPostDataPeriod { get; } = TimeSpan.FromHours(12);


        public TimeSpan AcceptableUpdateInterval { get; set; } = TimeSpan.FromDays(30);
    }


    public sealed class VersionSensorOptions : SensorOptions2
    {
        public Version Version { get; set; }

        public new string SensorName { get; set; }

        public DateTime StartTime { get; set; }
    }


    public sealed class ServiceSensorOptions : SensorOptions2
    {
        public string ServiceName { get; set; }
    }


    public sealed class CollectorInfoOptions : SensorOptions2
    {
        internal const string BaseCollectorPath = "Product Info/Collector";
    }


    public sealed class CollectorMonitoringInfoOptions : MonitoringSensorOptions { }
}
