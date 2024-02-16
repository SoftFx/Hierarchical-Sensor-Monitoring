using System;

namespace HSMDataCollector.Prototypes.Collections.Network
{
    internal abstract class ConnectionsDifferencePrototype : NetworkCollectionPrototype
    {
        internal ConnectionsDifferencePrototype()
        {
            TTL = TimeSpan.MaxValue;
        }
    }
}