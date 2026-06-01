using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal enum QueueState : byte
    {
        Stopped,
        Running,
        Stopping,
    }


    /// <summary>
    /// Shared queue processor template that owns the dequeue → send → success/retry/cancel/flush
    /// algorithm. Subclasses provide only how to dispatch one batch
    /// (<see cref="TryDispatchOneAsync(CancellationToken)"/>) and, optionally, how to wait for new
    /// work (<see cref="WaitForReadyAsync(CancellationToken)"/>).
    ///
    /// The <see cref="ShutdownMode"/> flowed through <see cref="StopAsync(ShutdownMode)"/> drives
    /// whether canceled in-flight packages are re-enqueued, whether accepted work is preserved for
    /// a follow-up flush, and whether the queue clears its leftover items immediately. After the
    /// queue transitions to <see cref="QueueState.Stopped"/> following a stop cycle, new
    /// <see cref="Enqeue(T)"/> calls return <see cref="EnqueueStatus.RejectedQueueStopped"/> until
    /// the next <see cref="Start"/>.
    /// </summary>
    internal abstract class QueueProcessorBase<T> : IQueueProcessor
    {
        private Task _task;
        private bool _disposed;
        private QueueState _state;
        private readonly Channel<QueueItem<T>> _channel;

        protected ChannelReader<QueueItem<T>> Reader => _channel.Reader;
        protected ChannelWriter<QueueItem<T>> Writer => _channel.Writer;
        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;

        private readonly object _lifecycleLock = new object();
        protected int _queueCount;
        private int _cleanupContinuationRegistered;

        // 1 when the queue is open for new writes. Cleared on terminal stop (state machine reaches
        // Stopped after running) and on Dispose; restored on Start. Read on the hot Enqeue path
        // without holding _lifecycleLock so the lifecycle gate stays cheap.
        private int _acceptingWritesFlag = 1;

        // Last shutdown mode requested via StopAsync. Read by the processing loop's catch blocks to
        // decide whether canceled packages should be re-enqueued. Default while running is
        // GracefulStop so an unexpected mid-run cancellation preserves work.
        private int _currentShutdownModeRaw = (int)ShutdownMode.GracefulStop;

        public abstract string QueueName { get; }

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender  = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _queueManager = queueManager;
            _logger = logger;

            _channel = Channel.CreateUnbounded<QueueItem<T>>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        }

        public bool Start()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    _logger.Error($"{QueueName} queue processor is disposed and cannot be started.");
                    return false;
                }

                if (_state == QueueState.Stopping)
                {
                    _logger.Error($"{QueueName} queue processor is still stopping and cannot be started again yet.");
                    return false;
                }

                if (_state == QueueState.Running)
                {
                    // Defensive: ProcessingLoop should never exit on its own (it loops until cancellation),
                    // but if a subclass override breaks that contract, recover by treating the queue as stopped.
                    // We do NOT call Task.Dispose on a possibly-faulted task — let the GC handle it to avoid
                    // surfacing unobserved exceptions on the finalizer thread.
                    if (_task == null || _task.IsCompleted)
                    {
                        _logger.Error($"{QueueName} queue processor task exited unexpectedly; restarting.");
                        _task = null;
                        _cancellationTokenSource?.Dispose();
                        _cancellationTokenSource = null;
                        _state = QueueState.Stopped;
                        Interlocked.Exchange(ref _cleanupContinuationRegistered, 0);
                    }
                    else
                    {
                        return true;
                    }
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _task = Task.Run(() => RunLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _state = QueueState.Running;
                Volatile.Write(ref _currentShutdownModeRaw, (int)ShutdownMode.GracefulStop);
                Volatile.Write(ref _acceptingWritesFlag, 1);
                Interlocked.Exchange(ref _cleanupContinuationRegistered, 0);
            }

            return true;
        }

        public async ValueTask<bool> StopAsync(ShutdownMode mode)
        {
            try
            {
                Task taskToWait;
                CancellationTokenSource tokenSourceToDispose;
                var clearOnStop = mode.ClearOnStop();

                lock (_lifecycleLock)
                {
                    Volatile.Write(ref _currentShutdownModeRaw, (int)mode);

                    if (_state == QueueState.Stopped)
                    {
                        Volatile.Write(ref _acceptingWritesFlag, 0);
                        if (clearOnStop)
                            ClearQueue();

                        return true;
                    }

                    if (_state == QueueState.Stopping)
                    {
                        if (clearOnStop)
                            ClearQueue();

                        return false;
                    }

                    _state = QueueState.Stopping;
                    taskToWait = _task;
                    tokenSourceToDispose = _cancellationTokenSource;
                }

                try
                {
                    tokenSourceToDispose?.Cancel();

                    if (!taskToWait.IsCompleted)
                    {
                        using (var delayCancellation = new CancellationTokenSource())
                        {
                            var delayTask = Task.Delay(_options.RequestTimeout, delayCancellation.Token);
                            var completedTask = await Task.WhenAny(taskToWait, delayTask).ConfigureAwait(false);

                            if (completedTask != taskToWait)
                            {
                                RegisterCompletionCleanup(taskToWait, tokenSourceToDispose);

                                _logger.Error($"{QueueName} queue processor did not stop within {_options.RequestTimeout}. IDataSender may ignore cancellation.");

                                if (clearOnStop)
                                    ClearQueue();

                                // The processing task is still alive; we will not flip the write flag here
                                // because the queue may still be draining items from the orphan task.
                                return false;
                            }

                            delayCancellation.Cancel();
                        }
                    }

                    await taskToWait.ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    if (taskToWait.IsCompleted)
                        CompleteStoppedTask(taskToWait, tokenSourceToDispose, clearOnStop);
                }
            }
            catch (OperationCanceledException)
            {
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                return true;
            }
        }

        internal virtual EnqueueResult Enqeue(T item)
        {
            if (Volatile.Read(ref _acceptingWritesFlag) == 0)
                return EnqueueResult.RejectedStopped();

            Interlocked.Increment(ref _queueCount);

            if (!Writer.TryWrite(new QueueItem<T>(item)))
            {
                Interlocked.Decrement(ref _queueCount);
                return EnqueueResult.RejectedStopped();
            }

            int dropped = 0;
            while (Volatile.Read(ref _queueCount) > _options.MaxQueueSize)
            {
                if (!TryDequeue(out _))
                    break;
                dropped++;
            }

            return EnqueueResult.Accept(dropped);
        }

        internal virtual EnqueueResult Enqeue(IEnumerable<T> items)
        {
            if (Volatile.Read(ref _acceptingWritesFlag) == 0)
                return EnqueueResult.RejectedStopped();

            int dropped = 0;
            bool anyAccepted = false;
            foreach (var item in items)
            {
                var result = Enqeue(item);
                if (result.IsAccepted)
                {
                    anyAccepted = true;
                    dropped += result.DroppedCount;
                }
                else if (result.Status == EnqueueStatus.RejectedQueueStopped)
                {
                    return result;
                }
            }

            return anyAccepted ? EnqueueResult.Accept(dropped) : EnqueueResult.Accept(0);
        }


        internal DataPackage<T> GetPackage()
        {
            var result = new DataPackage<T>();
            var items = new List<T>(_options.MaxValuesInPackage);
            var maxInspected = _options.MaxValuesInPackage > int.MaxValue / 2
                ? int.MaxValue
                : _options.MaxValuesInPackage * 2;
            var inspected = 0;
            DateTime now = DateTime.UtcNow;

            while (items.Count < _options.MaxValuesInPackage &&
                   inspected < maxInspected &&
                   TryDequeue(out QueueItem<T> item))
            {
                inspected++;
                result.AddInfo((now - item.BuildDate).TotalSeconds, 1);

                if (Validate(item.Value))
                    items.Add(item.Value);
            }

            result.Items = items;
            return result;
        }

        private bool Validate(T item)
        {
            if (item is BarSensorValueBase bar)
            {
                if (bar.Count <= 0)
                    return false;
            }

            return true;
        }


        /// <summary>
        /// Outer processing loop. Subclasses do NOT override this; they override
        /// <see cref="WaitForReadyAsync(CancellationToken)"/> and
        /// <see cref="TryDispatchOneAsync(CancellationToken)"/>. Centralizing the loop here
        /// removes the per-queue drift of <c>catch (OperationCanceledException)</c> and
        /// general-exception handling that has caused multiple shutdown-stability bugs.
        /// </summary>
        private async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await WaitForReadyAsync(token).ConfigureAwait(false);
                    await DispatchPendingAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                    try
                    {
                        await DelayAfterFailureAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                }
            }
        }

        private async ValueTask DispatchPendingAsync(CancellationToken token)
        {
            while (!IsEmpty && !token.IsCancellationRequested)
            {
                var dispatched = await TryDispatchOneAsync(token).ConfigureAwait(false);
                if (!dispatched || !ShouldContinueDispatching())
                    break;
            }
        }

        /// <summary>
        /// Wait for at least one item to be available (or for the wait period to elapse). Default
        /// is an event-driven wait via the channel reader; data queue overrides with a polling
        /// delay to gather batches before dispatch.
        /// </summary>
        protected virtual async ValueTask WaitForReadyAsync(CancellationToken token)
        {
            await Reader.WaitToReadAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatch one package/item. Returns true if anything was actually sent, false to short-
        /// circuit the dispatch loop (e.g. empty package after validation, or single-item queue
        /// fully drained).
        /// </summary>
        protected abstract ValueTask<bool> TryDispatchOneAsync(CancellationToken token);

        /// <summary>
        /// Whether the dispatch loop should pull another batch within the same outer loop
        /// iteration after a successful dispatch. Default is true (drain until empty);
        /// data queue overrides to only continue while the next batch could be full.
        /// </summary>
        protected virtual bool ShouldContinueDispatching() => true;

        /// <summary>
        /// Drain remaining queued work with a bounded token. Used by the graceful-stop path.
        /// </summary>
        public virtual async Task FlushAsync(CancellationToken token)
        {
            try
            {
                while (!IsEmpty && !token.IsCancellationRequested)
                {
                    if (!await TryDispatchOneAsync(token).ConfigureAwait(false))
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        /// <summary>
        /// Send the contents of <paramref name="package"/> via <paramref name="send"/>, recording
        /// diagnostics on success and re-enqueuing on retryable failure. On cancellation, items
        /// are re-enqueued only if the current shutdown mode preserves canceled packages.
        /// </summary>
        protected async ValueTask DispatchPackageAsync(
            DataPackage<T> package,
            Func<IEnumerable<T>, CancellationToken, ValueTask<PackageSendingInfo>> send,
            CancellationToken token)
        {
            if (package == null || !package.Items.Any())
                return;

            try
            {
                var sendingInfo = await send(package.Items, token).ConfigureAwait(false);
                _queueManager.AddPackageSendingInfo(sendingInfo);
                _queueManager.AddPackageInfo(QueueName, package.GetInfo());
            }
            catch (OperationCanceledException)
            {
                if (PreserveCanceledPackages)
                    ReEnqueueItems(package.Items);
                throw;
            }
            catch
            {
                ReEnqueueItems(package.Items);
                throw;
            }
        }

        /// <summary>
        /// Re-enqueue items back into this queue. Used by send-retry paths so retryable failures
        /// do not drop accepted work. Items rejected because the queue is already terminally
        /// stopped are silently dropped — the caller is the queue's own processing loop, and there
        /// is nothing useful to do with them at this layer.
        /// </summary>
        protected void ReEnqueueItems(IEnumerable<T> items)
        {
            foreach (var item in items)
                Enqeue(item);
        }

        protected ShutdownMode CurrentShutdownMode => (ShutdownMode)Volatile.Read(ref _currentShutdownModeRaw);

        protected bool PreserveCanceledPackages => CurrentShutdownMode.PreserveCanceledPackages();

        internal int QueueCount => Volatile.Read(ref _queueCount);

        protected bool IsEmpty => Volatile.Read(ref _queueCount) == 0;

        protected ValueTask<bool> WaitToReadAsync(CancellationToken token) => Reader.WaitToReadAsync(token);

        protected Task DelayAfterFailureAsync(CancellationToken token)
        {
            var delay = _options.PackageCollectPeriod > TimeSpan.Zero
                ? _options.PackageCollectPeriod
                : TimeSpan.FromMilliseconds(100);

            return Task.Delay(delay, token);
        }

        protected bool TryDequeue(out QueueItem<T> item)
        {
            if (!Reader.TryRead(out item))
                return false;

            Interlocked.Decrement(ref _queueCount);
            return true;
        }

        private void RegisterCompletionCleanup(Task task, CancellationTokenSource tokenSource)
        {
            if (Interlocked.Exchange(ref _cleanupContinuationRegistered, 1) == 1)
                return;

            task.ContinueWith(_ => CompleteStoppedTask(task, tokenSource, clearQueue: false),
                              CancellationToken.None,
                              TaskContinuationOptions.ExecuteSynchronously,
                              TaskScheduler.Default);
        }

        private void CompleteStoppedTask(Task task, CancellationTokenSource tokenSource, bool clearQueue)
        {
            lock (_lifecycleLock)
            {
                if (!ReferenceEquals(_task, task))
                    return;

                task.Dispose();
                _task = null;

                tokenSource?.Dispose();

                if (ReferenceEquals(_cancellationTokenSource, tokenSource))
                    _cancellationTokenSource = null;

                if (clearQueue)
                    ClearQueue();

                _state = QueueState.Stopped;
                Volatile.Write(ref _acceptingWritesFlag, 0);
                Interlocked.Exchange(ref _cleanupContinuationRegistered, 0);
            }
        }

        public int ClearQueue()
        {
            var count = 0;
            while (TryDequeue(out _))
                count++;

            return count;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                try
                {
                    Volatile.Write(ref _acceptingWritesFlag, 0);
                    StopAsync(ShutdownMode.TerminalDispose).AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {

                    _logger.Error($"Error during disposal: {ex}");
                }
            }

            _disposed = true;
        }

    }
}
