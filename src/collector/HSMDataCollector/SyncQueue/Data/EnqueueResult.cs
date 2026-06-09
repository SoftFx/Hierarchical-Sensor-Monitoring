namespace HSMDataCollector.SyncQueue.Data
{
    /// <summary>
    /// Outcome of an enqueue attempt. The two rejection statuses are deliberately distinct so
    /// internal tests can assert WHICH gate fired, but they are operationally equivalent at the
    /// producer/telemetry layer — <see cref="HSMDataCollector.Core.DataProcessor.HandleEnqueueResult"/>
    /// treats both as silent rejection.
    ///
    /// Producers SHOULD NOT branch on the distinction. The collector-lifecycle gate
    /// (<see cref="RejectedCollectorNotAcceptingData"/>) and the per-queue stop gate
    /// (<see cref="RejectedQueueStopped"/>) can both fire in response to a normal Stop or Dispose,
    /// and the producer's correct response in either case is "this value is lost; do not retry."
    /// Issue #1087 item B.
    /// </summary>
    internal enum EnqueueStatus : byte
    {
        /// <summary>The item entered the queue (or was the cause of overflow eviction — see
        /// <see cref="EnqueueResult.DroppedCount"/>).</summary>
        Accepted,

        /// <summary>The collector lifecycle (<c>CollectorLifecycle.CanAcceptData</c>) is closed —
        /// e.g. before <c>Start()</c> or after <c>Stop()</c>. The DataProcessor's lifecycle gate
        /// fired before reaching the queue.</summary>
        RejectedCollectorNotAcceptingData,

        /// <summary>The queue itself has stopped accepting public writes (its writes-accepting flag
        /// is 0). Distinguishes "queue closed for new work" from "channel write physically failed"
        /// for tests; producers see both as "value lost, do not retry."</summary>
        RejectedQueueStopped,
    }


    internal readonly struct EnqueueResult
    {
        internal EnqueueStatus Status { get; }

        internal int DroppedCount { get; }

        internal bool IsAccepted => Status == EnqueueStatus.Accepted;

        private EnqueueResult(EnqueueStatus status, int droppedCount)
        {
            Status = status;
            DroppedCount = droppedCount;
        }

        internal static EnqueueResult Accept(int droppedCount = 0) =>
            new EnqueueResult(EnqueueStatus.Accepted, droppedCount);

        internal static EnqueueResult RejectedNotAccepting() =>
            new EnqueueResult(EnqueueStatus.RejectedCollectorNotAcceptingData, 0);

        internal static EnqueueResult RejectedStopped(int droppedCount = 0) =>
            new EnqueueResult(EnqueueStatus.RejectedQueueStopped, droppedCount);
    }
}
