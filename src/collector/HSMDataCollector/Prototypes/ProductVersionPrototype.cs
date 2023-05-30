using System;

namespace HSMDataCollector.Options
{
    internal class ProductVersionPrototype : Prototype<VersionSensorOptions>
    {
        protected override string NodePath { get; } = "Product Info";

        internal override VersionSensorOptions GetAndFill(VersionSensorOptions options)
        {
            base.GetAndFill(options);

            if (string.IsNullOrEmpty(options.SensorName))
                options.SensorName = "Version";

            if (options.StartTime == default)
                options.StartTime = DateTime.UtcNow;
            
            return options;
        }
    }
}