namespace HSMDataCollector.Options.DefaultOptions
{
    internal sealed class CollectorStatusPrototype : Prototype<CollectorInfoOptions>
    {
        protected override string NodePath { get; } = "Product Info";
    }
}