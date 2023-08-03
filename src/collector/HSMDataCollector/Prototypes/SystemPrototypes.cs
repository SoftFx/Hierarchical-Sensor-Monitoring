using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Prototypes
{
    internal sealed class FreeRamMemoryPrototype : BarMonitoringPrototype
    {
        protected override string SensorName => "Free RAM memory MB";


        internal FreeRamMemoryPrototype() : base()
        {
            Description = "Free memory, which is memory available to the operating system," +
            " is defined as free and cache pages. The remainder is active memory, which is memory " +
            "currently in use by the operating system.";

            Enables = SetEnables.ForGrafana;
            Units = SetUnits.SetMB;
        }
    }


    internal sealed class TotalCPUPrototype : BarMonitoringPrototype
    {
        protected override string SensorName => "Total CPU";


        internal TotalCPUPrototype() : base()
        {
            Description = "CPU usage indicates the total percentage of processing power" +
            " exhausted to process data and run various programs on a network device, " +
            "server, or computer at any given point.";

            Enables = SetEnables.ForGrafana;
            Units = SetUnits.SetPercents;
        }
    }


    internal sealed class ProcessCpuPrototype : BarMonitoringPrototype
    {
        protected override string SensorName => "Process CPU";


        internal ProcessCpuPrototype()
        {
            Description = "CPU usage percentage.";

            Enables = SetEnables.ForGrafana;
            Units = SetUnits.SetPercents;
        }
    }


    internal sealed class ProcessMemoryPrototype : BarMonitoringPrototype
    {
        protected override string SensorName => "Process memory MB";


        internal ProcessMemoryPrototype()
        {
            Description = "Current process working set";

            Enables = SetEnables.ForGrafana;
            Units = SetUnits.SetMB;
        }
    }


    internal sealed class ProcessThreadCountPrototype : BarMonitoringPrototype
    {
        protected override string SensorName => "Process thread count";


        internal ProcessThreadCountPrototype()
        {
            Description = "The amount of threads, associated with current process";

            Enables = SetEnables.ForGrafana;
        }
    }
}
