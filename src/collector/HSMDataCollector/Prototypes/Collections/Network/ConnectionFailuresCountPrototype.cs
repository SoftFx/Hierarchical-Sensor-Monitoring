using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionFailuresCountPrototype : NetworkCollectionPrototype
    {
        protected override string SensorName => "Connection Failures Count";
 

        public override NetworkSensorOptions Get(NetworkSensorOptions customOptions)
        {
            customOptions = base.Get(customOptions);
            customOptions.Description = $"The number of connections that have failed in {customOptions.PostDataPeriod.ToReadableView()}." +
                                        " TCP counts a connection as having failed when it goes directly from sending (SYN-SENT) or receiving (SYN-RCVD) to CLOSED, or from receiving (SYN-RCVD) to listening (LISTEN)." +
                                        " [More info](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc787094(v=ws.10)?redirectedfrom=MSDN)";
            
            return customOptions;
        }
    }
}