using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionsResetCountPrototype : ConnectionsDifferencePrototype
    {
        protected override string SensorName => "Connections Reset Count";


        public override NetworkSensorOptions Get(NetworkSensorOptions customOptions)
        {
            customOptions = base.Get(customOptions);
            customOptions.Description = $"The number of connections reset in {customOptions.PostDataPeriod.ToReadableView()}." +
                                        " TCP counts a connection as having been reset when it goes directly from ESTABLISHED or CLOSE-WAIT to CLOSED." +
                                        " [More info](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc787094(v=ws.10)?redirectedfrom=MSDN)";
            
            return customOptions;
        }
    }
}