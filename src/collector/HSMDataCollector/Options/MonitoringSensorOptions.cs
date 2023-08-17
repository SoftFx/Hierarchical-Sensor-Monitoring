using HSMDataCollector.Alerts;
using HSMDataCollector.Converters;
using HSMDataCollector.SensorsMetainfo;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Options
{
    public class InstantSensorOptions : SensorOptions2
    {
        public List<InstantAlertTemplate> Alerts { get; set; } = new List<InstantAlertTemplate>();


        internal override SensorMetainfo Metainfo => this.ToInfo();


        internal InstantSensorOptions SetType(SensorType type)
        {
            Type = type;

            return this;
        }
    }


    public class MonitoringSensorOptions2 : SensorOptions2
    {
        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);

        internal override SensorMetainfo Metainfo => null;
    }


    public class BarSensorOptions2 : MonitoringSensorOptions2
    {
        public List<BarAlertTemplate> Alerts { get; set; } = new List<BarAlertTemplate>();


        public TimeSpan CollectBarPeriod { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);


        public int Precision { get; set; } = 2;


        internal override SensorMetainfo Metainfo => this.ToInfo();


        internal BarSensorOptions2 SetType(SensorType type)
        {
            Type = type;

            return this;
        }
    }


    public abstract class SensorOptions2
    {
        public SpecialAlertTemplate TtlAlert { get; set; }

        public Unit? SensorUnit { get; set; }


        public string Description { get; set; }

        public string Path { get; set; }


        public TimeSpan? KeepHistory { get; set; }

        public TimeSpan? SelfDestroy { get; set; }

        public TimeSpan? TTL { get; set; }


        public bool EnableForGrafana { get; set; }


        public bool OnlyUniqValues { get; set; }



        internal abstract SensorMetainfo Metainfo { get; }

        internal SensorType Type { get; private protected set; }

        internal bool HasSettings => KeepHistory.HasValue || SelfDestroy.HasValue || TTL.HasValue;

        internal string SensorName { get; set; } //???
    }


    public class SensorOptions
    {
        public string NodePath { get; set; }

        internal string SensorName { get; set; }
    }


    public class MonitoringSensorOptions : BarSensorOptions2
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


    public sealed class VersionSensorOptions : InstantSensorOptions
    {
        public Version Version { get; set; }

        public new string SensorName { get; set; }

        public DateTime StartTime { get; set; }
    }


    public sealed class ServiceSensorOptions : InstantSensorOptions
    {
        public string ServiceName { get; set; }
    }


    public sealed class CollectorInfoOptions : InstantSensorOptions
    {
        internal const string BaseCollectorPath = "Product Info/Collector";
    }


    public sealed class CollectorMonitoringInfoOptions : MonitoringSensorOptions { }
}
