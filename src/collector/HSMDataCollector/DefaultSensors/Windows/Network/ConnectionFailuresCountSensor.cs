using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class ConnectionFailuresCountSensor : SocketsSensor
    {
        protected override string CounterName => "Connection Failures";


        public ConnectionFailuresCountSensor(MonitoringInstantSensorOptions options) : base(options) {}
    }
}