using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSMDataCollector.Prototypes
{
    internal abstract class DisksMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<DiskSensorOptions>
    {
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromMinutes(5);

        protected override string Category => "Disks monitoring";


        protected abstract string UnixSensorName { get; }


        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.CalibrationRequests = customOptions?.CalibrationRequests ?? DiskSensorOptions.DefaultCalibrationRequests;
            options.TargetPath = customOptions?.TargetPath ?? DiskSensorOptions.DefaultTargetPath;

            return options;
        }


        internal IEnumerable<DiskSensorOptions> GetAllDisksOptions(DiskSensorOptions userOptions)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                yield return GetWindowsOptions(userOptions, drive.Name);
            }
        }

        internal DiskSensorOptions GetWindowsOptions(DiskSensorOptions customOptions, string diskName = null)
        {
            var options = Get(customOptions);

            options.DiskInfo = new WindowsDiskInfo(diskName ?? options.TargetPath);
            options.Path = string.Format(options.Path, options.DiskInfo.Name);

            return options;
        }

        internal DiskSensorOptions GetUnixOptions(DiskSensorOptions customOptions)
        {
            var options = Get(customOptions);

            options.DiskInfo = new UnixDiskInfo();
            options.Path = DefaultPrototype.BuildDefaultPath(Category, SensorName);

            return options;
        }
    }


    internal sealed class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on {0} disk";

        protected override string UnixSensorName => "Free space on disk";


        public FreeSpaceOnDiskPrototype() : base()
        {
            Description = "Current available free space of some disk";

            Type = SensorType.DoubleSensor;
            SensorUnit = Unit.MB;
        }
    }


    internal sealed class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on {0} disk prediction";

        protected override string UnixSensorName => "Free space on disk prediction";


        public FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Description = "Estimated time until disk space runs out";

            Type = SensorType.TimeSpanSensor;
        }
    }
}