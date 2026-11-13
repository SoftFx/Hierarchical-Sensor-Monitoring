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

        /// <summary>
        /// The stop drain of this queue: sends what it holds, one package after another, until
        /// the queue is empty, <paramref name="token"/> (the drain budget) fires or a send fails.
        /// What it leaves in the queue is the caller's to clear and count.
        /// </summary>
        Task<FlushResult> FlushAsync(CancellationToken token);

        int ClearQueue();
    }


    /// <summary>How the stop drain of one queue ended.</summary>
    internal readonly struct FlushResult
    {
        /// <summary>
        /// A send failed (#1747): the sender returned an error or threw. The drain of a stop
        /// ends at the first failed send, as in the native collector, so the value queues
        /// behind this one send nothing. False when the queue was emptied or the budget fired.
        /// </summary>
        internal bool SendFailed { get; }

        internal FlushResult(bool sendFailed)
        {
            SendFailed = sendFailed;
        }
    }
}
