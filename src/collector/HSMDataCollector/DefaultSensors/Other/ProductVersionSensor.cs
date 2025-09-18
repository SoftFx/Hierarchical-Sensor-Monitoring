using System;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductVersionSensor : SensorBase<Version, NoDisplayUnit>
    {
        private readonly Version _version;
        private readonly DateTime _startTime;


        public ProductVersionSensor(VersionSensorOptions options) : base(options)
        {
            _version = options.Version;
            _startTime = options.StartTime.HasValue ? options.StartTime.Value.ToUniversalTime() : DateTime.UtcNow;
        }


        public override async ValueTask<bool> StartAsync()
        {
            var ok = await base.StartAsync().ConfigureAwait(false);

            if (ok)
                SendValue(_version, comment: $"Start: {_startTime.ToString(DefaultTimeFormat)}");

            return ok;
        }


        public override ValueTask StopAsync()
        {
            SendValue(_version, comment: $"Stop: {DateTime.UtcNow.ToString(DefaultTimeFormat)}");

            return base.StopAsync();
        }
    }
}