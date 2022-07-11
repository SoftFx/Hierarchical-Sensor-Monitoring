using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IDisposable
    {
        event Action<List<StoreInfo>> NewItemsEvent;

        void AddItem(StoreInfo storeInfo);
        void AddItems(List<StoreInfo> storeInfos);
    }
}
