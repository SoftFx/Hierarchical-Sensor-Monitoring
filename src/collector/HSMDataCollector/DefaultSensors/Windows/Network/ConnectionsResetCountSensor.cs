using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public class ConnectionsResetCountSensor : SocketsSensor
    {
        protected override string CounterName => "Connections Reset";
        
        
        public ConnectionsResetCountSensor(MonitoringInstantSensorOptions options) : base(options) {}
    }
}
