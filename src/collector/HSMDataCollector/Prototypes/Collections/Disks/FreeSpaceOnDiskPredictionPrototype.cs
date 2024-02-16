using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class FreeSpaceOnDiskPredictionPrototype : DisksMonitoringPrototype
    {
        private const string CalibrationInfo = "After the start of the sensor, it's calibrated during {0} requests that post with OffTime status.";

        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromMinutes(5);


        public FreeSpaceOnDiskPredictionPrototype() : base()
        {
            Type = SensorType.TimeSpanSensor;
            
            //Alerts = new List<InstantAlertTemplate>()
            //{
            //    AlertsFactory.IfValue(AlertOperation.LessThanOrEqual, TimeSpan.FromDays(2))
            //                 .ThenSendNotification($"[$product] $sensor. Free disk space will run out in about $value")
            //                 .AndSetSensorError().Build(),
            //};
        }


        public override DiskSensorOptions Get(DiskSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = $"{options.Description} {string.Format(CalibrationInfo, options.CalibrationRequests)}";

            return options;
        }
    }


    internal sealed class WindowsFreeSpaceOnDiskPredictionPrototype : FreeSpaceOnDiskPredictionPrototype
    {
        private const string SensorNameTemplate = "Free space on {0} disk prediction";

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


    internal sealed class UnixFreeSpaceOnDiskPredictionPrototype : FreeSpaceOnDiskPredictionPrototype
    {
        protected override string SensorName => "Free space on disk prediction";

        protected override string OsDiskInfo => UnixDescription;


        protected override DiskSensorOptions SetDiskInfo(DiskSensorOptions options) => SetUnixOptions(options);
    }
}
