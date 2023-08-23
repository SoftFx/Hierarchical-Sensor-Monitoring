using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.Prototypes
{
    internal abstract class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        private const string CalibrationInfo = "After the start of the sensor, it's calibrated during {0} requests that post with OffTime status";


        public FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Type = SensorType.TimeSpanSensor;
        }


        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = $"{options.Description} {string.Format(CalibrationInfo, options.CalibrationRequests)}";

            //options.Alerts.Add(AlertsFactory.IfValue(AlertOperation.LessOrEqualThan, 5.GigobytesToMegabytes())
            //                                .ThenNotify($"[$product] {SensorName} is running out. Current free space is $value {options.SensorUnit}")
            //                                .AndSetSensorError().Build());

            return options;
        }
    }


    internal sealed class WindowsFreeSpaceOnDiskPredictionPrototype : FreeSpaceOnDiskPredictionPrototype
    {
        private string _sensorName = "Free space on {0} disk prediction";


        protected override string SensorName => _sensorName;

        protected override string OsDiskInfo => WindowsDescription;


        protected override DiskSensorOptions SetDiskInfo(DiskSensorOptions options)
        {
            options = SetWindowsOptions(options);

            _sensorName = string.Format(SensorName, options.DiskInfo.Name);

            return options;
        }
    }


    internal sealed class UnixFreeSpaceOnDiskPredictionPrototype : FreeSpaceOnDiskPredictionPrototype
    {
        protected override string SensorName => "Free space on disk";

        protected override string OsDiskInfo => UnixDescription;


        protected override DiskSensorOptions SetDiskInfo(DiskSensorOptions options) => SetUnixOptions(options);
    }
}
