using System;
using System.Collections.Generic;
using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Prototypes
{
    internal abstract class WindowsInfoMonitoringPrototype : MonitoringInstantSensorOptionsPrototype<WindowsInfoSensorOptions>
    {
        protected const string _wmiUrl = "https://learn.microsoft.com/windows/win32/wmisdk/wmi-start-page";

        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromHours(12);

        protected override string Category => WindowsOsInfo;


        protected WindowsInfoMonitoringPrototype() : base()
        {
            IsComputerSensor = true;
        }


        public override WindowsInfoSensorOptions Get(WindowsInfoSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = $"{options.Description} The system check is carried out every {options.PostDataPeriod.ToReadableView()}";

            return options;
        }
    }


    internal sealed class WindowsLastRestartPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last restart";


        public WindowsLastRestartPrototype() : base()
        {
            Description = $"This sensor sends information about the time of the last OS restart." +
                          $" The information is read using the [**WMI**]({_wmiUrl}) class 'Win32_OperatingSystem'.";

            Type = SensorType.TimeSpanSensor;
        }
    }

    internal sealed class WindowsInstallDatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Install date";


        public WindowsInstallDatePrototype() : base()
        {
            Description = $"This sensor sends information about the time of the OS install date." +
                          $" The information is read using the [**WMI**]({_wmiUrl}) class 'Win32_OperatingSystem'.";

            Type = SensorType.TimeSpanSensor;

            Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfValue(AlertOperation.GreaterThan, TimeSpan.FromDays(365*4))
                             .ThenSendNotification($"[$product] $sensor. Windows was installed more than $value ago")
                             .AndSetIcon(AlertIcon.Warning).Build()
            };
        }
    }


    internal sealed class WindowsLastUpdatePrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Last update";


        public WindowsLastUpdatePrototype() : base()
        {
            Description = "This sensor sends information about the time of the last OS update." +
                $" The information is read using the [**WMI**]({_wmiUrl}) class 'Win32_QuickFixEngineering'.";

            Type = SensorType.TimeSpanSensor;

            Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfValue(AlertOperation.GreaterThan, TimeSpan.FromDays(90))
                             .ThenSendNotification($"[$product] $sensor. Windows hasn't been updated for $value")
                             .AndSetIcon(AlertIcon.Warning).Build()
            };
        }
    }


    internal sealed class WindowsVersionPrototype : WindowsInfoMonitoringPrototype
    {
        protected override string SensorName => "Version & patch";


        public WindowsVersionPrototype() : base()
        {
            Description = "Current version of the OS in the format: *ProductName DisplayVersion (Major.Minor.Build)*. " +
                "Information is read from [**Windows Registry**](https://en.wikipedia.org/wiki/Windows_Registry).";

            Type = SensorType.VersionSensor;
            AggregateData = true;
        }
    }
}