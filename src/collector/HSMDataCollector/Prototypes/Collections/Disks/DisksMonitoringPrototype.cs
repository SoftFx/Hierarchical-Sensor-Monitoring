using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Collections.Generic;
using System.IO;
using HSMDataCollector.DefaultSensors.Windows;

namespace HSMDataCollector.Prototypes
{
    internal abstract class BarDisksMonitoringPrototype : BarSensorOptionsPrototype<DiskBarSensorOptions>
    {
        private const string BaseDescription = "The sensor sends information about {0} with a period of {1} and aggregated into bars of {2}. The information is read using " +
                                                "[**Performance counter**](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.performancecounter?view=netframework-4.7.2) by path *{3}*";


        private string _sensorName;


        protected abstract string DescriptionPath { get;}

        protected override string Category => DisksMonitoringPrototype.DiskCategory;

        protected override string SensorName => _sensorName;

        protected abstract string SensorNameTemplate { get; }


        protected BarDisksMonitoringPrototype() : base()
        {
            IsComputerSensor = true;
        }


        protected DiskBarSensorOptions SetDiskInfo(DiskBarSensorOptions options)
        {
            options.SetInfo(new WindowsDiskInfo(options.TargetPath));

            _sensorName = string.Format(SensorNameTemplate, options.DiskInfo.DiskLetter);

            return options;
        }

        public override DiskBarSensorOptions Get(DiskBarSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.TargetPath = customOptions?.TargetPath ?? DiskSensorOptions.DefaultTargetPath;

            options = SetDiskInfo(options);

            options.Description = string.Format(BaseDescription, SensorName, options.PostDataPeriod.ToReadableView(), options.BarPeriod.ToReadableView(), $"{WindowsDiskBarSensorBase.Category}/{DescriptionPath}");

            options.Path = RebuildPath();

            return options;
        }


        internal IEnumerable<DiskBarSensorOptions> GetAllDisksOptions(DiskBarSensorOptions userOptions)
        {
            var prototype = Get(userOptions);

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed)
                {
                    prototype.TargetPath = drive.Name;

                    yield return Get(prototype);
                }
            }
        }
    }


    internal abstract class DisksMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<DiskSensorOptions>
    {
        internal const string BaseDescription = "The sensor sends information about {0} with a period of {1}. The information is read using {2}.";
        internal const string DiskCategory = "Disks monitoring";

        protected const string WindowsDescription = "[**Disk info**](https://learn.microsoft.com/en-us/dotnet/api/system.io.driveinfo?view=netframework-4.7.2) class";
        protected const string UnixDescription = "[**df**](https://www.ibm.com/docs/en/aix/7.2?topic=d-df-command) command";


        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromMinutes(5);

        protected override string Category => DiskCategory;


        protected abstract string OsDiskInfo { get; }


        protected DisksMonitoringPrototype() : base()
        {
            IsComputerSensor = true;
        }


        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.CalibrationRequests = customOptions?.CalibrationRequests ?? DiskSensorOptions.DefaultCalibrationRequests;
            options.TargetPath = customOptions?.TargetPath ?? DiskSensorOptions.DefaultTargetPath;

            options = SetDiskInfo(options);

            options.Path = RebuildPath();
            options.Description = string.Format(BaseDescription, SensorName, options.PostDataPeriod.ToReadableView(), OsDiskInfo);

            return options;
        }


        internal IEnumerable<DiskSensorOptions> GetAllDisksOptions(DiskSensorOptions userOptions)
        {
            var prototype = Get(userOptions);

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed)
                {
                    prototype.TargetPath = drive.Name;

                    yield return Get(prototype);
                }
            }
        }


        protected abstract DiskSensorOptions SetDiskInfo(DiskSensorOptions options);

        protected DiskSensorOptions SetWindowsOptions(DiskSensorOptions options) => options.SetInfo(new WindowsDiskInfo(options.TargetPath));

        protected DiskSensorOptions SetUnixOptions(DiskSensorOptions options) => options.SetInfo(new UnixDiskInfo());
    }
}