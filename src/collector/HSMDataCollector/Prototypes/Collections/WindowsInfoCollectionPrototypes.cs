using HSMDataCollector.Options;
using HSMSensorDataObjects;
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


        public WindowsIsNeedUpdatePrototype() : base()
        {
            Description = "Gets true if the system has not been updated for a half a year";

            Type = SensorType.BooleanSensor;
        }
    }


    internal sealed class WindowsLastRestartPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last restart";


        public WindowsLastRestartPrototype() : base()
        {
            Description = "This sensor sends information about the time of the last OS restart. " +
                "Information is read from [**Windows Registry**](https://en.wikipedia.org/wiki/Windows_Registry).";

            Type = SensorType.TimeSpanSensor;
        }
    }


    internal sealed class WindowsLastUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last update";


        public WindowsLastUpdatePrototype() : base()
        {
            Description = "This sensor sends information about the time of the last OS update. " +
                "Information is read from [**Windows Registry**](https://en.wikipedia.org/wiki/Windows_Registry).";

            Type = SensorType.TimeSpanSensor;
        }
    }
}