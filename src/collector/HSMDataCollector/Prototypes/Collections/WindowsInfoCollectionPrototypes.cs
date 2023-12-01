using HSMDataCollector.Alerts;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Prototypes
{
    internal abstract class WindowsInfoMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<WindowsInfoSensorOptions>
    {
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromHours(12);

        protected override string Category => WindowsOsInfo;


        protected WindowsInfoMonitoringPrototype() : base()
        {
            IsComputerSensor = true;
        }


        public override WindowsInfoSensorOptions Get(WindowsInfoSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = $"{options.Description} Information is read from [**Windows Registry**](https://en.wikipedia.org/wiki/Windows_Registry)." +
            $" The system check is carried out every {options.PostDataPeriod.ToReadableView()}";

            return options;
        }
    }


    internal sealed class WindowsLastRestartPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last restart";


        public WindowsLastRestartPrototype() : base()
        {
            Type = SensorType.TimeSpanSensor;
        }

        public override WindowsInfoSensorOptions Get(WindowsInfoSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = $"This sensor sends information about the time of the last OS restart. " +
                $"The information is read using [**Performance counter**](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.performancecounter?view=netframework-4.7.2) by path " +
                $"*{WindowsLastRestart.CategoryName}/{WindowsLastRestart.CounterName}* " +
                $"The system check is carried out every {options.PostDataPeriod.ToReadableView()}";

            return options;
        }
    }


    internal sealed class WindowsLastUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last update";


        public WindowsLastUpdatePrototype() : base()
        {
            Description = "This sensor sends information about the time of the last OS update.";

            Type = SensorType.TimeSpanSensor;

            Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfValue(AlertOperation.GreaterThan, TimeSpan.FromDays(90))
                             .ThenSendNotification($"[$product] $sensor. Windows hasn't been updated for $value")
                             .AndSetSensorError().Build()
            };
        }
    }


    internal sealed class WindowsVersionPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Version & patch";


        public WindowsVersionPrototype() : base()
        {
            Description = "Current version of the OS in the format: *ProductName DisplayVersion (Major.Minor.Build)*.";

            Type = SensorType.StringSensor;
            AggregateData = true;
        }
    }
}