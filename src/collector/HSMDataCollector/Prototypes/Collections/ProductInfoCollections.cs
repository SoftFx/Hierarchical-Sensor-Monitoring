using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class ProductInfoPrototype : InstantSensorOptionsPrototype<ServiceSensorOptions>
    {
        internal const string ProductInfoCategory = "ProductInfo";

        protected override string Category => ProductInfoCategory;
    }


    internal abstract class ProductVersionInfoPrototype : InstantSensorOptionsPrototype<VersionSensorOptions>
    {
        protected override string Category => ProductInfoPrototype.ProductInfoCategory;


        public override VersionSensorOptions Get(VersionSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.StartTime = DateTime.UtcNow;
            options.SensorName = SensorName;

            return options;
        }
    }


    internal sealed class CollectorVersionPrototype : ProductVersionInfoPrototype
    {
        protected override string SensorName => "Collector version";


        internal CollectorVersionPrototype() : base()
        {
            Description = "Current DataCollector version after calling Start method";
        }


        public override VersionSensorOptions Get(VersionSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Version = DataCollectorExtensions.Version;

            return options;
        }
    }


    internal sealed class ServiceCommandsPrototype : ProductInfoPrototype
    {
        protected override string SensorName => "Service commands";


        internal ServiceCommandsPrototype() : base()
        {
            Description = "Service Commands";
        }
    }


    internal sealed class ServiceStatusPrototype : ProductInfoPrototype
    {
        protected override string SensorName => "Service status";


        internal ServiceStatusPrototype() : base()
        {
            Description = "Current status of the connected product";
        }
    }


    internal sealed class ProductVersionPrototype : ProductVersionInfoPrototype
    {
        protected override string SensorName => "Version";


        internal ProductVersionPrototype() : base()
        {
            Description = "Current connected product version after calling Start method";
        }
    }


    internal sealed class ServiceAlivePrototype : MonitoringInstantSensorOptionsPrototype<MonitoringInstantSensorOptions>
    {
        protected override string Category => ProductInfoPrototype.ProductInfoCategory;

        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromSeconds(15);

        protected override string SensorName => "Service alive";



        internal ServiceAlivePrototype() : base()
        {
            Description = "Indicator that the monitored service is alive";
        }
    }
}