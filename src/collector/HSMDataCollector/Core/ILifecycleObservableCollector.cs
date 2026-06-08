namespace HSMDataCollector.Core
{
    /// <summary>
    /// Optional collector capability for portable lifecycle observers. Kept separate from
    /// <see cref="IDataCollector"/> so external implementations of the legacy interface remain
    /// source-compatible.
    /// </summary>
    public interface ILifecycleObservableCollector
    {
        /// <summary>
        /// Registers an observer notified on each lifecycle transition. This is the portable
        /// equivalent of the ToStarting/ToRunning/ToStopping/ToStopped events. Only transitions
        /// occurring after registration are delivered (the current state is not replayed).
        /// Returns this collector for chaining. A null listener is ignored.
        /// </summary>
        IDataCollector AddLifecycleListener(ILifecycleListener listener);
    }
}
