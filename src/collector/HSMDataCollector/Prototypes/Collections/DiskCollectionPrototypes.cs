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


        internal IEnumerable<DiskSensorOptions> GetAllDisksOptions(DiskSensorOptions userOptions)
        {
            var diskOptions = Get(userOptions);

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                diskOptions.TargetPath = drive.Name; // TODO: bug with sensor names should be checked

                yield return diskOptions;
            }
        }

        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.CalibrationRequests = customOptions?.CalibrationRequests ?? DiskSensorOptions.DefaultCalibrationRequests;
            options.TargetPath = customOptions?.TargetPath ?? DiskSensorOptions.DefaultTargetPath;

            var diskInfo = new WindowsDiskInfo(options.TargetPath);

            //options.Path = $"{options.Path} {diskInfo.Name}";

            return options;
        }
    }


    internal sealed class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on disk";


        public FreeSpaceOnDiskPrototype() : base()
        {
            Description = "Current available free space of some disk";

            Type = SensorType.DoubleSensor;
            SensorUnit = Unit.MB;
        }
    }


    internal sealed class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on disk prediction";


        public FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Description = "Estimated time until disk space runs out";

            Type = SensorType.TimeSpanSensor;
        }
    }
}