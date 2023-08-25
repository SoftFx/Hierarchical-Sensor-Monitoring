using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes
{
    internal abstract class FreeSpaceOnDiskPrototype : DisksMonitoringPrototype
    {
        public FreeSpaceOnDiskPrototype() : base()
        {
            Type = SensorType.DoubleSensor;
            SensorUnit = Unit.MB;
        }


        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Alerts.Add(AlertsFactory.IfValue(AlertOperation.LessThanOrEqual, 5.GigobytesToMegabytes())
                                            .ThenSendNotification($"[$product] {SensorName} is running out. Current free space is $value {options.SensorUnit}")
                                            .AndSetIcon(AlertIcon.ArrowDown).AndSetSensorError().Build());

            return options;
        }
    }


    internal sealed class WindowsFreeSpaceOnDiskPrototype : FreeSpaceOnDiskPrototype
    {
        private string _sensorName = "Free space on {0} disk";


        protected override string SensorName => _sensorName;

        protected override string OsDiskInfo => WindowsDescription;


        protected override DiskSensorOptions SetDiskInfo(DiskSensorOptions options)
        {
            options = SetWindowsOptions(options);

            _sensorName = string.Format(SensorName, options.DiskInfo.DiskLetter);

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
