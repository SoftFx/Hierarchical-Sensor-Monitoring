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

        internal bool TryMarkRunning()
        {
            if (_disposed || Interlocked.Exchange(ref _isRunning, 1) == 1)
                return false;

            lock (_lock)
            {
                if (_disposed)
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                    return false;
                }

                return true;
            }
        }

        internal void ExecuteAndComplete()
        {
            try
            {
                _action().ConfigureAwait(false).GetAwaiter().GetResult();
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

        internal void SetCurrentRun(Task task)
        {
            lock (_lock)
            {
                _currentRun = task;
            }
        }

        internal void TryRun()
        {
            if (!TryMarkRunning())
                return;

            SetCurrentRun(Task.Run(ExecuteAndComplete));
        }

        internal async ValueTask StopAsync(bool waitForCurrentRun = true)
        {
            Task taskToWait;
            lock (_lock)
            {
                _disposed = true;
                CollectorScheduler.Remove(this);
                taskToWait = _currentRun;
            }

            if (waitForCurrentRun)
                await taskToWait.ConfigureAwait(false);
        }

        public void Dispose() => StopAsync(waitForCurrentRun: false).ConfigureAwait(false).GetAwaiter().GetResult();
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
                try
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
                        DispatchBatch(dueTasks);

                        _signal.Wait(1, _cancellation.Token);
                        _signal.Reset();

                        continue;
                    }

                    _signal.Wait(waitMilliseconds, _cancellation.Token);
                    _signal.Reset();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Prevent unexpected exceptions from killing the scheduler loop.
                }
            }
        }

        private static void DispatchBatch(List<ScheduledTask> dueTasks)
        {
            var ready = new List<ScheduledTask>(dueTasks.Count);

            foreach (var task in dueTasks)
            {
                if (task.TryMarkRunning())
                    ready.Add(task);
            }

            if (ready.Count == 0)
                return;

            var batch = ready;
            var batchTask = Task.Run(() =>
            {
                for (int i = 0; i < batch.Count; i++)
                    batch[i].ExecuteAndComplete();
            });

            for (int i = 0; i < ready.Count; i++)
                ready[i].SetCurrentRun(batchTask);
        }

        private static int ToIntWait(long waitMilliseconds)
        {
            if (waitMilliseconds <= 0)
                return 0;

            return waitMilliseconds > int.MaxValue ? int.MaxValue : (int)waitMilliseconds;
        }
    }
}
