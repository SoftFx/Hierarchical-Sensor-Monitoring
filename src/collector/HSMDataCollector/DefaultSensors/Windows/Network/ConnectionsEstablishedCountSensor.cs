using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal sealed class ConnectionsEstablishedCountSensor : SocketsSensor
    {
        protected override string CounterName => "Connections Established";


        internal ConnectionsEstablishedCountSensor(MonitoringInstantSensorOptions options) : base(options) { }
    }
}