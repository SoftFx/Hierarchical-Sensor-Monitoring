namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionFailuresCountPrototype : NetworkCollectionPrototype
    {
        protected override string SensorName => "Connection Failures Count";


        public ConnectionFailuresCountPrototype() : base()
        {
            Description = "The number of connections that have failed." +
                          " TCP counts a connection as having failed when it goes directly from sending (SYN-SENT) or receiving (SYN-RCVD) to CLOSED, or from receiving (SYN-RCVD) to listening (LISTEN).";
        }
    }
}