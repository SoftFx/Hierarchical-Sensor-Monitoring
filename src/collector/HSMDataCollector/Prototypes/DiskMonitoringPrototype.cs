using System;
using System.Collections.Generic;
using System.IO;

namespace HSMDataCollector.Options
{
    internal sealed class DiskMonitoringPrototype : BarMonitoringPrototype<DiskSensorOptions>
    {
        protected override string NodePath { get; } = "Disk monitoring";


        internal IEnumerable<DiskSensorOptions> GetAllDisksOptions(DiskSensorOptions userOptions)
        {
            var diskOptions = GetAndFill(userOptions);

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                diskOptions.TargetPath = drive.Name;

                yield return diskOptions;
            }
        }
    }
}
