namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionsResetCountPrototype : NetworkCollectionPrototype
    {
        protected override string SensorName => "Connections Reset Count";


        public ConnectionsResetCountPrototype() : base()
        {
            Description = "The number of connections reset." +
                          "TCP counts a connection as having been reset when it goes directly from ESTABLISHED or CLOSE-WAIT to CLOSED.";
        }
    }
}