using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes
{
    internal abstract class SystemMonitoringPrototype : BarSensorOptionsPrototype<BarSensorOptions>
    {
        protected override string Category => string.Empty;
    }


    internal sealed class FreeRamMemoryPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Free RAM memory MB";


        internal FreeRamMemoryPrototype() : base()
        {
            Description = "Free memory, which is memory available to the operating system," +
            " is defined as free and cache pages. The remainder is active memory, which is memory " +
            "currently in use by the operating system.";

            SensorUnit = Unit.MB;
        }
    }


    internal sealed class TotalCPUPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Total CPU";


        internal TotalCPUPrototype() : base()
        {
            Description = "CPU usage indicates the total percentage of processing power" +
            " exhausted to process data and run various programs on a network device, " +
            "server, or computer at any given point.";

            SensorUnit = Unit.Percents;
        }
    }


    internal sealed class ProcessCpuPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Process CPU";


        internal ProcessCpuPrototype() : base()
        {
            Description = "CPU usage percentage.";

            SensorUnit = Unit.Percents;
        }
    }


    internal sealed class ProcessMemoryPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Process memory MB";


        internal ProcessMemoryPrototype() : base()
        {
            Description = "Current process working set";

            SensorUnit = Unit.MB;
        }
    }


    internal sealed class ProcessThreadCountPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Process thread count";


        internal ProcessThreadCountPrototype() : base()
        {
            Description = "The amount of threads, associated with current process";
        }
    }
}