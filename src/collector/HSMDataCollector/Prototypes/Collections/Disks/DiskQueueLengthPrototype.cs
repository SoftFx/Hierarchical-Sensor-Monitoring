using HSMDataCollector.Alerts;
using HSMSensorDataObjects;
using HSMDataCollector.DefaultSensors.Windows;

namespace HSMDataCollector.Prototypes.Collections.Disks
{
    internal sealed class WindowsDiskQueueLengthPrototype : BarDisksMonitoringPrototype
    {
        protected override string SensorNameTemplate => "Disk queue length on {0} disk";

        protected override string DescriptionPath => $"{WindowsDiskBarSensorBase.Category}/Avg. Disk Queue Length";


        public WindowsDiskQueueLengthPrototype() : base()
        {
            Type = SensorType.DoubleBarSensor;
        }
    }
}