using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
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
    /// Shared queue processor template that owns the dequeue -> send -> success/retry/cancel/flush
    /// algorithm. Subclasses provide only how to dispatch one batch
    /// (<see cref="TryDispatchOneAsync(CancellationToken)"/>) and, optionally, how to wait for new
    /// work (<see cref="WaitForReadyAsync(CancellationToken)"/>).
    /// </summary>
    internal abstract class QueueProcessorBase<T> : IQueueProcessor
    {
        private Task _task;
        private bool _disposed;
        private QueueState _state = QueueState.Stopped;
        private readonly Channel<QueueItem<T>> _channel;

        protected ChannelReader<QueueItem<T>> Reader => _channel.Reader;
        protected ChannelWriter<QueueItem<T>> Writer => _channel.Writer;
        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;

        private readonly object _lifecycleLock = new object();
        private int _cleanupContinuationRegistered;

        // 1 when the queue is open for public writes. Internal retry re-enqueue bypasses this so
        // post-stop flush failures can preserve already accepted work.
        private int _acceptingWritesFlag = 1;

        // Last shutdown mode requested via StopAsync. Read by the processing loop's catch blocks to
        // decide whether canceled packages should be re-enqueued.
        private int _currentShutdownModeRaw = (int)ShutdownMode.GracefulStop;

        public abstract string QueueName { get; }

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
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
                    // Close the queue to new public writes the moment we commit to stopping —
                    // not at CompleteStoppedTask. Otherwise during the degraded "sender ignores
                    // cancellation" window between Stopping and the final timeout return, public
                    // AddValue paths whose CanAcceptData check still says true would land items
                    // into the channel that ClearQueue silently discards seconds later. Internal
                    // retry re-enqueue bypasses this flag via ReEnqueueItem.
                    Volatile.Write(ref _acceptingWritesFlag, 0);
                    taskToWait = _task;
                    tokenSourceToDispose = _cancellationTokenSource;
                }

                try
                {
                    if (mode == ShutdownMode.GracefulStop)
                        tokenSourceToDispose?.Cancel();
                    else
                        tokenSourceToDispose?.CancelAfter(TimeSpan.Zero);

                    if (!taskToWait.IsCompleted)
                    {
                        using (var delayCancellation = new CancellationTokenSource())
                        {
                            var stopWaitTimeout = mode.StopWaitTimeout(_options.RequestTimeout);
                            var delayTask = Task.Delay(stopWaitTimeout, delayCancellation.Token);
                            var completedTask = await Task.WhenAny(taskToWait, delayTask).ConfigureAwait(false);

                            if (completedTask != taskToWait)
                            {
                                RegisterCompletionCleanup(taskToWait, tokenSourceToDispose);

                                _logger.Error($"{QueueName} queue processor did not stop within {stopWaitTimeout}. IDataSender may ignore cancellation.");

                                if (clearOnStop)
                                    ClearQueue();

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

        internal virtual EnqueueResult Enqueue(T item) =>
            EnqueueCore(new QueueItem<T>(item), rejectWhenNotAcceptingWrites: true);

        private EnqueueResult EnqueueCore(QueueItem<T> item, bool rejectWhenNotAcceptingWrites)
        {
            if (rejectWhenNotAcceptingWrites && Volatile.Read(ref _acceptingWritesFlag) == 0)
                return EnqueueResult.RejectedStopped();

            if (!Writer.TryWrite(item))
            {
                _logger.Error($"{QueueName} queue processor did not write value");
                return EnqueueResult.RejectedStopped();
            }

            int dropped = 0;
            while (QueueCount > _options.MaxQueueSize)
            {
                if (!TryDequeue(out _))
                    break;

                dropped++;
            }

            return EnqueueResult.Accept(dropped);
        }

        internal virtual EnqueueResult Enqueue(IEnumerable<T> items)
        {
            if (Volatile.Read(ref _acceptingWritesFlag) == 0)
                return EnqueueResult.RejectedStopped();

            int dropped = 0;
            bool anyAccepted = false;
            foreach (var item in items)
            {
                var result = Enqueue(item);
                if (result.IsAccepted)
                {
                    anyAccepted = true;
                    dropped += result.DroppedCount;
                }
                else if (result.Status == EnqueueStatus.RejectedQueueStopped)
                {
                    // Preserve the count of items already evicted by overflow before the queue
                    // flipped: the overflow sensor would otherwise under-report this batch.
                    return EnqueueResult.RejectedStopped(dropped);
                }
            }

            return EnqueueResult.Accept(dropped);
        }

        internal DataPackage<T> GetPackage()
        {
            var estimatedCount = Math.Min(QueueCount + 16, _options.MaxValuesInPackage);
            var package = new DataPackage<T>(estimatedCount);

            while (package.Count < _options.MaxValuesInPackage && TryDequeue(out QueueItem<T> item))
            {
                if (Validate(item.Value))
                    package.AddValue(item);
            }

            return package;
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

        protected virtual async ValueTask WaitForReadyAsync(CancellationToken token)
        {
            await Reader.WaitToReadAsync(token).ConfigureAwait(false);
        }

        protected abstract ValueTask<bool> TryDispatchOneAsync(CancellationToken token);

        protected virtual bool ShouldContinueDispatching() => true;

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

        protected async ValueTask DispatchPackageAsync(
            DataPackage<T> package,
            Func<IEnumerable<T>, CancellationToken, ValueTask<PackageSendingInfo>> send,
            CancellationToken token)
        {
            if (package == null || package.Count == 0)
                return;

            PackageSendingInfo sendingInfo;

            try
            {
                sendingInfo = await send(package, token).ConfigureAwait(false);
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

            if (sendingInfo.Error != null)
            {
                ReEnqueueItems(package.Items);
                throw new InvalidOperationException($"Failed to send package for {QueueName} ({package.Count} values preserved). {sendingInfo.Error}");
            }

            _queueManager.AddPackageSendingInfo(sendingInfo);
            _queueManager.AddPackageInfo(QueueName, package.GetInfo());
        }

        protected EnqueueResult ReEnqueueItem(QueueItem<T> item) =>
            EnqueueCore(item, rejectWhenNotAcceptingWrites: false);

        protected EnqueueResult ReEnqueueItem(T item) =>
            ReEnqueueItem(new QueueItem<T>(item));

        protected void ReEnqueueItems(IEnumerable<QueueItem<T>> items)
        {
            foreach (var item in items)
                ReEnqueueItem(item);
        }

        protected ShutdownMode CurrentShutdownMode => (ShutdownMode)Volatile.Read(ref _currentShutdownModeRaw);

        protected bool PreserveCanceledPackages => CurrentShutdownMode.PreserveCanceledPackages();

        internal int QueueCount => Reader.Count;

        protected bool IsEmpty => QueueCount == 0;

        protected ValueTask<bool> WaitToReadAsync(CancellationToken token) => Reader.WaitToReadAsync(token);

        protected Task DelayAfterFailureAsync(CancellationToken token)
        {
            var delay = _options.PackageCollectPeriod > TimeSpan.Zero
                ? _options.PackageCollectPeriod
                : TimeSpan.FromMilliseconds(100);

            return Task.Delay(delay, token);
        }

        protected bool TryDequeue(out QueueItem<T> item) => Reader.TryRead(out item);

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
            int count = 0;
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
