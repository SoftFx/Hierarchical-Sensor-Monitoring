using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes
{
    internal sealed class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on disk";


        internal FreeSpaceOnDiskPrototype() : base()
        {
            Description = "Current available free space of some disk";

            SensorUnit = Unit.MB;
        }
    }


    internal sealed class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        protected override string SensorName => "Free space on disk prediction";


        internal FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Description = "Estimated time until disk space runs out";
        }
    }
}