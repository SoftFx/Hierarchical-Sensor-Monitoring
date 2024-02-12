using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal abstract class NetworkCollectionPrototype : MonitoringInstantSensorOptionsPrototype<NetworkSensorOptions>
    {
        protected override string Category => "Network";
        protected override TimeSpan DefaultPostDataPeriod { get; } = TimeSpan.FromMinutes(1);


        internal NetworkCollectionPrototype()
        {
            IsComputerSensor = true;
            Type = SensorType.DoubleSensor;
            
            TTL = TimeSpan.FromMinutes(5);
            KeepHistory = TimeSpan.FromDays(90);
        }
    }
}
