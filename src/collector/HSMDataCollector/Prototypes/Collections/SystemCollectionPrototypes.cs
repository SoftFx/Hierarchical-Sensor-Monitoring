using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System.Collections.Generic;

namespace HSMDataCollector.Prototypes
{
    internal abstract class SystemMonitoringPrototype : BarSensorOptionsPrototype<BarSensorOptions>
    {
        protected override string Category => string.Empty;
    }


    internal sealed class FreeRamMemoryPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Free RAM memory";


        public FreeRamMemoryPrototype() : base()
        {
            Description = "Free memory, which is memory available to the operating system," +
            " is defined as free and cache pages. The remainder is active memory, which is memory " +
            "currently in use by the operating system. More info can be found [**here**](https://en.wikipedia.org/wiki/Random-access_memory).";

            SensorUnit = Unit.MB;
            Type = SensorType.DoubleBarSensor;
        }
    }


    internal sealed class TotalCPUPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Total CPU";


        public TotalCPUPrototype() : base()
        {
            Description = "CPU usage indicates the total percentage of processing power" +
            " exhausted to process data and run various programs on a network device, " +
            "server, or computer at any given point. More info can be found [**here**](https://en.wikipedia.org/wiki/Central_processing_unit).";

            SensorUnit = Unit.Percents;
            Type = SensorType.DoubleBarSensor;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfMean(AlertOperation.GreaterThan, 50).ThenSendNotification("[$product]$path $property $operation $target%").AndSetIcon(AlertIcon.Warning).Build(),
            };
        }
    }


    internal sealed class ProcessCpuPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Process CPU";


        public ProcessCpuPrototype() : base()
        {
            Description = "CPU usage indicates the total percentage of processing power" +
            " exhausted to process data and run various programs on a network device, " +
            "server, or computer at any given point. More info can be found [**here**](https://en.wikipedia.org/wiki/Central_processing_unit).  \n" +
            "This sensor sends information about CPUs of the process in which it's running.";

            SensorUnit = Unit.Percents;
            Type = SensorType.DoubleBarSensor;
        }
    }


    internal sealed class ProcessMemoryPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Process memory";


        public ProcessMemoryPrototype() : base()
        {
            Description = "Free memory, which is memory available to the operating system," +
            " is defined as free and cache pages. The remainder is active memory, which is memory " +
            "currently in use by the operating system. More info can be found [**here**](https://en.wikipedia.org/wiki/Random-access_memory).  \n" +
            "This sensor sends information about RAM of the process in which it's running.";

            SensorUnit = Unit.MB;
            Type = SensorType.DoubleBarSensor;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfMean(AlertOperation.GreaterThan, 30.GigobytesToMegabytes()).ThenSendNotification($"[$product]$path $property $operation $target {Unit.MB}").AndSetIcon(AlertIcon.Warning).Build(),
            };
        }
    }


    internal sealed class ProcessThreadCountPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Process thread count";


        public ProcessThreadCountPrototype() : base()
        {
            Description = "A thread is the basic unit to which the operating system allocates processor time. A thread can execute any part of the process code, " +
            "including parts currently being executed by another thread.  \n" +
            "This sensor sends information about threads count of the process in which it's running. " +
            "More information about processes and threads you can find [**here**](https://learn.microsoft.com/en-us/windows/win32/procthread/processes-and-threads).";

            Type = SensorType.DoubleBarSensor;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfMean(AlertOperation.GreaterThan, 2000).ThenSendNotification("[$product]$path $property $operation $target").AndSetIcon(AlertIcon.Warning).Build(),
            };
        }
    }
}