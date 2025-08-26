using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IDisposable
    {
        event Action<IUpdateRequest> ItemAdded;

        int QueueSize { get; }

        Task<TaskResult> ProcessRequestAsync(IUpdateRequest storeInfo, CancellationToken token = default);

        Task AddItemAsync(IUpdateRequest storeInfo, CancellationToken token = default);

        Task AddItemsAsync(IEnumerable<IUpdateRequest> storeInfos, CancellationToken token = default);
    }
}
