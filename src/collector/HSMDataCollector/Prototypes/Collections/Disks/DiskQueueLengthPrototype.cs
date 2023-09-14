using HSMDataCollector.Alerts;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System.Collections.Generic;

namespace HSMDataCollector.Prototypes.Collections.Disks
{
    internal sealed class WindowsDiskQueueLengthPrototype : BarDisksMonitoringPrototype
    {
        private const string SensorNameTemplate = "Disk queue length on {0} disk";

        private string _sensorName;

        protected override string SensorName => _sensorName;


        public WindowsDiskQueueLengthPrototype() : base()
        {
            Type = SensorType.DoubleBarSensor;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfMean(AlertOperation.GreaterThanOrEqual, 80)
                             .ThenSendNotification("[$product]$path $property $operation $target%")
                             .AndSetIcon(AlertIcon.Warning).Build()
            };
        }


        protected override DiskBarSensorOptions SetDiskInfo(DiskBarSensorOptions options)
        {
            options.SetInfo(new WindowsDiskInfo(options.TargetPath));

            _sensorName = string.Format(SensorNameTemplate, options.DiskInfo.DiskLetter);

            return options;
        }
    }
}