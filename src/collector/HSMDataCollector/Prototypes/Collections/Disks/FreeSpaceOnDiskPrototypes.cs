using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Prototypes
{
    internal abstract class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        public FreeSpaceOnDiskPrototype() : base()
        {
            Statistics = StatisticsOptions.EMA;
            Type = SensorType.DoubleSensor;
            SensorUnit = Unit.MB;
        }


        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfEmaValue(AlertOperation.LessThanOrEqual, 5.GigobytesToMegabytes())
                             .AndConfirmationPeriod(TimeSpan.FromMinutes(5))
                             .ThenSendNotification($"[$product] {SensorName} is running out. Current free space is $value {options.SensorUnit}")
                             .AndSetIcon(AlertIcon.ArrowDown).AndSetSensorError().Build()
            };

            return options;
        }
    }


    internal sealed class WindowsFreeSpaceOnDiskPrototype : FreeSpaceOnDiskPrototype
    {
        private const string SensorNameTemplate = "Free space on {0} disk";

        private string _sensorName;


        protected override string SensorName => _sensorName;

        protected override string OsDiskInfo => WindowsDescription;


        protected override DiskSensorOptions SetDiskInfo(DiskSensorOptions options)
        {
            options = SetWindowsOptions(options);

            _sensorName = string.Format(SensorNameTemplate, options.DiskInfo.DiskLetter);

            return options;
        }
    }


    internal sealed class UnixFreeSpaceOnDiskPrototype : FreeSpaceOnDiskPrototype
    {
        protected override string SensorName => "Free space on disk";

        protected override string OsDiskInfo => UnixDescription;


        protected override DiskSensorOptions SetDiskInfo(DiskSensorOptions options) => SetUnixOptions(options);
    }
}
