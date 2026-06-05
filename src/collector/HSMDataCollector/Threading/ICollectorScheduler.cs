using System;
using System.Threading.Tasks;


namespace HSMDataCollector.Threading
{
    /// <summary>
    /// Schedules periodic and one-shot actions on a shared background worker. Implementations
    /// must be thread-safe. Disposing the scheduler cancels its worker loop and prevents new
    /// scheduling; in-flight task callbacks may still finish on their threadpool threads.
    /// </summary>
    internal interface ICollectorScheduler : IDisposable
    {
        /// <exception cref="ObjectDisposedException">The scheduler has already been disposed.</exception>
        ScheduledTask Schedule(Action action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null);

        /// <exception cref="ObjectDisposedException">The scheduler has already been disposed.</exception>
        ScheduledTask Schedule(Func<Task> action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null);

        /// <summary>
        /// Removes a scheduled task if it is still queued. Implementations should no-op when the
        /// task is null, already removed, or the scheduler has already been disposed.
        /// </summary>
        void Remove(ScheduledTask task);
    }
}
