using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class ConnectionFailuresCountSensor : ConnectionsDifferenceSensor
    {
        protected override string CounterName => "Connection Failures";


        internal ConnectionFailuresCountSensor(MonitoringInstantSensorOptions options) : base(options) {}
    }
}