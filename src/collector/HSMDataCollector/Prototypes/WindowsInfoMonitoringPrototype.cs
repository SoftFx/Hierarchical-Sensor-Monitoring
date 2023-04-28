using System;

namespace HSMDataCollector.Options
{
    internal sealed class WindowsInfoMonitoringOptions : Prototype<WindowsSensorOptions>
    {
        protected override string NodePath { get; } = "Windows OS Info";


        internal WindowsInfoMonitoringOptions() : base()
        {
            DefaultOptions.PostDataPeriod = TimeSpan.FromHours(12);
        }
    }
}
