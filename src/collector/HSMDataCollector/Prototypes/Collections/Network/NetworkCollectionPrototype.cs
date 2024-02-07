using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal abstract class NetworkCollectionPrototype : MonitoringInstantSensorOptionsPrototype<NetworkSensorOptions>
    {
        protected override string Category => "Network";
        protected override TimeSpan DefaultPostDataPeriod => TimeSpan.FromMinutes(1);
    }
}
