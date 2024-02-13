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

            Type = SensorType.IntSensor;

            KeepHistory = TimeSpan.FromDays(90);
            TTL = TimeSpan.FromMinutes(5);
        }
    }
}
