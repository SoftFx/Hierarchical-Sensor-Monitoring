using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductVersionSensor : SensorBase<Version>
    {
        private readonly Version _version;
        private readonly DateTime _startTime;

        
        public ProductVersionSensor(VersionSensorOptions options) : base(options)
        {
            _version = options.Version;
            _startTime = options.StartTime;
        }


        internal void StartInfo() => SendValue(_version, comment: $"Start: {_startTime.ToString(DefaultTimeFormat)}");

        internal void StopInfo() => SendValue(_version, comment: $"Stop: {DateTime.UtcNow.ToString(DefaultTimeFormat)}");
    }
}