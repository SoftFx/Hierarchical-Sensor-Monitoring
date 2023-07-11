using HSMDataCollector.Extensions;
using System;
namespace HSMDataCollector.Options.DefaultOptions
{
    internal sealed class CollectorVersionPrototype : Prototype<CollectorInfoOptions>
    {
        protected override string NodePath { get; } = CollectorInfoOptions.BaseCollectorPath;


        internal VersionSensorOptions ConvertToVersionOptions(CollectorInfoOptions options)
        {
            options = GetAndFill(options);

            return new VersionSensorOptions
            {
                SensorName = "Collector version",
                Version = DataCollectorExtensions.Version,
                NodePath = options.NodePath,
                StartTime = DateTime.UtcNow,
            };
        }
    }
}