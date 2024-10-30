using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;


namespace HSMDataCollector.Prototypes
{
    internal abstract class ProductVersionInfoPrototype : InstantSensorOptionsPrototype<VersionSensorOptions>
    {
        public override VersionSensorOptions Get(VersionSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Type = SensorType.VersionSensor;
            options.StartTime = customOptions?.StartTime ?? DateTime.UtcNow;
            options.KeepHistory = TimeSpan.FromDays(365 * 5 + 1); // at least 1 leap year
            options.Version = customOptions?.Version;

            return options;
        }
    }


    internal sealed class CollectorVersionPrototype : ProductVersionInfoPrototype
    {
        protected override string SensorName => "Collector version";


        public CollectorVersionPrototype() : base()
        {
            Description = "This sensor sends the current [**Version**](https://learn.microsoft.com/en-us/dotnet/api/system.version?view=netframework-4.7.2) of DataCollector " +
            "and its start time in UTC format. All available versions of DataCollector can be found [**here**](https://www.nuget.org/packages/HSMDataCollector.HSMDataCollector).";
        }


        public override VersionSensorOptions Get(VersionSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Version = DataCollectorExtensions.Version;

            return options;
        }
    }


    internal sealed class ServiceCommandsPrototype : InstantSensorOptionsPrototype<ServiceSensorOptions>
    {
        protected override string SensorName => "Service commands";


        public ServiceCommandsPrototype() : base()
        {
            Description = "This is a special sensor that sends information about various critical commands (Start, Stop, Update, Restart, etc.) and information about the initiator.";
            Type = SensorType.StringSensor;
        }


        public override ServiceSensorOptions Get(ServiceSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            if (options.Alerts == null)
                options.Alerts = new List<InstantAlertTemplate>();

            options.Alerts.Add(AlertsFactory.IfReceivedNewValue().ThenSendNotification($"[$product] $value - $comment").Build());

            return options;
        }
    }


    internal sealed class ServiceStatusPrototype : EnumSensorOptionsPrototype<ServiceSensorOptions>
    {
        protected override string SensorName => "Service status";

        private string _category;
        protected override string Category => _category;

        public ServiceStatusPrototype() : base()
        {
            AggregateData = true;
        }


        public override ServiceSensorOptions Get(ServiceSensorOptions customOptions)
        {
            var options = DefaultPrototype.Merge(this, customOptions);

            options.IsHostService = customOptions.IsHostService;
            options.ServiceName = customOptions.ServiceName;

            if (options.IsHostService)
                options.Path = DefaultPrototype.RevealDefaultPath(options, Category, SensorName);
            else
                options.Path = DefaultPrototype.BuildPath(customOptions.SensorPath, SensorName);


            options.EnumOptions = new List<EnumOption>
            {
                new EnumOption((int)ServiceControllerStatus.ContinuePending, nameof(ServiceControllerStatus.ContinuePending), "The service continue is pending", 0xFFB403),
                new EnumOption((int)ServiceControllerStatus.Paused,          nameof(ServiceControllerStatus.Paused),          "The service is paused.",          0x0314FF),
                new EnumOption((int)ServiceControllerStatus.PausePending,    nameof(ServiceControllerStatus.PausePending),    "The service pause is pending.",   0x809EFF),
                new EnumOption((int)ServiceControllerStatus.Running,         nameof(ServiceControllerStatus.Running),         "The service is running.",         0x00FF00),
                new EnumOption((int)ServiceControllerStatus.StartPending,    nameof(ServiceControllerStatus.StartPending),    "The service start pending.",      0xBFFFBF),
                new EnumOption((int)ServiceControllerStatus.Stopped,         nameof(ServiceControllerStatus.Stopped),         "The service is stopped.",         0xFF0000),
                new EnumOption((int)ServiceControllerStatus.StopPending,     nameof(ServiceControllerStatus.StopPending),     "The service stop pending.",       0xFD6464)
            };

            options.Description = $"This sensor subscribes to the specified [**Windows service**](https://en.wikipedia.org/wiki/Windows_service) and sends status changes." +
                " Windows service has the following statuses: \n\n";

            options.Description += options.GenerateEnumOptionsDecription();

            options.Description += "\nMore information you can find [**here**](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontrollerstatus?view=dotnet-plat-ext-7.0)";

            return options;
        }
    }


    internal sealed class ProductVersionPrototype : ProductVersionInfoPrototype
    {
        protected override string SensorName => "Version";


        public ProductVersionPrototype() : base()
        {
            Description = "This sensor sends the current [**Version**](https://learn.microsoft.com/en-us/dotnet/api/system.version?view=netframework-4.7.2) " +
                "of connected application and its start time in UTC format.";
        }
    }


    internal sealed class ServiceAlivePrototype : MonitoringInstantSensorOptionsPrototype<MonitoringInstantSensorOptions>
    {
        private const string DescriptionTemplate = "This sensor sends DataCollector heartbits with a period of {0}. " +
            "If TTL triggered and HSM server stopped receiving data, you need to check the status of the connected application.";


        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromSeconds(15);

        protected override string SensorName => "Service alive";


        public ServiceAlivePrototype() : base()
        {
            Description = "Indicator that the monitored service is alive";
            Type = SensorType.BooleanSensor;
            AggregateData = true;

            TTL = TimeSpan.FromMinutes(1);
            KeepHistory = TimeSpan.FromDays(180);

            TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSendInstantHourlyScheduledNotification($"[$product]$path").AndSetIcon(AlertIcon.Clock).AndSetSensorError().Build();
        }


        public override MonitoringInstantSensorOptions Get(MonitoringInstantSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = string.Format(DescriptionTemplate, options.PostDataPeriod.ToReadableView());

            return options;
        }
    }


    internal sealed class CollectorErrorsPrototype : InstantSensorOptionsPrototype<InstantSensorOptions>
    {
        protected override string SensorName => "Collector errors";


        public CollectorErrorsPrototype() : base()
        {
            Description = "Indicator that the monitored errors that thrown in a DataCollector.";
            Type = SensorType.StringSensor;
            AggregateData = true;

            TTL = TimeSpan.MaxValue; //Never
        }
    }
}