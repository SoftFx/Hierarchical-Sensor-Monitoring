using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IDisposable
    {
        event Action<List<(StoreInfo, BaseValue)>> NewItemsEvent;

        void AddItem((StoreInfo, BaseValue) storeWithBase);
        void AddItems(List<(StoreInfo, BaseValue)> storesWithBases);
    }
}
