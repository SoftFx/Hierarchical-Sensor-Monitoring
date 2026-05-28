namespace HSMDataCollector.Core
{
    /// <summary>
    /// Observer notified on each <see cref="DataCollector"/> lifecycle transition. This is the
    /// portable equivalent of the <c>ToStarting</c>/<c>ToRunning</c>/<c>ToStopping</c>/<c>ToStopped</c>
    /// events — prefer it for new code and for non-.NET ports, where C# events have no direct analog.
    ///
    /// Implementations are invoked under the collector's internal lifecycle lock (the same place the
    /// events fire), so handlers must not block or call back into Start/Stop/Dispose. Exceptions thrown
    /// by a handler are caught and logged; they do not affect other listeners or the transition itself.
    ///
    /// Only transitions that occur after the listener is registered are delivered — the current state
    /// is not replayed on registration.
    /// </summary>
    public interface ILifecycleListener
    {
        void OnStarting();

        void OnRunning();

        void OnStopping();

        void OnStopped();
    }
}
