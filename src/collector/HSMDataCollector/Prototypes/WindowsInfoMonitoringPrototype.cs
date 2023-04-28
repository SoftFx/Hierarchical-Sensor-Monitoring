using System;

namespace HSMDataCollector.Options
{
    internal sealed class WindowsInfoMonitoringPrototype : Prototype<WindowsSensorOptions>
    {
        protected override string NodePath { get; } = "Windows OS Info";


        internal WindowsInfoMonitoringPrototype() : base()
        {
            DefaultOptions.PostDataPeriod = TimeSpan.FromHours(12);
        }
    }
}
