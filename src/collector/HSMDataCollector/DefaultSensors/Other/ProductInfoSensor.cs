using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductInfoSensor : SensorBase<string>
    {
        private readonly string _version;
        
        
        protected override string SensorName => "Product Version";

        public ProductInfoSensor(ProductInfoOptions options) : base(options)
        {
            _version = options.Version;
        }
        protected override string GetValue() => _version;
    }
}