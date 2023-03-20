using System;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors.SensorBases;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductInfoSensor : SensorBase<string>
    {
        private readonly string _version;
        private readonly DateTime _startTime;
        
        protected override string SensorName => "Product Version";

        
        public ProductInfoSensor(ProductInfoOptions options) : base(options)
        {
            _version = options.Version;
            _startTime = options.StartTime;
        }
        protected override string GetValue() => $"Version: {_version}, Start: {_startTime}";
        
        internal override Task<bool> Start()
        {
            SendValue(GetValue());
            return base.Start();
        }

    }
}