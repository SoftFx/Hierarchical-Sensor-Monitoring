using System;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.SystemInfo;


namespace HSMDataCollector.Options
{
    public sealed class DiskSensorOptions : MonitoringInstantSensorOptions
    {
        internal const int DefaultCalibrationRequests = 6;
        internal const string DefaultTargetPath = @"C:\";


        internal IDiskInfo DiskInfo { get; private set; }


        public int CalibrationRequests { get; set; } = DefaultCalibrationRequests;

        public string TargetPath { get; set; } = DefaultTargetPath;

        /// <summary>
        /// How often the disk-space prediction sensor samples free space to update its drain-speed
        /// EMA, independently of how often it POSTS. Internal on purpose: the 30 s default is part of
        /// the cross-collector contract (the native source uses the same constant), and only the
        /// conformance driver shortens it so a fixture can observe the calibration sequence in
        /// seconds instead of half an hour (#1426).
        /// </summary>
        internal TimeSpan SpaceCheckPeriod { get; set; } =
            TimeSpan.FromSeconds(FreeDiskSpacePredictionBase.DefaultSpaceCheckPeriodInSec);


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

        public DateTime? StartTime { get; set; }


        public VersionSensorOptions() { }

        public VersionSensorOptions(Version version)
        {
            Version = version;
        }
    }


    public sealed class ServiceSensorOptions : EnumSensorOptions
    {
        public string ServiceName { get; set; }

        public bool IsHostService { get; set; } = true;

        public string SensorPath { get; set; }

        public ServiceSensorOptions() { }

        public ServiceSensorOptions(string serviceName) : base()
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