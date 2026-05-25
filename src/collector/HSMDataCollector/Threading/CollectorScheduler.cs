using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Threading
{
    internal sealed class ScheduledTask : IDisposable
    {
        private readonly Func<Task> _action;
        private readonly Action<Exception> _onError;
        private readonly TimeSpan _period;
        private readonly object _lock = new object();

        private Task _currentRun = Task.CompletedTask;
        private int _isRunning;
        private bool _disposed;

        internal long NextRunMilliseconds { get; private set; }

        internal bool IsDisposed => _disposed;

        internal ScheduledTask(Func<Task> action, TimeSpan delay, TimeSpan period, Action<Exception> onError)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _period = period;
            _onError = onError;
            NextRunMilliseconds = CollectorScheduler.GetTickCountMilliseconds() + Math.Max(0L, (long)delay.TotalMilliseconds);
        }

        internal void Advance(long nowMilliseconds)
        {
            if (_period == Timeout.InfiniteTimeSpan)
            {
                NextRunMilliseconds = long.MaxValue;
                return;
            }

            var interval = Math.Max(1L, (long)_period.TotalMilliseconds);
            do
            {
                NextRunMilliseconds += interval;
            }
            while (NextRunMilliseconds <= nowMilliseconds);
        }

        internal void TryRun()
        {
            if (_disposed || Interlocked.Exchange(ref _isRunning, 1) == 1)
                return;

            lock (_lock)
            {
                if (_disposed)
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                    return;
                }

                _currentRun = Task.Run(ExecuteAsync);
            }
        }

        internal async ValueTask StopAsync()
        {
            Task taskToWait;
            lock (_lock)
            {
                _disposed = true;
                CollectorScheduler.Remove(this);
                taskToWait = _currentRun;
            }

            await taskToWait.ConfigureAwait(false);
        }

        public void Dispose() => StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        private async Task ExecuteAsync()
        {
            try
            {
                await _action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);

                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _disposed = true;
                    CollectorScheduler.Remove(this);
                }
            }
        }
    }

    internal static class CollectorScheduler
    {
        private static readonly object _lock = new object();
        private static readonly List<ScheduledTask> _tasks = new List<ScheduledTask>();
        private static readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);
        private static readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private static readonly Task _worker = Task.Run(Loop);

        internal static ScheduledTask Schedule(Action action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null)
        {
            return Schedule(() =>
            {
                action();
                return Task.CompletedTask;
            }, delay, period, onError);
        }

        internal static ScheduledTask Schedule(Func<Task> action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null)
        {
            if (period != Timeout.InfiniteTimeSpan && period <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be greater than zero.");

            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative.");

            var task = new ScheduledTask(action, delay, period, onError);

            lock (_lock)
                _tasks.Add(task);

            _signal.Set();

            return task;
        }

        internal static void Remove(ScheduledTask task)
        {
            lock (_lock)
                _tasks.Remove(task);

            _signal.Set();
        }

        internal static long GetTickCountMilliseconds() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

        private static void Loop()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                List<ScheduledTask> dueTasks = null;
                var waitMilliseconds = Timeout.Infinite;
                var now = GetTickCountMilliseconds();

                lock (_lock)
                {
                    for (int i = _tasks.Count - 1; i >= 0; i--)
                    {
                        var task = _tasks[i];

                        if (task.IsDisposed)
                        {
                            _tasks.RemoveAt(i);
                            continue;
                        }

                        if (task.NextRunMilliseconds <= now)
                        {
                            dueTasks = dueTasks ?? new List<ScheduledTask>();
                            dueTasks.Add(task);
                            task.Advance(now);
                        }
                        else
                        {
                            var nextWait = task.NextRunMilliseconds - now;
                            waitMilliseconds = waitMilliseconds == Timeout.Infinite
                                ? ToIntWait(nextWait)
                                : Math.Min(waitMilliseconds, ToIntWait(nextWait));
                        }
                    }
                }

                if (dueTasks != null)
                {
                    foreach (var task in dueTasks)
                        task.TryRun();

                    continue;
                }

                try
                {
                    _signal.Wait(waitMilliseconds, _cancellation.Token);
                    _signal.Reset();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static int ToIntWait(long waitMilliseconds)
        {
            if (waitMilliseconds <= 0)
                return 0;

            return waitMilliseconds > int.MaxValue ? int.MaxValue : (int)waitMilliseconds;
        }
    }
}
