using System;
using System.Globalization;
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
                SendValue(_version, comment: $"Start: {FormatMarkerTime(_startTime)}");

            return ok;
        }


        public override ValueTask StopAsync()
        {
            SendValue(_version, comment: $"Stop: {FormatMarkerTime(DateTime.UtcNow)}");

            return base.StopAsync();
        }


        // The marker comment is a wire-visible string, so it must NOT depend on the host's locale
        // (#1433). In a custom format string '/' and ':' are the date/time SEPARATOR placeholders,
        // replaced by the current culture's DateSeparator/TimeSeparator — on ru-RU the very same
        // format renders "22.09.2026 18:33:37". Formatting invariantly keeps every collector,
        // managed or native, emitting the identical comment on any machine.
        private static string FormatMarkerTime(DateTime utc) =>
            utc.ToString(DefaultTimeFormat, CultureInfo.InvariantCulture);
    }
}
