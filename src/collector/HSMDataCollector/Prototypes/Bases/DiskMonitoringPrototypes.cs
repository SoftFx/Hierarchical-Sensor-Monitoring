using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSMDataCollector.Prototypes
{
    internal abstract class DisksMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<DiskSensorOptions>
    {
        private const int DefaultCalibrationRequests = 6;
        private const string DefaultTargetPath = @"C:\";


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

            options.CalibrationRequests = customOptions?.CalibrationRequests ?? DefaultCalibrationRequests;
            options.TargetPath = customOptions?.TargetPath ?? DefaultTargetPath;

            var diskInfo = new WindowsDiskInfo(options.TargetPath);

            options.Path = $"{options.Path} {diskInfo.Name}";

            return options;
        }
    }
}