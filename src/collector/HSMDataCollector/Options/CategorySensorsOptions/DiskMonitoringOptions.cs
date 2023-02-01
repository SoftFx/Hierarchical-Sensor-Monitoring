using System;

namespace HSMDataCollector.Options
{
    internal sealed class DiskMonitoringOptions : OptionsProperty<DiskSensorOptions>
    {
        protected override string NodePath { get; } = "Disk monitoring";


        internal DiskMonitoringOptions() : base()
        {
            DefaultOptions.PostDataPeriod = TimeSpan.FromMinutes(5);
        }
    }
}
