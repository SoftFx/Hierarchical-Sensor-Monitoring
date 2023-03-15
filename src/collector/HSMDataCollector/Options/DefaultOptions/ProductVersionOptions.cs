namespace HSMDataCollector.Options
{
    internal sealed class ProductVersionOptions : OptionsProperty<VersionSensorOptions>
    {
        protected override string NodePath { get; } = "Product Info/Version Updates";
    }
}