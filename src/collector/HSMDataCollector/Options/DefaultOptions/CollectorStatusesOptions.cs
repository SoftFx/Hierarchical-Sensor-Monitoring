namespace HSMDataCollector.Options.DefaultOptions
{
    internal sealed class CollectorStatusesOptions : OptionsProperty<CollectorInfoOptions>
    {
        protected override string NodePath { get; } = "Product Info";
    }
}