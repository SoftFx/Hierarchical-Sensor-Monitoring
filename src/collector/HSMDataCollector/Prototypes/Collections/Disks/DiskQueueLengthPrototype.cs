using HSMDataCollector.Alerts;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System.Collections.Generic;
using HSMDataCollector.Extensions;

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
            SensorUnit = Unit.Percents;

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
        
        public override DiskBarSensorOptions Get(DiskBarSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = string.Format(BaseDescription, SensorName, options.PostDataPeriod.ToReadableView(), options.BarPeriod.ToReadableView(), "LogicalDisk/Avg. Disk Queue Length");

            return options;
        }
    }
}