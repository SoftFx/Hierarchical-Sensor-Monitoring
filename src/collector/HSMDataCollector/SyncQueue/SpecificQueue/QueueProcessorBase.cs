using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
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

        // 1 while FlushAsync is iterating the bounded post-stop drain. Re-enqueued failed packages
        // during this window are immediately discarded by ClearQueue in FlushAndLogAsync — so the
        // DispatchPackageAsync failure exception describes them as "queued for clear" instead of
        // the misleading "preserved" wording used in the normal retry loop (#1087 A).
        private int _inFlushFlag;

        // Mirrors the BuildDate.Ticks of items currently in the channel, in FIFO order, so the
        // retry filter can peek the head's BuildDate (Channel<T> cannot Peek). Used to decide if a
        // failed-retry item is OLDER than what's currently waiting — if so, it would sit at the
        // tail with a stale BuildDate and survive later overflow eviction that drops the
        // actually-fresher FIFO head (issue #1090).
        //
        // Every channel write/read is paired with its mirror update under _mirrorLock (#1102-C2):
        // without that atomicity a consumer could pop the channel while the producer's mirror write
        // had not landed yet, leaving a PERMANENT orphan tick that shifts the peeked head to stale
        // values and silently weakens the retry filter for the rest of the process lifetime
        // (reproduced by QueueMirrorConsistencyTests).
        //
        // Replaces the original watermark approach from b0f8a90f5, which tracked the all-time
        // newest accepted BuildDate. That implementation dropped almost any multi-item retry
        // batch below capacity because the batch's own newest item raised the watermark above
        // its older siblings — caught by PR #1091 review. The mirror tracks only currently-queued
        // items, so re-enqueueing a batch [A(t1), B(t2), C(t3)] into an empty queue preserves
        // all three (the mirror is empty for A, becomes [t1] for B's check, [t1, t2] for C's).
        private readonly Queue<long> _buildDateMirror = new Queue<long>();
        private readonly object _mirrorLock = new object();

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
            EnqueueCore(new QueueItem<T>(item), isRetry: false);

        // A single flag instead of two separate booleans: retry semantics ALWAYS imply both
        // "bypass the writes-accepting gate" (we're preserving already-accepted work past a
        // StopAsync) and "drop self on overflow instead of evicting newer head" (issue #1088).
        // Splitting these into two parameters lets a future caller pass an invalid combination
        // (e.g. bypass-gate + normal-overflow) that silently regresses #1088.
        private EnqueueResult EnqueueCore(QueueItem<T> item, bool isRetry)
        {
            if (!isRetry && Volatile.Read(ref _acceptingWritesFlag) == 0)
                return EnqueueResult.RejectedStopped();

            // Issue #1088 / #1090: a failed-retry item must not evict newer telemetry on
            // overflow. The queue uses position-based FIFO eviction (drop head when over
            // MaxQueueSize), which is only correct while position order matches BuildDate order.
            // A retry item's BuildDate predates everything that arrived while its send attempt
            // was in flight; we drop the retry on two conditions:
            //
            //   1. (#1090) Its BuildDate predates the FIFO head currently in queue. If we wrote
            //      this retry to the tail it would sit there with a stale BuildDate while the
            //      fresher head got evicted on the next normal overflow — recreating #1088 from
            //      a different state path. The head BuildDate comes from the _buildDateMirror
            //      which is updated alongside the channel.
            //
            //   2. (#1088) The queue is already at MaxQueueSize and would otherwise overflow.
            //      Sufficient on its own for the explicit #1088 scenario; the head check above
            //      closes the more general case.
            //
            // Both filters are bypassed once _acceptingWritesFlag is 0 (post-Stop): there is no
            // fresh telemetry to protect during shutdown, and the in-flight items being cancelled
            // and re-enqueued must survive into the bounded flush.
            //
            // The returned DroppedCount=1 refers to THIS retry being rejected (not a head
            // eviction); telemetry collapses both into the same "lost-payload" count, which is
            // fine because the user-observable cost is identical.
            if (isRetry && IsOlderThanQueueHead(item))
                return EnqueueResult.Accept(droppedCount: 1);

            if (isRetry && QueueCount >= _options.MaxQueueSize)
                return EnqueueResult.Accept(droppedCount: 1);

            // Channel write and mirror update are one atomic step (#1102-C2): a consumer observing
            // the item in the channel is guaranteed to also observe its tick in the mirror, so no
            // dequeue can ever leave an orphan tick behind. The (potentially user-implemented)
            // logger is invoked outside the lock.
            bool written;
            lock (_mirrorLock)
            {
                written = Writer.TryWrite(item);

                if (written)
                    _buildDateMirror.Enqueue(item.BuildDate.Ticks);
            }

            if (!written)
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

        private bool IsOlderThanQueueHead(QueueItem<T> item)
        {
            // Shutdown bypass: once public writes are closed (StopAsync / dispose) there is no
            // fresh telemetry to protect from a stale retry, and a cancellation-retry of an
            // in-flight send must still be preserved so the bounded flush can either ship it or
            // log it as discarded. Skipping the filter here also avoids stale mirror state from
            // leaking into the post-stop window.
            if (Volatile.Read(ref _acceptingWritesFlag) == 0)
                return false;

            lock (_mirrorLock)
            {
                if (_buildDateMirror.Count == 0)
                    return false;

                return item.BuildDate.Ticks < _buildDateMirror.Peek();
            }
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
                    // Dedup the log because DispatchPackageAsync re-enqueues on a non-retryable
                    // server error and rethrows here on every cycle — a poison package would
                    // otherwise produce one error log per PackageCollectPeriod indefinitely.
                    // RETRY POLICY: re-enqueue + rethrow is intentional. The graceful-stop story
                    // depends on preserving accepted work even under transient transport failure;
                    // a per-package retry cap would re-introduce the old "data dropped on first
                    // server hiccup" regression. Overflow eviction is the backstop: a permanently
                    // failing item rides the queue tail until MaxQueueSize evicts it.
                    if (_queueManager != null)
                        _queueManager.AddQueueLoopError(QueueName, ex);
                    else
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
            Volatile.Write(ref _inFlushFlag, 1);
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
            finally
            {
                Volatile.Write(ref _inFlushFlag, 0);
            }
        }

        // True while the bounded post-stop drain is running. Subclasses use this to reword
        // dispatch-failure exceptions so the log line doesn't claim items were "preserved" right
        // before ClearQueue discards them.
        protected bool IsFlushing => Volatile.Read(ref _inFlushFlag) == 1;

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
                var dropped = ReEnqueueItems(package.Items);
                var preserved = package.Count - dropped;
                // During FlushAsync the items are re-enqueued only so ClearQueue can log them as
                // "discarded"; calling them "preserved" contradicts the very next log line.
                var fate = IsFlushing ? "queued for clear" : "preserved";
                // Drop count rolls up both #1088 (queue at capacity) and #1090 (retry older than
                // current head) reasons — wording stays generic so it's accurate for either.
                var loss = dropped > 0 ? $", {dropped} dropped" : string.Empty;
                throw new InvalidOperationException($"Failed to send package for {QueueName} ({preserved} {fate}{loss}). {sendingInfo.Error}");
            }

            _queueManager.AddPackageSendingInfo(sendingInfo);
            _queueManager.AddPackageInfo(QueueName, package.GetInfo());
        }

        // Self-reports retry-path evictions to the queue manager so every caller — including
        // FileQueueProcessor, which only re-enqueues single items — surfaces the drop count to
        // QueueOverflowSensor. Previously the reporting lived in ReEnqueueItems, which meant
        // file-queue retry drops were silent (issue #1088 review).
        protected EnqueueResult ReEnqueueItem(QueueItem<T> item)
        {
            var result = EnqueueCore(item, isRetry: true);
            if (result.IsAccepted && result.DroppedCount > 0)
                _queueManager?.ReportRequeueEviction(QueueName, result.DroppedCount);
            return result;
        }

        // Returns the count of items dropped at capacity so DispatchPackageAsync can include
        // it in the failure log line.
        protected int ReEnqueueItems(IEnumerable<QueueItem<T>> items)
        {
            int dropped = 0;
            foreach (var item in items)
            {
                var result = ReEnqueueItem(item);
                if (result.IsAccepted)
                    dropped += result.DroppedCount;
            }
            return dropped;
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

        // Single dequeue path so the BuildDate mirror always stays in step with the channel —
        // channel read and mirror dequeue are one atomic step under _mirrorLock (#1102-C2).
        // Callers in this class are: GetPackage, the EnqueueCore overflow trim loop, and ClearQueue.
        protected bool TryDequeue(out QueueItem<T> item)
        {
            lock (_mirrorLock)
            {
                if (!Reader.TryRead(out item))
                    return false;

                if (_buildDateMirror.Count > 0)
                    _buildDateMirror.Dequeue();

                return true;
            }
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
