using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Prototypes
{
    internal sealed class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on disk";


        internal FreeSpaceOnDiskPrototype() : base()
        {
            Description = "Current available free space of some disk";

            Enables = SetEnables.ForGrafana;
            Units = SetUnits.SetMB;
        }
    }


    internal sealed class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on disk prediction";


        internal FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Description = "Estimated time until disk space runs out";

            Enables = SetEnables.ForGrafana;
        }
    }
}