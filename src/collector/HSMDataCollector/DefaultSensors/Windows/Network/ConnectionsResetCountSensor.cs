using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal sealed class ConnectionsResetCountSensor : ConnectionsDifferenceSensor
    {
        protected override string CounterName => "Connections Reset";
        
        
        internal ConnectionsResetCountSensor(MonitoringInstantSensorOptions options) : base(options) {}
    }
}
