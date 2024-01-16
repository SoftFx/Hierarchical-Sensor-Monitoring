using System;
using System.Collections.Generic;
using HSMDataCollector.Alerts;
using HSMDataCollector.DefaultSensors.Windows;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes.Collections.Disks
{
    internal class WindowsAverageDiskWriteSpeedPrototype : BarDisksMonitoringPrototype
    {
        protected override string DescriptionPath => WindowsAverageDiskWriteSpeed.Counter;

        protected override string SensorNameTemplate => "Average disk write speed on {0} disk";


        public WindowsAverageDiskWriteSpeedPrototype() : base()
        {
            SensorUnit = Unit.bytes;

            // Alerts = new List<BarAlertTemplate>()
            // {
            //     AlertsFactory.IfEmaMean(AlertOperation.GreaterThanOrEqual, 80)
            //         .AndConfirmationPeriod(TimeSpan.FromMinutes(5))
            //         .ThenSendNotification($"[$product]$path $property $operation $target$unit")
            //         .AndSetIcon(AlertIcon.Warning).Build()
            // };
        }
    }
}