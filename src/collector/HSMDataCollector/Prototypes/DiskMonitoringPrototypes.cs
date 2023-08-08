using HSMDataCollector.Prototypes;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSMDataCollector.Options
{
    internal sealed class DiskMonitoringPrototype : <DiskSensorOptions>
    {
        protected override string NodePath { get; } = "Disk monitoring";


       
    }

    internal abstract class DisksMonitoringPrototype : BarBaseMonitoringPrototype<>
    {
        protected override string Category => "Disk monitoring";

        internal IEnumerable<DiskSensorOptions> GetAllDisksOptions(DiskSensorOptions userOptions)
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
    }
}
