namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal sealed class ConnectionsEstablishedCountPrototype : NetworkCollectionPrototype
    {
        protected override string SensorName => "Connections Established Count";


        public ConnectionsEstablishedCountPrototype() : base()
        {
            Description = "The number of simultaneous connections supported by TCP." +
                          " This counter displays the number of connections last observed to be in the ESTABLISHED or CLOSE-WAIT state." +
                          " This counter displays the last observed value (indicating the current state).";
        }
    }
}