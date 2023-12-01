using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Prototypes
{
    internal abstract class ProductVersionInfoPrototype : InstantSensorOptionsPrototype<VersionSensorOptions>
    {
        public override VersionSensorOptions Get(VersionSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Type = SensorType.VersionSensor;
            options.StartTime = customOptions?.StartTime ?? DateTime.UtcNow;

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


    internal sealed class ServiceStatusPrototype : InstantSensorOptionsPrototype<ServiceSensorOptions>
    {
        protected override string SensorName => "Service status";


        public ServiceStatusPrototype() : base()
        {
            Description = "This sensor subscribes to the specified [**Windows service**](https://en.wikipedia.org/wiki/Windows_service) and sends status changes. " +
                "Windows service has the following statuses: \n\n" +
                "* ContinuePending - The service continue is pending.  \n" +
                "* Paused - The service is paused. \n" +
                "* PausePending - The service pause is pending. \n" +
                "* Running - The service is running.\n" +
                "* StartPending - The service is starting.\n" +
                "* Stopped - The service is not running.\n" +
                "* StopPending - The service is stopping. \n\n" +
                "More information you can find [**here**](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontrollerstatus?view=dotnet-plat-ext-7.0)";

            Type = SensorType.IntSensor;
        }


        public override ServiceSensorOptions Get(ServiceSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.ServiceName = customOptions.ServiceName;

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

            TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSendNotification($"[$product]$path").AndSetIcon(AlertIcon.Clock).AndSetSensorError().Build();
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