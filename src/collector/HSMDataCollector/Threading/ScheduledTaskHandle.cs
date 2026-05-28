using System;
using System.Threading.Tasks;


namespace HSMDataCollector.Threading
{
    /// <summary>
    /// Composable lifecycle wrapper around a single periodic <see cref="ScheduledTask"/>. Encapsulates
    /// the "schedule one action, start/stop/restart it, exactly once" boilerplate that monitoring and
    /// bar sensors previously hand-rolled with their own field + lock. Sensors now <i>compose</i> one of
    /// these per periodic action (send loop, bar-collect loop) instead of inheriting the timer plumbing.
    ///
    /// Start and StopAsync are individually thread-safe and idempotent. Callers should still
    /// serialize Start and StopAsync for the same handle when they need a deterministic final state.
    /// </summary>
    internal sealed class ScheduledTaskHandle
    {
        private readonly ICollectorScheduler _scheduler;
        private readonly object _lock = new object();

        private ScheduledTask _task;

        internal ScheduledTaskHandle(ICollectorScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        internal bool IsScheduled
        {
            get
            {
                lock (_lock)
                    return _task != null;
            }
        }

        /// <summary>
        /// Schedules <paramref name="action"/> if it is not already scheduled. Idempotent: a second
        /// call while running is a no-op (the existing schedule is kept).
        /// </summary>
        internal void Start(Action action, TimeSpan delay, TimeSpan period, Action<Exception> onError)
        {
            lock (_lock)
            {
                if (_task == null)
                    _task = _scheduler.Schedule(action, delay, period, onError);
            }
        }

        /// <summary>
        /// Stops the scheduled action if running. Idempotent: a no-op when not scheduled.
        /// </summary>
        /// <param name="waitForCurrentRun">When true, awaits a bounded completion of an in-flight run.</param>
        internal async ValueTask StopAsync(bool waitForCurrentRun = true)
        {
            ScheduledTask toStop;
            lock (_lock)
            {
                toStop = _task;
                _task = null;
            }

            if (toStop != null)
                await toStop.StopAsync(waitForCurrentRun).ConfigureAwait(false);
        }
    }
}
