namespace HSMDataCollector.SyncQueue.Data
{
    /// <summary>
    /// Explicit shutdown intent for queue/processor StopAsync paths. Replaces ad hoc booleans
    /// such as <c>clearQueue</c>/<c>flushAcceptedWork</c>/<c>preserveCanceledPackages</c> that
    /// did not document who chose which behavior or why.
    /// </summary>
    internal enum ShutdownMode : byte
    {
        /// <summary>
        /// User-initiated <c>DataCollector.Stop()</c>: preserve accepted work, perform a bounded
        /// flush of queued items, and re-enqueue canceled in-flight packages so they survive the
        /// stop cycle.
        /// </summary>
        GracefulStop,

        /// <summary>
        /// Terminal <c>DataCollector.Dispose()</c>: cancel quickly under broken/nonresponsive
        /// transport. Do not preserve canceled in-flight packages forever; clear queue leftovers
        /// after a bounded flush so the process can exit.
        /// </summary>
        TerminalDispose,

        /// <summary>
        /// Cleanup after a failed <c>Start()</c> (or partial queue startup): drop queued items
        /// because the collector never finished entering the running phase. Behaves like a
        /// terminal stop but does not assume any user-visible data was accepted.
        /// </summary>
        StartRollback,
    }


    internal static class ShutdownModeExtensions
    {
        /// <summary>
        /// True when canceled in-flight packages should be re-enqueued so a follow-up flush can
        /// retry them. Only graceful stop preserves work; terminal dispose discards because the
        /// transport is being torn down and we should not loop forever, and start-rollback
        /// discards because the collector never finished entering the running phase.
        /// </summary>
        internal static bool PreserveCanceledPackages(this ShutdownMode mode) =>
            mode == ShutdownMode.GracefulStop;

        /// <summary>
        /// True when the queue should attempt a bounded flush of accepted items before the
        /// processor task is torn down. Graceful stop and terminal dispose both flush so that
        /// last-value sensor values produced by sensor stop/dispose make it out; only the
        /// start-rollback path skips the flush because no user data was ever accepted.
        /// </summary>
        internal static bool FlushAcceptedWork(this ShutdownMode mode) =>
            mode != ShutdownMode.StartRollback;

        /// <summary>
        /// True when StopAsync should discard remaining queue items immediately rather than
        /// hand them off to a later flush phase. Only start-rollback uses this — graceful stop
        /// and terminal dispose both clear AFTER their bounded flush, not inside StopAsync.
        /// </summary>
        internal static bool ClearOnStop(this ShutdownMode mode) =>
            mode == ShutdownMode.StartRollback;
    }
}
