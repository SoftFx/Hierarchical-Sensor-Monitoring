using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductVersionSensor : SensorBase<string>
    {
        private readonly string _version;
        private readonly DateTime _startTime;

        protected override string SensorName => "Version";


        public ProductVersionSensor(ProductVersionOptions options) : base(options)
        {
            _version = options.Version;
            _startTime = options.StartTime;
        }


        internal void StartInfo() => SendValue(_version, comment: $"Start: {_startTime.ToString(DefaultTimeFormat)}");

        internal void StopInfo() => SendValue(_version, comment: $"Stop: {DateTime.UtcNow.ToString(DefaultTimeFormat)}");
    }
}