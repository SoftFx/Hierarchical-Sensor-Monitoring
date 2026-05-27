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
        ScheduledTask Schedule(Action action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null);

        ScheduledTask Schedule(Func<Task> action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null);

        void Remove(ScheduledTask task);
    }
}
