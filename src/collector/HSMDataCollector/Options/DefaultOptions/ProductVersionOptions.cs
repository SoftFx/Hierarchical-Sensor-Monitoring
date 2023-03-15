using System;

namespace HSMDataCollector.Options
{
    internal sealed class ProductVersionOptions : OptionsProperty<VersionSensorOptions>
    {
        protected override string NodePath { get; } = "Product Info/Version Updates";

        internal override VersionSensorOptions GetAndFill(VersionSensorOptions options)
        {
            if (options.StartTime == default)
                options.StartTime = DateTime.UtcNow;
            
            return base.GetAndFill(options);
        }
    }
}