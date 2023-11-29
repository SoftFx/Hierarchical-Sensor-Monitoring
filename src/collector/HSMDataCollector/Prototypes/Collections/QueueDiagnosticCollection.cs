using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Prototypes.Collections
{
    internal static class QueueDiagnosticCommon
    {
        internal const string DiagnosticQueueInfo = "Queue stats";
    }


    internal abstract class QueueDiagnosticInstantPrototype : InstantSensorOptionsPrototype<InstantSensorOptions>
    {
        protected override string Category => QueueDiagnosticCommon.DiagnosticQueueInfo;
    }


    internal abstract class QueueDiagnosticBarPrototype : BarSensorOptionsPrototype<BarSensorOptions>
    {
        protected override string Category => QueueDiagnosticCommon.DiagnosticQueueInfo;


        protected QueueDiagnosticBarPrototype() : base()
        {
            Type = SensorType.DoubleBarSensor;
            IsPrioritySensor = true;
        }
    }


    internal sealed class QueueOverflowPrototype : QueueDiagnosticBarPrototype
    {
        protected override string SensorName => "Queue overflow";


        public QueueOverflowPrototype() : base()
        {
            Type = SensorType.IntegerBarSensor;
            //unit count should be added
        }


        public QueueOverflowPrototype ApplyOptions(CollectorOptions options)
        {
            Description = $"The sensor sends the amount of data that was removed from the queue during the overflow process.  \n" +
            $"Collector max queue size = **{options.MaxQueueSize}**, collect period = **{options.PackageCollectPeriod.ToReadableView()}**.";

            return this;
        }
    }


    internal sealed class PackageValuesCountPrototype : QueueDiagnosticBarPrototype
    {
        protected override string SensorName => "Values count in package";


        public PackageValuesCountPrototype() : base()
        {
            Type = SensorType.IntegerBarSensor;
            //count unit count should be added
        }


        public PackageValuesCountPrototype ApplyOptions(CollectorOptions options)
        {
            Description = $"The sensor sends information about the number of values in each collected package. Package max values count = **{options.MaxValuesInPackage}**.";

            return this;
        }
    }


    internal sealed class PackageProcessTimePrototype : QueueDiagnosticBarPrototype
    {
        protected override string SensorName => "Package process time";


        public PackageProcessTimePrototype() : base()
        {
            SensorUnit = Unit.Seconds;
        }


        public PackageProcessTimePrototype ApplyOptions(CollectorOptions options)
        {
            Description = $"The sensor sends information about the package processing time. Package collect period = **{options.PackageCollectPeriod.ToReadableView()}**.";

            return this;
        }
    }


    internal sealed class PackageContentSizePrototype : QueueDiagnosticBarPrototype
    {
        protected override string SensorName => "Package content size";


        public PackageContentSizePrototype() : base()
        {
            Description = $"The sensor sends information about the package body size.";
            SensorUnit = Unit.MB;
        }
    }
}