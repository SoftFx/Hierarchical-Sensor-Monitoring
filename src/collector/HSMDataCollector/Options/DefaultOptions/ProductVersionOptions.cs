using System;

namespace HSMDataCollector.Options
{
    internal sealed class ProductVersionOptions : OptionsProperty<ProductInfoOptions>
    {
        protected override string NodePath { get; } = "Product Info";

        internal override ProductInfoOptions GetAndFill(ProductInfoOptions options)
        {
            base.GetAndFill(options);
            
            if (options.StartTime == default)
                options.StartTime = DateTime.UtcNow;
            
            return options;
        }
    }
}