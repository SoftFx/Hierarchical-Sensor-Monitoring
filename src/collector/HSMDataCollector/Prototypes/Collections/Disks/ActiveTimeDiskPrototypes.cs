using HSMDataCollector.Alerts;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMDataCollector.DefaultSensors.Windows;

namespace HSMDataCollector.Prototypes.Collections.Disks
{
    internal sealed class WindowsActiveTimeDiskPrototype : BarDisksMonitoringPrototype
    {
        protected override string SensorNameTemplate => "Active time on {0} disk";

        protected override string DescriptionPath => $"{WindowsDiskBarSensorBase.Category}/% Disk Time";


        public WindowsActiveTimeDiskPrototype() : base()
        {
            Type = SensorType.DoubleBarSensor;
            SensorUnit = Unit.Percents;
        }
    }
}