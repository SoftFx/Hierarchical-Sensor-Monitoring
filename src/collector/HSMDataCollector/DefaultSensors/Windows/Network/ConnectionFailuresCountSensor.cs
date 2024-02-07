using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal sealed class ConnectionFailuresCountSensor : ConnectionsDifferenceSensor
    {
        internal protected override string CounterName => "Connection Failures";


        internal ConnectionFailuresCountSensor(MonitoringInstantSensorOptions options) : base(options) {}
    }
}