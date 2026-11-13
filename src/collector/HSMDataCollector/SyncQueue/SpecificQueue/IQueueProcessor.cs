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

        /// <summary>
        /// The items of a send the drain gave up (#1748): the budget fired and the sender did
        /// not come back. They are in no queue any more, so the clear cannot count them; the
        /// caller adds them to the discarded items of this queue.
        /// </summary>
        internal int Abandoned { get; }

        internal FlushResult(bool sendFailed, int abandoned = 0)
        {
            SendFailed = sendFailed;
            Abandoned = abandoned;
        }
    }


    /// <summary>
    /// One send of the stop drain, and who settles the package it carries (#1748).
    ///
    /// Either the send comes back and its dispatch settles the package (delivered, or back
    /// into the queue for the clear to count), or the sender does not come back after the
    /// budget cancelled it and the drain gives the send up, counting its items as discarded.
    /// When the budget fires the two race. INVARIANT: exactly one of them gets the package.
    /// Both go through one compare-and-swap on the state, so a package is never counted as
    /// discarded and put back as well, and never neither.
    /// </summary>
    internal sealed class DrainSend
    {
        private const int InFlight = 0;
        private const int Returned = 1;
        private const int Abandoned = 2;

        private int _state;
        private int _items;

        /// <summary>How many items the send carries; set before the sender is called.</summary>
        internal int Items
        {
            get => Volatile.Read(ref _items);
            set => Volatile.Write(ref _items, value);
        }

        /// <summary>
        /// The send came back. True when its package is the dispatch's to settle; false when
        /// the drain has given the send up, and the package must not be touched any more.
        /// </summary>
        internal bool TryReturn() => Interlocked.CompareExchange(ref _state, Returned, InFlight) != Abandoned;

        /// <summary>
        /// The drain gives the send up. True when its package is now lost and the drain's to
        /// count; false when the send came back first and its dispatch is settling the package.
        /// </summary>
        internal bool TryAbandon() => Interlocked.CompareExchange(ref _state, Abandoned, InFlight) == InFlight;
    }
}
