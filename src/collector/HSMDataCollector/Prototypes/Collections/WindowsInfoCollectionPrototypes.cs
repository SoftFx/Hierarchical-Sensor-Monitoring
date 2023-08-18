using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class WindowsInfoMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<WindowsInfoSensorOptions>
    {
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromHours(12);

        protected override string Category => "Windows OS info";
    }


    internal sealed class WindowsIsNeedUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Is need update";


        internal WindowsIsNeedUpdatePrototype() : base()
        {
            Description = "Gets true if the system has not been updated for a half a year";
        }
    }


    internal sealed class WindowsLastRestartPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last restart";


        internal WindowsLastRestartPrototype() : base()
        {
            Description = "Time since last system restart";
        }
    }


    internal sealed class WindowsUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last update";


        internal WindowsUpdatePrototype() : base()
        {
            Description = "Time since last system update";
        }
    }
}