using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductInfoSensor : SensorBase<string>
    {
        private readonly string _version;
        private readonly DateTime _startTime;

        protected override string SensorName => "Version";


        public ProductInfoSensor(ProductInfoOptions options) : base(options)
        {
            _version = options.Version;
            _startTime = options.StartTime;
        }


        internal void SendVersion() => SendValue($"Version: {_version}", comment: $"Start: {_startTime.ToString(DefaultTimeFormat)}");
    }
}