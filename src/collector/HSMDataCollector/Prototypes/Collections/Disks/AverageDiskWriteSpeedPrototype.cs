using HSMDataCollector.DefaultSensors.Windows;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes.Collections.Disks
{
    internal sealed class WindowsAverageDiskWriteSpeedPrototype : BarDisksMonitoringPrototype
    {
        protected override string DescriptionPath => WindowsAverageDiskWriteSpeed.Counter;

        protected override string SensorNameTemplate => "Average disk write speed on {0} disk";


        public WindowsAverageDiskWriteSpeedPrototype() : base()
        {
            SensorUnit = Unit.MB;
        }
    }
}