using HSMDataCollector.Alerts;
using HSMDataCollector.Converters;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Options
{
    public interface IMonitoringOptions
    {
        TimeSpan PostDataPeriod { get; set; }
    }


    public class MonitoringInstantSensorOptions : InstantSensorOptions, IMonitoringOptions
    {
        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);
    }


    public class InstantSensorOptions : SensorOptions
    {
        public List<InstantAlertTemplate> Alerts { get; set; } = new List<InstantAlertTemplate>();

        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();
    }


    public class BarSensorOptions : SensorOptions, IMonitoringOptions
    {
        public List<BarAlertTemplate> Alerts { get; set; } = new List<BarAlertTemplate>();


        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan BarTickPeriod { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int Precision { get; set; } = 2;


        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();
    }


    public abstract class SensorOptions
    {
        internal abstract AddOrUpdateSensorRequest ApiRequest { get; }

        internal SensorType Type { get; set; }

        internal string Module { get; set; }

        internal string Path { get; set; }


        public SpecialAlertTemplate TtlAlert { get; set; }


        public string Description { get; set; }

        public Unit? SensorUnit { get; set; }


        public TimeSpan? KeepHistory { get; set; }

        public TimeSpan? SelfDestroy { get; set; }

        public TimeSpan? TTL { get; set; }


        public bool? EnableForGrafana { get; set; }

        public bool? AggregateData { get; set; }
    }


    public sealed class DiskSensorOptions : MonitoringInstantSensorOptions
    {
        internal const int DefaultCalibrationRequests = 6;
        internal const string DefaultTargetPath = @"C:\";


        internal IDiskInfo DiskInfo { get; private set; }


        public int CalibrationRequests { get; set; } = DefaultCalibrationRequests;

        public string TargetPath { get; set; } = DefaultTargetPath;


        internal DiskSensorOptions SetInfo(IDiskInfo info)
        {
            DiskInfo = info;

            return this;
        }
    }


    public sealed class VersionSensorOptions : InstantSensorOptions
    {
        public Version Version { get; set; }

        public DateTime StartTime { get; set; }
    }


    public sealed class ServiceSensorOptions : InstantSensorOptions
    {
        public string ServiceName { get; set; }
    }


    public sealed class WindowsInfoSensorOptions : MonitoringInstantSensorOptions { }

    public sealed class CollectorMonitoringInfoOptions : MonitoringInstantSensorOptions { }
}