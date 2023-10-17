using HSMDataCollector.DefaultSensors.SystemInfo;
using System;

namespace HSMDataCollector.Options
{
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


    public sealed class DiskBarSensorOptions : BarSensorOptions
    {
        internal IDiskInfo DiskInfo { get; private set; }

        public string TargetPath { get; set; } = DiskSensorOptions.DefaultTargetPath;


        internal DiskBarSensorOptions SetInfo(IDiskInfo info)
        {
            DiskInfo = info;

            return this;
        }
    }


    public sealed class VersionSensorOptions : InstantSensorOptions
    {
        public Version Version { get; set; }

        public DateTime StartTime { get; set; }


        public VersionSensorOptions() { }

        public VersionSensorOptions(Version version)
        {
            Version = version;
        }
    }


    public sealed class ServiceSensorOptions : InstantSensorOptions
    {
        public string ServiceName { get; set; }


        public ServiceSensorOptions() { }

        public ServiceSensorOptions(string serviceName)
        {
            ServiceName = serviceName;
        }
    }


    public sealed class WindowsInfoSensorOptions : MonitoringInstantSensorOptions
    {
        public WindowsInfoSensorOptions()
        {
            PostDataPeriod = TimeSpan.FromHours(12);
        }
    }

    public sealed class CollectorMonitoringInfoOptions : MonitoringInstantSensorOptions { }
}