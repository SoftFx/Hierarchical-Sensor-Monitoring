using System;

namespace HSMDataCollector.Options
{
    internal sealed class ProductVersionPrototype : OptionsProperty<ProductVersionOptions>
    {
        protected override string NodePath { get; } = "Product Info";

        internal override ProductVersionOptions GetAndFill(ProductVersionOptions options)
        {
            base.GetAndFill(options);
            
            if (options.StartTime == default)
                options.StartTime = DateTime.UtcNow;
            
            return options;
        }
    }
}