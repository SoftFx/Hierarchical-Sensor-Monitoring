using System;
using System.Collections.Generic;
using System.IO;

namespace HSMDataCollector.Options
{
    internal sealed class DiskMonitoringPrototype : Prototype<DiskSensorOptions>
    {
        protected override string NodePath { get; } = "Disk monitoring";


        internal DiskMonitoringPrototype() : base()
        {
            DefaultOptions.PostDataPeriod = TimeSpan.FromMinutes(5);
        }


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
