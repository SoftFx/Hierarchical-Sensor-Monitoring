using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IAsyncDisposable
    {
        string Name { get; }

        int QueueSize { get; }

        Stopwatch Stopwatch { get; }

        Task<TaskResult> ProcessRequestAsync(IUpdateRequest storeInfo, CancellationToken token = default);

        Task AddItemAsync(IUpdateRequest storeInfo, CancellationToken token = default);

        Task AddItemsAsync(IEnumerable<IUpdateRequest> storeInfos, CancellationToken token = default);
    }
}
