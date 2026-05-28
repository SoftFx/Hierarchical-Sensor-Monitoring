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
        private readonly ICollectorScheduler _scheduler;
        private static readonly TimeSpan CurrentRunStopTimeout = TimeSpan.FromSeconds(1);

        private Task _currentRun = Task.CompletedTask;
        private int _isRunning;
        private bool _disposed;

        internal long NextRunMilliseconds { get; private set; }

        internal long BucketKey { get; set; } = long.MinValue;

        internal LinkedListNode<ScheduledTask> BucketNode { get; set; }

        internal bool IsDisposed => Volatile.Read(ref _disposed);

        internal bool IsRunning => Volatile.Read(ref _isRunning) == 1;

        internal Task CurrentRun
        {
            get
            {
                lock (_lock)
                    return _currentRun;
            }
        }

        internal ScheduledTask(ICollectorScheduler scheduler, Func<Task> action, TimeSpan delay, TimeSpan period, Action<Exception> onError)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
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
                Volatile.Write(ref _disposed, true);
                _scheduler.Remove(this);
                taskToWait = _currentRun;
            }

            if (waitForCurrentRun)
                await WaitForCurrentRunAsync(taskToWait).ConfigureAwait(false);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Volatile.Write(ref _disposed, true);
                _scheduler.Remove(this);
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

                // One-shot task self-disposes once its single run completes. Write under the lock so
                // concurrent IsDisposed readers (e.g. CollectorScheduler.Loop's pre-dispatch check)
                // observe a consistent value with every other writer of _disposed.
                if (_period == Timeout.InfiniteTimeSpan)
                {
                    lock (_lock)
                    {
                        Volatile.Write(ref _disposed, true);
                        _scheduler.Remove(this);
                    }
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

    /// <summary>
    /// Bucketed timer-wheel scheduler. Owns a single worker task that dispatches due actions onto
    /// the threadpool. Construct one per <see cref="Core.DataCollector"/>; sharing across collectors
    /// is allowed but not required.
    /// </summary>
    internal sealed class CollectorScheduler : ICollectorScheduler
    {
        private readonly object _lock = new object();
        private readonly SortedDictionary<long, LinkedList<ScheduledTask>> _tasks = new SortedDictionary<long, LinkedList<ScheduledTask>>();
        private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Task _worker;
        private static readonly TimeSpan WorkerStopTimeout = TimeSpan.FromSeconds(5);
        private int _disposed;

        public CollectorScheduler()
        {
            _worker = Task.Run(Loop);
        }

        public ScheduledTask Schedule(Action action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return Schedule(() =>
            {
                action();
                return Task.CompletedTask;
            }, delay, period, onError);
        }

        public ScheduledTask Schedule(Func<Task> action, TimeSpan delay, TimeSpan period, Action<Exception> onError = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (period != Timeout.InfiniteTimeSpan && period <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be greater than zero.");

            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative.");

            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(nameof(CollectorScheduler));

            var task = new ScheduledTask(this, action, delay, period, onError);

            lock (_lock)
            {
                if (Volatile.Read(ref _disposed) == 1)
                {
                    task.Dispose();
                    throw new ObjectDisposedException(nameof(CollectorScheduler));
                }

                AddTask(task);
            }

            // Mirrors Remove(): if Dispose() races past the _disposed guard above and
            // disposes _signal before this Set runs, swallow the ObjectDisposedException —
            // the worker is already shut down so there is nothing to wake.
            try
            {
                _signal.Set();
            }
            catch (ObjectDisposedException)
            {
            }

            return task;
        }

        public void Remove(ScheduledTask task)
        {
            if (task == null)
                return;

            // A ScheduledTask can outlive its scheduler's Dispose if cleanup happens out
            // of order (e.g. a sensor's StopAsync running after the scheduler is disposed).
            // Guard both the dictionary mutation and the signal so we never touch disposed state.
            if (Volatile.Read(ref _disposed) == 1)
                return;

            lock (_lock)
                RemoveTask(task);

            try
            {
                _signal.Set();
            }
            catch (ObjectDisposedException)
            {
                // Lost the race with Dispose; nothing to wake.
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (Volatile.Read(ref _disposed) == 1)
                    return;

                Volatile.Write(ref _disposed, 1);
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Cancellation token source may have been disposed concurrently; ignore.
            }
            catch (AggregateException ex)
            {
                Trace.TraceError($"{nameof(CollectorScheduler)} cancellation callback failed: {ex}");
            }

            try
            {
                _signal.Set();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a concurrent path; ignore.
            }

            var workerExited = false;
            try
            {
                workerExited = _worker.Wait(WorkerStopTimeout);
            }
            catch (AggregateException ex)
            {
                Trace.TraceError($"{nameof(CollectorScheduler)} worker faulted while stopping: {ex}");
            }

            if (!workerExited)
            {
                Trace.TraceError(
                    $"{nameof(CollectorScheduler)} worker did not stop within {WorkerStopTimeout}. " +
                    "Scheduler handles were left undisposed to avoid faulting the worker task.");
            }

            // Only dispose the cancellation source and the signal if the worker has actually exited.
            // If we time out and the worker later wakes from _signal.Wait, it will read
            // _cancellation.Token (which throws ObjectDisposedException on a disposed CTS) and the
            // worker task would fault unobserved. Both objects are GC-collectable when unreachable.
            if (workerExited)
            {
                _cancellation.Dispose();
                _signal.Dispose();
            }
        }

        internal static long GetTickCountMilliseconds() =>
            (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

        private void Loop()
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

        private void AddTask(ScheduledTask task)
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

        private void RemoveTask(ScheduledTask task)
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

        private void GetFirstBucket(out long key, out LinkedList<ScheduledTask> bucket)
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
