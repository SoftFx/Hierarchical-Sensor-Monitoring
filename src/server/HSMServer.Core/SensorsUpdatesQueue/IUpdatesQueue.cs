using HSMServer.Core.Model.Requests;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IDisposable
    {
        event Action<BaseRequestModel> ItemAdded;

        Task AddItemAsync(BaseRequestModel storeInfo, CancellationToken token = default);

        Task AddItemsAsync(IEnumerable<BaseRequestModel> storeInfos, CancellationToken token = default);
    }
}
