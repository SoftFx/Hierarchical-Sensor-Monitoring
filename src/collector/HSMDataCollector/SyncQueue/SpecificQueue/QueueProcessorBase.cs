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

        // What the stop drain sends through (#1747): a built-in sender that repeats a failed
        // request inside a send gives a view of itself that does not; any other sender, a
        // host's own included, is used as it is.
        private readonly IDataSender _drainSender;

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

        public abstract string QueueName { get; }

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _drainSender = (_sender as IStopDrainSender)?.StopDrain ?? _sender;
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

            // Monitoring-history retry policy: every value is history, so a failed retry is NEVER
            // dropped below capacity — it is re-enqueued and retried (at-least-once). The single
            // drop is the #1088 backstop: when the queue is already at MaxQueueSize the retry (the
            // oldest data — it was popped from the head while everything queued behind it arrived
            // during its send) is dropped instead of evicting a queued value via the tail-write +
            // overflow path below, matching normal overflow's newest-wins.
            //
            // The earlier #1090 below-capacity head-drop (IsOlderThanQueueHead + _buildDateMirror)
            // was removed: for a large history buffer it discarded values that did not need to be
            // lost. The DroppedCount=1 reports THIS retry being rejected at capacity.
            if (isRetry && QueueCount >= _options.MaxQueueSize)
                return EnqueueResult.Accept(droppedCount: 1);

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
            // True when the pause after a failure ended because the transport got its connection
            // back: what waited for it is dispatched now, not after the next wait for work.
            var dispatchNow = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!dispatchNow)
                        await WaitForReadyAsync(token).ConfigureAwait(false);

                    dispatchNow = false;

                    await DispatchPendingAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    // Dedup the log because DispatchPackageAsync re-enqueues a failed package and
                    // rethrows here on every cycle — a package that keeps failing would otherwise
                    // produce one error log per PackageCollectPeriod indefinitely. (A registration
                    // the server refused with a 4xx does not come back: RejectionIsFinal.)
                    // RETRY POLICY: re-enqueue + rethrow is intentional. The graceful-stop story
                    // depends on preserving accepted work even under transient transport failure;
                    // a per-package retry cap would re-introduce the old "data dropped on first
                    // server hiccup" regression. Overflow eviction is the backstop: a permanently
                    // failing item rides the queue tail until MaxQueueSize evicts it.
                    //
                    // A package that waits for the transport to come back is not an error of the
                    // queue: the transport reports its own state, once, and the queue stays quiet.
                    // Unless that transport belongs to another collector: it reports there, and
                    // not at all while its owner is stopped, so here the queue is the only voice
                    // (deduplicated like every queue error).
                    if (ex is PackageSendFailedException failed && failed.IsTransportDown && !(_queueManager?.TransportReportsElsewhere ?? false))
                        _logger.Debug(ex.Message);
                    else if (_queueManager != null)
                        _queueManager.AddQueueLoopError(QueueName, ex);
                    else
                        _logger.Error(ex);

                    try
                    {
                        dispatchNow = await PauseAfterFailureAsync(ex, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                }
            }
        }

        // The pause before a failed package is tried again: the collect period, or, for a
        // package that only lacked a connection, until the transport has one, whichever comes
        // first. Returns true in the second case.
        private async Task<bool> PauseAfterFailureAsync(Exception failure, CancellationToken token)
        {
            var pause = DelayAfterFailureAsync(token);

            if (!(failure is PackageSendFailedException failed) || !failed.IsTransportDown || !(_sender is ICollectorTransport transport))
            {
                await pause.ConfigureAwait(false);
                return false;
            }

            // A floor, so that a connection that comes and goes cannot make this loop spin.
            await Task.Delay(TransportRetryFloor, token).ConfigureAwait(false);

            // The wait for a connection ends with this pause, whichever of the two is first:
            // left pending, one more waiter would stay behind every cycle of an outage.
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var connected = transport.WaitForConnectionAsync(wait.Token);
                var woken = await Task.WhenAny(pause, connected).ConfigureAwait(false) == connected && connected.Status == TaskStatus.RanToCompletion;

                wait.Cancel();
                _ = connected.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                return woken;
            }
        }

        private static readonly TimeSpan TransportRetryFloor = TimeSpan.FromMilliseconds(100);

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

        public virtual async Task<FlushResult> FlushAsync(CancellationToken token)
        {
            // The first failed send ends the drain (#1747): every catch below leaves the loop,
            // and the caller is told, so that the queues behind this one do not try the same
            // server again. This is the queue's rule and holds for any sender.
            var sendFailed = false;

            Volatile.Write(ref _inFlushFlag, 1);
            try
            {
                Task cancellationFired = null;

                while (!IsEmpty && !token.IsCancellationRequested)
                {
                    // Published for the dispatch that starts on the next line, which takes it
                    // before its first await (BeginDrainSend).
                    var drainSend = new DrainSend();
                    Volatile.Write(ref _drainSend, drainSend);

                    var dispatch = TryDispatchOneAsync(token);
                    bool dispatched;

                    if (dispatch.IsCompleted)
                    {
                        // Fast path: consume the ValueTask directly — no Task allocation per
                        // drained item while the sender completes synchronously.
                        dispatched = await dispatch.ConfigureAwait(false);
                    }
                    else
                    {
                        // Shutdown must stay bounded even against an IDataSender that ignores
                        // cancellation: the run-loop side is protected by StopAsync's WhenAny
                        // timeout, but a sender that hangs for the FIRST time here would block
                        // this await forever and lock the host's restart. Race each dispatch
                        // against the flush token and give the in-flight send up when the
                        // sender does not come back: losing its items is preferable to an
                        // unbounded Stop.
                        var dispatchTask = dispatch.AsTask();

                        if (cancellationFired == null)
                            cancellationFired = Task.Delay(Timeout.Infinite, token);

                        if (await Task.WhenAny(dispatchTask, cancellationFired).ConfigureAwait(false) != dispatchTask)
                        {
                            // The budget fired with a send in flight (#1748). The same token
                            // cancelled the send, so a sender that honours cancellation is on
                            // its way back: it gets a moment, and then nothing is wrong and
                            // nothing is said. Only a sender that is still away after that
                            // moment has ignored the cancellation.
                            var graceOver = WaitBeforeAbandon(dispatchTask);

                            if (await Task.WhenAny(dispatchTask, graceOver).ConfigureAwait(false) != dispatchTask && drainSend.TryAbandon())
                            {
                                // From here the package is the drain's: the send, when it
                                // comes back, touches neither the queue nor the counts.
                                ObserveAbandonedDispatch(dispatchTask);
                                _logger.Error($"{QueueName} flush gave up a send that did not return after the stop budget cancelled it: IDataSender ignores cancellation. Its {drainSend.Items} item(s) are counted as discarded.");
                                return new FlushResult(sendFailed: false, abandoned: drainSend.Items);
                            }

                            // The send came back in time, or came back first in the race with
                            // the line above: its dispatch has the package and is settling it
                            // (no sender code is left to wait for), so the await below is short
                            // and the clear that follows this flush sees what it put back.
                        }

                        dispatched = await dispatchTask.ConfigureAwait(false);
                    }

                    if (!dispatched)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // The budget fired in the middle of a send: the drain is over for every queue
                // anyway. A sender that gave the send up without being asked has failed it.
                sendFailed = !token.IsCancellationRequested;
            }
            catch (PackageSendFailedException ex) when (ex.IsTransportDown)
            {
                // No connection to flush through; the discarded items are counted by the caller.
                sendFailed = true;
                _logger.Debug(ex.Message);
            }
            catch (Exception ex)
            {
                sendFailed = true;
                _logger.Error(ex);
            }
            finally
            {
                Volatile.Write(ref _drainSend, null);
                Volatile.Write(ref _inFlushFlag, 0);
            }

            return new FlushResult(sendFailed);
        }

        // The abandoned dispatch keeps running on the hung sender; observe its eventual fault so
        // it cannot surface as an unobserved task exception when it finally completes.
        private static void ObserveAbandonedDispatch(Task dispatchTask) =>
            _ = dispatchTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        // How long a send may take to come back once the stop budget has cancelled it. A send
        // that honours its token is back in milliseconds; this is the margin for a busy thread
        // pool, and it is what a stop can take beyond its budget, once (a spent budget starts
        // no further send). Too short a moment would only bring back a wrong Error line: who
        // gets the package is decided by DrainSend, not by this.
        private static readonly TimeSpan AbandonGrace = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// The wait before the drain gives up a send that the budget has cancelled: it is given
        /// the dispatch that is in flight and returns the task that ends the wait. A delay of
        /// <see cref="AbandonGrace"/>; tests replace it to decide themselves which comes first,
        /// the return of the send or the end of the wait.
        /// </summary>
        internal Func<Task, Task> WaitBeforeAbandon { get; set; } = _ => Task.Delay(AbandonGrace);

        // The send of the drain that is in flight; null outside a drain. Written by FlushAsync,
        // read by the dispatch it starts.
        private DrainSend _drainSend;

        /// <summary>
        /// Called by a dispatch right before it hands <paramref name="items"/> items to the
        /// sender, and before its first await: FlushAsync published the send for exactly this
        /// call. Null in the run loop, whose packages nobody gives up. (The run loop of a queue
        /// has exited before its drain begins, so it never picks up a send of the drain.)
        /// </summary>
        protected DrainSend BeginDrainSend(int items)
        {
            var drainSend = Volatile.Read(ref _drainSend);

            if (drainSend != null)
                drainSend.Items = items;

            return drainSend;
        }

        /// <summary>
        /// Called by a dispatch when its send has come back, before it settles the package in
        /// any way. False when the stop drain has given this send up: its items are counted as
        /// discarded and the queue may have been cleared and even started again since, so
        /// nothing of the package goes back into the queue, the overflow count or the package
        /// diagnostics.
        /// </summary>
        protected bool OwnsPackage(DrainSend drainSend, int items)
        {
            if (drainSend == null || drainSend.TryReturn())
                return true;

            _logger.Debug($"{QueueName}: a send the stop drain had given up has returned; its {items} item(s) were counted as discarded then and are not queued again.");

            return false;
        }

        // True while the bounded post-stop drain is running. Subclasses use this to reword
        // dispatch-failure exceptions so the log line doesn't claim items were "preserved" right
        // before ClearQueue discards them.
        protected bool IsFlushing => Volatile.Read(ref _inFlushFlag) == 1;

        /// <summary>
        /// The sender of the dispatch that is starting now: the single-attempt view inside the
        /// stop drain, the sender itself in the run loop. Read it when the send starts. The run
        /// loop of a queue has exited before its drain begins (DataProcessor flushes only a
        /// queue whose StopAsync returned true), so the two never read it at the same time.
        /// </summary>
        protected IDataSender Sender => IsFlushing ? _drainSender : _sender;

        protected async ValueTask DispatchPackageAsync(
            DataPackage<T> package,
            Func<IEnumerable<T>, CancellationToken, ValueTask<PackageSendingInfo>> send,
            CancellationToken token)
        {
            if (package == null || package.Count == 0)
                return;

            var drainSend = BeginDrainSend(package.Count);
            PackageSendingInfo sendingInfo;

            try
            {
                sendingInfo = await send(package, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (OwnsPackage(drainSend, package.Count) && PreserveCanceledPackages)
                    ReEnqueueItems(package.Items);
                throw;
            }
            catch
            {
                if (OwnsPackage(drainSend, package.Count))
                    ReEnqueueItems(package.Items);
                throw;
            }

            if (!OwnsPackage(drainSend, package.Count))
                return;

            // Items the sender could never send (a value larger than a session frame) are lost for
            // good; they are counted where every lost value is counted.
            if (sendingInfo.DroppedCount > 0)
                _queueManager?.ReportRequeueEviction(QueueName, sendingInfo.DroppedCount);

            if (sendingInfo.Error != null)
            {
                // A refusal (4xx) on a queue whose items never succeed on repeat is final: the
                // package is reported once and leaves the queue (#1659 A2). Everything else,
                // a missing answer and a 5xx included, goes back for the next cycle.
                if (RejectionIsFinal && sendingInfo.IsRejected)
                {
                    ReportRejectedPackage(DescribeItems(package), sendingInfo);
                    return;
                }

                // A sender that delivers a package in parts says which items are settled; only
                // the others go back, or what was delivered would arrive twice.
                var unsettled = Unsettled(package, sendingInfo.Settled);
                var dropped = ReEnqueueItems(unsettled);
                // The drop count is the #1088 backstop (a retry refused by a queue at capacity).
                throw new PackageSendFailedException(QueueName, unsettled.Count - dropped, dropped, IsFlushing, sendingInfo.Error, sendingInfo.IsTransportDown);
            }

            _queueManager.AddPackageSendingInfo(sendingInfo);
            _queueManager.AddPackageInfo(QueueName, package.GetInfo());
        }

        private static IReadOnlyCollection<QueueItem<T>> Unsettled(DataPackage<T> package, bool[] settled)
        {
            if (settled == null)
                return package.Items;

            var unsettled = new List<QueueItem<T>>(package.Count);
            var position = 0;

            foreach (var item in package.Items)
            {
                if (position >= settled.Length || !settled[position])
                    unsettled.Add(item);

                position++;
            }

            return unsettled;
        }

        /// <summary>
        /// True when a package the server refused with a 4xx must not be sent again. Off by
        /// default: a value batch keeps the at-least-once rule and returns to the queue on any
        /// failed send, as it does in the native collector. The command queue turns it on.
        /// </summary>
        protected virtual bool RejectionIsFinal => false;

        /// <summary>
        /// What a rejected package held, for its report: the count by default; a queue whose
        /// items carry an identity names a lone item.
        /// </summary>
        protected virtual string DescribeItems(DataPackage<T> package) => $"{package.Count} item(s)";

        private void ReportRejectedPackage(string items, PackageSendingInfo sendingInfo)
        {
            if (_queueManager != null)
                _queueManager.ReportRejectedPackage(QueueName, items, sendingInfo);
            else
                _logger.Error(DataProcessor.BuildRejectionMessage(QueueName, items, sendingInfo));
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

        // Single channel dequeue path used by GetPackage, the EnqueueCore overflow trim loop, and
        // ClearQueue. Channel<T> is thread-safe for concurrent reads, so no extra lock is needed.
        protected bool TryDequeue(out QueueItem<T> item)
        {
            return Reader.TryRead(out item);
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
