using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class ConnectionsEstablishedCountSensor : SocketsSensor
    {
        protected override string CounterName => "Connections Established";


        public ConnectionsEstablishedCountSensor(MonitoringInstantSensorOptions options) : base(options) { }
    }
}