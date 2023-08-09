using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsMetainfo;
using System.Collections.Generic;
using System.IO;

namespace HSMDataCollector.Prototypes
{
    internal abstract class DisksMonitoringPrototype : BaseMonitoringPrototype<DiskMonitoringSensorMetainfo, DiskSensorOptions>
    {
        protected override string Category => "Disk monitoring";


        internal IEnumerable<DiskMonitoringSensorMetainfo> GetAllDisksOptions(DiskSensorOptions userOptions)
        {
            var diskOptions = Get(userOptions);

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                diskOptions.TargetPath = drive.Name;

                yield return diskOptions;
            }
        }

        protected override DiskMonitoringSensorMetainfo Apply(DiskMonitoringSensorMetainfo info, DiskSensorOptions options)
        {
            info.CalibrationRequests = options.CalibrationRequests;
            info.PostDataPeriod = options.PostDataPeriod;
            info.TargetPath = options.TargetPath;

            var diskInfo = new WindowsDiskInfo(options.TargetPath);

            info.Path = BuildPath(SystemPath, Category, $"{SensorName} {diskInfo.Name}");

            return info;
        }
    }
}