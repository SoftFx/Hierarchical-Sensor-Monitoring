using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.SyncQueue.Data;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    /// <summary>
    /// Non-generic surface used by <see cref="HSMDataCollector.Core.DataProcessor"/> to start, stop,
    /// flush, and inspect a queue without naming its item type. Concrete queues remain generic;
    /// only the lifecycle and diagnostic operations are exposed here.
    /// </summary>
    internal interface IQueueProcessor : IDisposable
    {
        string QueueName { get; }

        bool Start();

        ValueTask<bool> StopAsync(ShutdownMode mode);

        Task FlushAsync(CancellationToken token);

        int ClearQueue();
    }
}
