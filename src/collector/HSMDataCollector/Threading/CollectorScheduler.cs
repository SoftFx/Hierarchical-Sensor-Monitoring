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
        private static readonly TimeSpan CurrentRunStopTimeout = TimeSpan.FromSeconds(1);

        private Task _currentRun = Task.CompletedTask;
        private int _isRunning;
        private bool _disposed;

        internal long NextRunMilliseconds { get; private set; }

        internal long BucketKey { get; set; } = long.MinValue;

        internal LinkedListNode<ScheduledTask> BucketNode { get; set; }

        internal bool IsDisposed => _disposed;

        internal bool IsRunning => Volatile.Read(ref _isRunning) == 1;

        internal Task CurrentRun
        {
            get
            {
                lock (_lock)
                    return _currentRun;
            }
        }

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

        internal bool TryAttachRun(Task currentRun)
        {
            lock (_lock)
            {
                if (_disposed || _isRunning == 1)
                    return false;

                _isRunning = 1;
                _currentRun = currentRun;
                return true;
            }
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
                await WaitForCurrentRunAsync(taskToWait).ConfigureAwait(false);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                CollectorScheduler.Remove(this);
            }
        }

        internal async Task ExecuteAttachedAsync()
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

        private static async Task WaitForCurrentRunAsync(Task task)
        {
            if (task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return;
            }

            // User callbacks can block forever; stop waits for short in-flight work but remains bounded.
            var completedTask = await Task.WhenAny(task, Task.Delay(CurrentRunStopTimeout)).ConfigureAwait(false);
            if (completedTask == task)
                await task.ConfigureAwait(false);
        }
    }

    internal static class CollectorScheduler
    {
        private static readonly object _lock = new object();
        private static readonly SortedDictionary<long, LinkedList<ScheduledTask>> _tasks = new SortedDictionary<long, LinkedList<ScheduledTask>>();
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
                AddTask(task);

            _signal.Set();

            return task;
        }

        internal static void Remove(ScheduledTask task)
        {
            lock (_lock)
                RemoveTask(task);

            _signal.Set();
        }

        internal static long GetTickCountMilliseconds() =>
            (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

        private static void Loop()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                List<ScheduledTask> dueTasks = null;
                var waitMilliseconds = Timeout.Infinite;
                var now = GetTickCountMilliseconds();

                lock (_lock)
                {
                    while (_tasks.Count > 0)
                    {
                        GetFirstBucket(out var bucketKey, out var bucket);

                        if (bucketKey > now)
                        {
                            waitMilliseconds = ToIntWait(bucketKey - now);
                            break;
                        }

                        _tasks.Remove(bucketKey);

                        foreach (var task in bucket)
                        {
                            task.BucketNode = null;
                            task.BucketKey = long.MinValue;

                            if (task.IsDisposed)
                                continue;

                            dueTasks = dueTasks ?? new List<ScheduledTask>();
                            dueTasks.Add(task);
                            task.Advance(now);
                            AddTask(task);
                        }
                    }
                }

                if (dueTasks != null)
                {
                    DispatchTasks(dueTasks);

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

        private static void DispatchTasks(List<ScheduledTask> dueTasks)
        {
            foreach (var task in dueTasks)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!task.TryAttachRun(completion.Task))
                    continue;

                ThreadPool.QueueUserWorkItem(_ => ExecuteQueuedTask(task, completion));
            }
        }

        private static async void ExecuteQueuedTask(ScheduledTask task, TaskCompletionSource<bool> completion)
        {
            try
            {
                await task.ExecuteAttachedAsync().ConfigureAwait(false);
            }
            finally
            {
                completion.TrySetResult(true);
            }
        }

        private static void AddTask(ScheduledTask task)
        {
            var key = task.NextRunMilliseconds;
            if (!_tasks.TryGetValue(key, out var bucket))
            {
                bucket = new LinkedList<ScheduledTask>();
                _tasks.Add(key, bucket);
            }

            task.BucketKey = key;
            task.BucketNode = bucket.AddLast(task);
        }

        private static void RemoveTask(ScheduledTask task)
        {
            var node = task.BucketNode;
            if (node == null)
                return;

            if (_tasks.TryGetValue(task.BucketKey, out var bucket))
            {
                bucket.Remove(node);

                if (bucket.Count == 0)
                    _tasks.Remove(task.BucketKey);
            }

            task.BucketNode = null;
            task.BucketKey = long.MinValue;
        }

        private static void GetFirstBucket(out long key, out LinkedList<ScheduledTask> bucket)
        {
            using (var enumerator = _tasks.GetEnumerator())
            {
                enumerator.MoveNext();
                key = enumerator.Current.Key;
                bucket = enumerator.Current.Value;
            }
        }
    }
}
