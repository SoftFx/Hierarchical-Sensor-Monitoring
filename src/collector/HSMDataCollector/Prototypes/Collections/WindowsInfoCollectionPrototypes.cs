﻿using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class WindowsInfoMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<WindowsInfoSensorOptions>
    {
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromHours(12);

        protected override string Category => "Windows OS info";


        public override WindowsInfoSensorOptions Get(WindowsInfoSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = $"{options.Description}. Information is read from [**Windows Registry**](https://en.wikipedia.org/wiki/Windows_Registry)." +
            $" The system check is carried out every {options.PostDataPeriod.ToReadableView()}";

            return options;
        }
    }


    internal sealed class WindowsIsNeedUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Is need update";


        public WindowsIsNeedUpdatePrototype() : base()
        {
            Description = "Gets true if the system has not been updated for a half a year";

            Type = SensorType.BooleanSensor;
        }
    }


    internal sealed class WindowsLastRestartPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last restart";


        public WindowsLastRestartPrototype() : base()
        {
            Description = "This sensor sends information about the time of the last OS restart.";

            Type = SensorType.TimeSpanSensor;
        }
    }


    internal sealed class WindowsLastUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last update";


        public WindowsLastUpdatePrototype() : base()
        {
            Description = "This sensor sends information about the time of the last OS update.";

            Type = SensorType.TimeSpanSensor;

            Alerts.Add(AlertsFactory.IfValue(AlertOperation.GreaterThan, TimeSpan.FromDays(90))
                                    .ThenSendNotification($"[$product] $sensor. Windows hasn't been updated for $value")
                                    .AndSetSensorError().Build());
        }
    }
}