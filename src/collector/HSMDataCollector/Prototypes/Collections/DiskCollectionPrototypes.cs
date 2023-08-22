using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Extensions;
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
        private const string BaseDescription = "The sensor sends information about {0} with a period of {1}. Information about free disk space is read using {2}.";
        private const string WindowsDescription = "[**Disk info**](https://learn.microsoft.com/en-us/dotnet/api/system.io.driveinfo?view=netframework-4.7.2) class";
        private const string UnixDescription = "[**df**](https://www.ibm.com/docs/en/aix/7.2?topic=d-df-command) command";


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
                if (drive.DriveType == DriveType.Fixed)
                    yield return GetWindowsOptions(userOptions, drive.Name);
            }
        }

        internal DiskSensorOptions GetWindowsOptions(DiskSensorOptions customOptions, string diskName = null)
        {
            var options = Get(customOptions);

            options.DiskInfo = new WindowsDiskInfo(diskName ?? options.TargetPath);

            var fillName = string.Format(SensorName, options.DiskInfo.Name);

            options.Path = string.Format(options.Path, options.DiskInfo.Name);
            options.Description = BuildDescription(WindowsDescription, fillName, options);

            return options;
        }

        internal DiskSensorOptions GetUnixOptions(DiskSensorOptions customOptions)
        {
            var options = Get(customOptions);

            options.DiskInfo = new UnixDiskInfo();
            options.Path = DefaultPrototype.BuildDefaultPath(Category, UnixSensorName);
            options.Description = BuildDescription(UnixDescription, UnixSensorName, options);

            return options;
        }


        protected virtual string AddAdditionalInfo(DiskSensorOptions options) => options.Description;

        private string BuildDescription(string info, string name, DiskSensorOptions options)
        {
            options.Description = string.Format(BaseDescription, name, options.PostDataPeriod.ToReadableView(), info);

            return AddAdditionalInfo(options);
        }
    }


    internal sealed class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on {0} disk";

        protected override string UnixSensorName => "Free space on disk";


        public FreeSpaceOnDiskPrototype() : base()
        {
            Type = SensorType.DoubleSensor;
            SensorUnit = Unit.MB;
        }
    }


    internal sealed class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        private const string CalibrationInfo = "After the start of the sensor, it's calibrated during {0} requests that post with OffTime status";


        protected override string SensorName => "Free space on {0} disk prediction";

        protected override string UnixSensorName => "Free space on disk prediction";


        public FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Type = SensorType.TimeSpanSensor;
        }


        protected override string AddAdditionalInfo(DiskSensorOptions options)
        {
            options.Description = $"{options.Description} {string.Format(CalibrationInfo, options.CalibrationRequests)}";

            return base.AddAdditionalInfo(options);
        }
    }
}