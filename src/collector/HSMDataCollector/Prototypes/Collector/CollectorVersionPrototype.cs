using System;
using System.Reflection;
namespace HSMDataCollector.Options.DefaultOptions
{
    internal sealed class CollectorVersionPrototype : Prototype<CollectorInfoOptions>
    {
        protected override string NodePath { get; } = CollectorInfoOptions.BaseCollectorPath;


        internal VersionSensorOptions ConvertToVersionOptions(CollectorInfoOptions options)
        {
            options = GetAndFill(options);

            var assembly = Assembly.GetExecutingAssembly()?.GetName();
            var version = assembly.Version;

            return new VersionSensorOptions
            {
                SensorName = "Collector version",
                Version = version,
                NodePath = options.NodePath,
                StartTime = DateTime.UtcNow,
            };
        }
    }
}