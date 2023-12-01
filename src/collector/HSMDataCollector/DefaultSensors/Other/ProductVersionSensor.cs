using HSMDataCollector.Options;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductVersionSensor : SensorBase<Version>
    {
        private readonly Version _version;
        private readonly DateTime _startTime;


        public ProductVersionSensor(VersionSensorOptions options) : base(options)
        {
            _version = options.Version;
            _startTime = options.StartTime.HasValue ? options.StartTime.Value.ToUniversalTime() : DateTime.UtcNow;
        }


        internal override async Task<bool> Start()
        {
            var ok = await base.Start();

            if (ok)
                SendValue(_version, comment: $"Start: {_startTime.ToString(DefaultTimeFormat)}");

            return ok;
        }


        internal override Task Stop()
        {
            SendValue(_version, comment: $"Stop: {DateTime.UtcNow.ToString(DefaultTimeFormat)}");

            return base.Stop();
        }
    }
}