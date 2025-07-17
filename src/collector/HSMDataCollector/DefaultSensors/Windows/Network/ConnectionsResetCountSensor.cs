using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class ConnectionsResetCountSensor : ConnectionsDifferenceSensor
    {
        protected override string CounterName => "Connections Reset";
        
        
        internal ConnectionsResetCountSensor(MonitoringInstantSensorOptions options) : base(options) {}
    }
}
