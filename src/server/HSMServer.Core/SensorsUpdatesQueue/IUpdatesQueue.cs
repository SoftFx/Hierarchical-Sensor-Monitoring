using System;
using System.Collections.Generic;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IDisposable
    {
        event Action<IEnumerable<StoreInfo>> ItemsAdded;


        void AddItem(StoreInfo storeInfo);

        void AddItems(IEnumerable<StoreInfo> storeInfos);
    }
}
