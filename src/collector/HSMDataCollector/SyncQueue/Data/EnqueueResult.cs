namespace HSMDataCollector.SyncQueue.Data
{
    internal enum EnqueueStatus : byte
    {
        Accepted,
        RejectedCollectorNotAcceptingData,
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

        internal static EnqueueResult RejectedStopped() =>
            new EnqueueResult(EnqueueStatus.RejectedQueueStopped, 0);
    }
}
