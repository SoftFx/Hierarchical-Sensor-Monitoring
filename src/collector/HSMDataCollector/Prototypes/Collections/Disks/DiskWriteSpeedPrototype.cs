using HSMDataCollector.DefaultSensors.Windows;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes.Collections.Disks
{
    internal sealed class WindowsDiskWriteSpeedPrototype : BarDisksMonitoringPrototype
    {
        protected override string DescriptionPath => "Disk Write MB/sec";

        protected override string SensorNameTemplate => "Average disk write speed on {0} disk";


        public WindowsDiskWriteSpeedPrototype() : base()
        {
            SensorUnit = Unit.MB;
        }
    }
}