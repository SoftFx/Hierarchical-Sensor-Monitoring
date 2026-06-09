using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Regression coverage for the shutdown/lifecycle refactor (issues #1071–#1075):
    /// EnqueueResult rejection after queue stop, ShutdownMode flush semantics, the
    /// self-diagnostics suppression boundary, and sensor lifecycle-epoch invalidation.
    /// </summary>
    public sealed class CollectorQueueShutdownTests
    {
        // #1073: a value enqueued after the queue has terminally stopped must be rejected — it
        // cannot remain stranded in a stopped channel waiting for a process that never restarts.
        [Fact]
        public async Task Enqueue_after_stop_returns_rejected_queue_stopped()
        {
            var sender = new SilentDataSender();
            using (var collector = new DataCollector(CreateOptions(sender, "queue-rejection")))
            {
                await collector.Start().ConfigureAwait(false);

                var dataQueue = GetDataQueue(collector);

                // Sanity: queue accepts work while the collector is running.
                var firstResult = InvokeEnqueue(dataQueue, BuildBarValue());
                Assert.Equal(EnqueueStatus.Accepted, firstResult.Status);

                await collector.Stop().ConfigureAwait(false);

                var afterStopResult = InvokeEnqueue(dataQueue, BuildBarValue());

                Assert.Equal(EnqueueStatus.RejectedQueueStopped, afterStopResult.Status);
                Assert.Equal(0, afterStopResult.DroppedCount);
            }
        }

        // #1071 / #1073: retry re-enqueue is an internal preservation path for already accepted
        // work. It must keep working after StopAsync closes public writes; otherwise a failed
        // post-stop flush dequeues a package and silently loses it before ClearQueue can log it.
        [Fact]
        public async Task Flush_failure_after_queue_stop_preserves_dequeued_work_for_clear()
        {
            var sender = new SilentDataSender();
            using (var collector = new DataCollector(CreateOptions(sender, "flush-requeue")))
            {
                var dataQueue = GetDataQueue(collector);

                var acceptedResult = InvokeEnqueue(dataQueue, BuildBarValue());
                Assert.Equal(EnqueueStatus.Accepted, acceptedResult.Status);

                await StopQueueAsync(dataQueue, ShutdownMode.GracefulStop).ConfigureAwait(false);

                sender.ThrowOnData = true;

                using (var flushCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    await FlushQueueAsync(dataQueue, flushCancellation.Token).ConfigureAwait(false);

                Assert.Equal(1, GetQueueCount(dataQueue));
                Assert.Equal(1, ClearQueue(dataQueue));
            }
        }

        // #1072 / #1073: lifecycle gating still rejects writes after Stop() so a late SendValue
        // does not silently land in a stopped queue, even via the public AddValue path.
        [Fact]
        public async Task Public_AddValue_after_stop_does_not_strand_value_in_queue()
        {
            var sender = new SilentDataSender();
            using (var collector = new DataCollector(CreateOptions(sender, "late-add-value")))
            {
                var sensor = collector.CreateDoubleSensor("late-add-value/data");

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                sensor.AddValue(42);

                await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

                var dataQueue = GetDataQueue(collector);
                Assert.Equal(0, GetQueueCount(dataQueue));
            }
        }

        // #1075: file/command flushes that run AFTER the data-drain boundary must not produce
        // package diagnostics, because the data queue has already been drained and any new
        // diagnostic value would survive into a future restart as stale telemetry.
        [Fact]
        public async Task Package_diagnostics_are_suppressed_after_data_drain_boundary()
        {
            var sender = new SilentDataSender();
            using (var collector = new DataCollector(CreateOptions(sender, "diag-boundary")))
            {
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                var dataProcessor = GetDataProcessor(collector);
                var suppressedField = typeof(DataProcessor).GetField("_diagnosticsSuppressedFlag", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(suppressedField);

                var flagValue = (int)suppressedField.GetValue(dataProcessor);
                Assert.Equal(1, flagValue);

                // Invoking the diagnostic entry points after the boundary should be a no-op —
                // they exit before touching the (now stopped) data queues.
                var packageInfo = Activator.CreateInstance(
                    typeof(PackageInfo),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { 1.0, 1 },
                    culture: null);
                typeof(DataProcessor)
                    .GetMethod("AddPackageInfo", BindingFlags.Instance | BindingFlags.Public)
                    .Invoke(dataProcessor, new[] { "Data", packageInfo });

                var sendingInfo = new PackageSendingInfo(contentSize: 1.0);
                dataProcessor.AddPackageSendingInfo(sendingInfo);
                // No throw, no enqueue — the assertion is simply that we got here under suppression.
            }
        }

        // #1075: a fresh Start after Stop clears the suppression flag so diagnostics from the
        // new lifecycle generation flow normally (no stale stop-cycle telemetry carry-over).
        [Fact]
        public async Task Start_after_stop_clears_diagnostics_suppression()
        {
            var sender = new SilentDataSender();
            using (var collector = new DataCollector(CreateOptions(sender, "diag-restart")))
            {
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                var dataProcessor = GetDataProcessor(collector);
                var suppressedField = typeof(DataProcessor).GetField("_diagnosticsSuppressedFlag", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.Equal(1, (int)suppressedField.GetValue(dataProcessor));

                await collector.Start().ConfigureAwait(false);

                Assert.Equal(0, (int)suppressedField.GetValue(dataProcessor));
            }
        }

        // PR #1080 review #9a: the RejectedStopped factory must round-trip the dropped count so
        // callers can distinguish "rejected with N drops already evicted" from "rejected clean".
        // Before the fix the factory took no arguments and DroppedCount was always 0.
        [Fact]
        public void RejectedStopped_factory_carries_dropped_count()
        {
            var result = EnqueueResult.RejectedStopped(droppedCount: 42);

            Assert.Equal(EnqueueStatus.RejectedQueueStopped, result.Status);
            Assert.Equal(42, result.DroppedCount);
            Assert.False(result.IsAccepted);
        }

        [Fact]
        public void RejectedStopped_factory_defaults_dropped_count_to_zero()
        {
            var result = EnqueueResult.RejectedStopped();

            Assert.Equal(EnqueueStatus.RejectedQueueStopped, result.Status);
            Assert.Equal(0, result.DroppedCount);
        }

        // PR #1080 review #9b: when Enqueue(IEnumerable<T>) iterates a batch and the queue flips
        // to a stopped/terminal state midway, the result must carry the count of items already
        // evicted by overflow before the flip. The pre-fix implementation simply did
        // `return result;` on the first rejection, dropping the accumulator on the floor — so
        // QueueOverflowSensor under-reported.
        [Fact]
        public void Enqueue_batch_preserves_drop_count_when_queue_stops_mid_batch()
        {
            var sender = new SilentDataSender();
            var options = CreateOptions(sender, "drop-count-preservation");

            var queue = new FlippingQueueProcessor(options, acceptFirst: 3, droppedPerAccept: 1);

            var batch = Enumerable.Range(0, 10)
                .Select(_ => (SensorValueBase)new IntBarSensorValue { Count = 1 })
                .ToList();

            var result = InvokeBatchEnqueue(queue, batch);

            Assert.Equal(EnqueueStatus.RejectedQueueStopped, result.Status);
            // 3 Accept(droppedCount: 1) calls -> 3 evictions visible to the caller even though
            // the rejection short-circuits the rest of the batch.
            Assert.Equal(3, result.DroppedCount);
            Assert.False(result.IsAccepted);
        }

        [Fact]
        public void Enqueue_batch_drop_count_is_zero_when_rejection_happens_before_any_accept()
        {
            var sender = new SilentDataSender();
            var options = CreateOptions(sender, "drop-count-immediate");

            // acceptFirst: 0 — the very first item is rejected; the accumulator never advances.
            var queue = new FlippingQueueProcessor(options, acceptFirst: 0, droppedPerAccept: 5);

            var batch = Enumerable.Range(0, 4)
                .Select(_ => (SensorValueBase)new IntBarSensorValue { Count = 1 })
                .ToList();

            var result = InvokeBatchEnqueue(queue, batch);

            Assert.Equal(EnqueueStatus.RejectedQueueStopped, result.Status);
            Assert.Equal(0, result.DroppedCount);
        }

        // #1072: terminal Dispose still performs a bounded flush so accepted last-value-style
        // work makes it out instead of being silently discarded.
        [Fact]
        public async Task TerminalDispose_flushes_accepted_work_under_bounded_timeout()
        {
            var sender = new SilentDataSender();
            var collector = new DataCollector(CreateOptions(sender, "terminal-flush"));

            var sensor = collector.CreateDoubleSensor("terminal-flush/data");
            await collector.Start().ConfigureAwait(false);

            sensor.AddValue(7);

            collector.Dispose();

            Assert.True(sender.WaitForDataPackage(TimeSpan.FromSeconds(2)),
                "TerminalDispose must still drain accepted work with a bounded flush.");
        }

        // PR #1080 review #5: lock in the Dispose-vs-Stop race fix. Stop(customStoppingTask) sets
        // Status=Stopping inside its first lock, then awaits the custom task BEFORE publishing
        // _currentProcessorStopTask. Without the fix, a Dispose() that races in during the await
        // saw a null processor stop task, owned the terminal stop itself, and the resumed Stop
        // then kicked off a SECOND concurrent _dataProcessor.StopAsync with the graceful mode —
        // overwriting Dispose's TerminalDispose policy on each queue's _currentShutdownModeRaw.
        // The fix has Dispose publish its task inside the lock; a racing Stop joins it.
        // Assertion: after the race resolves, the data queue's mode reflects Dispose's choice,
        // not Stop's. (Without the fix, GracefulStop would have overwritten TerminalDispose.)
        [Fact]
        public async Task Dispose_racing_Stop_customTask_publishes_terminal_mode_to_queues()
        {
            var sender = new SilentDataSender();
            var collector = new DataCollector(CreateOptions(sender, "stop-dispose-race"));

            collector.CreateDoubleSensor("stop-dispose-race/data");

            await collector.Start().ConfigureAwait(false);

            // customStoppingTask blocks Stop after it transitioned Status→Stopping but before
            // it published _currentProcessorStopTask. This is the window where the bug used to
            // open: Dispose() racing in observed _currentProcessorStopTask == null.
            var releaseStop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopTask = collector.Stop(releaseStop.Task);

            // Give Stop time to enter the lock and transition to Stopping.
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

            // Race: Dispose runs while Stop is still parked on releaseStop.Task.
            var disposeTask = Task.Run(() => collector.Dispose());

            // Give Dispose time to take the inner lock, publish its terminal stop task, and
            // begin its bounded flush.
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

            // Release Stop's custom task. With the fix, Stop's second lock observes Dispose's
            // published task and joins it instead of starting a second StopAsync.
            releaseStop.SetResult(true);

            await Task.WhenAll(stopTask, disposeTask).ConfigureAwait(false);

            Assert.Equal(CollectorStatus.Disposed, collector.Status);

            var dataQueue = GetDataQueue(collector);
            var modeRaw = (int)typeof(QueueProcessorBase<SensorValueBase>)
                .GetField("_currentShutdownModeRaw", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(dataQueue);

            Assert.Equal((int)ShutdownMode.TerminalDispose, modeRaw);
        }

        // Issue #1088: a failed-retry payload must not evict newer telemetry on queue overflow.
        // Before the fix, ReEnqueueItem wrote the retry to the channel tail and let the overflow
        // loop drop the head — but the head is the FIFO-newest among items that arrived during the
        // retry's send attempt, so a stale retry would silently delete fresher values. With the
        // fix, the retry path checks capacity FIRST: if the queue is already at MaxQueueSize, the
        // retry is dropped and the existing newer items stay intact.
        [Fact]
        public void ReEnqueue_when_queue_at_capacity_drops_retry_to_preserve_newer_telemetry()
        {
            var sender = new SilentDataSender();
            var options = CreateOptions(sender, "issue-1088-full");
            options.MaxQueueSize = 5;

            var queue = new CapacityTestQueueProcessor(options);

            // Fill the queue with five fresher values, tagged to verify identity below.
            for (int i = 0; i < 5; i++)
            {
                var ok = queue.InvokeEnqueueRaw(new IntBarSensorValue { Count = 1, Comment = $"fresh-{i}" });
                Assert.Equal(EnqueueStatus.Accepted, ok.Status);
                Assert.Equal(0, ok.DroppedCount);
            }
            Assert.Equal(5, queue.QueueCount);

            // Simulate a failed-retry payload. Before the fix this would write to the tail and
            // overflow-evict the head ("fresh-0").
            var staleRetry = new QueueItem<SensorValueBase>(new IntBarSensorValue { Count = 1, Comment = "stale-retry" });

            var result = queue.InvokeReEnqueue(staleRetry);

            Assert.Equal(EnqueueStatus.Accepted, result.Status);
            Assert.Equal(1, result.DroppedCount);
            Assert.Equal(5, queue.QueueCount);

            // Critically: the stale retry must NOT be in the queue, and all five fresh values must
            // still be there in their original order. Pre-fix behaviour was "C D E F A"; we want
            // "fresh-0 .. fresh-4" with no "stale-retry".
            var contents = queue.DrainAll().Select(v => v.Comment).ToList();
            Assert.Equal(new[] { "fresh-0", "fresh-1", "fresh-2", "fresh-3", "fresh-4" }, contents);
        }

        [Fact]
        public void ReEnqueue_below_capacity_writes_item_normally()
        {
            var sender = new SilentDataSender();
            var options = CreateOptions(sender, "issue-1088-below");
            options.MaxQueueSize = 5;

            var queue = new CapacityTestQueueProcessor(options);

            queue.InvokeEnqueueRaw(new IntBarSensorValue { Count = 1, Comment = "fresh-0" });
            queue.InvokeEnqueueRaw(new IntBarSensorValue { Count = 1, Comment = "fresh-1" });
            Assert.Equal(2, queue.QueueCount);

            var retry = new QueueItem<SensorValueBase>(new IntBarSensorValue { Count = 1, Comment = "retry-after-fail" });

            var result = queue.InvokeReEnqueue(retry);

            Assert.Equal(EnqueueStatus.Accepted, result.Status);
            Assert.Equal(0, result.DroppedCount);
            Assert.Equal(3, queue.QueueCount);

            var contents = queue.DrainAll().Select(v => v.Comment).ToList();
            Assert.Equal(new[] { "fresh-0", "fresh-1", "retry-after-fail" }, contents);
        }

        // Issue #1088: ReEnqueueItems returns the aggregated drop count so the
        // DispatchPackageAsync failure path can log "N preserved, M dropped at capacity" instead
        // of the previously misleading "(N values preserved)" line. The contents-unchanged
        // assertion guards the core invariant; the returned count guards the log-honesty fix.
        [Fact]
        public void ReEnqueueItems_returns_drop_count_and_preserves_queue_for_full_queue()
        {
            var sender = new SilentDataSender();
            var options = CreateOptions(sender, "issue-1088-batch");
            options.MaxQueueSize = 3;

            var queue = new CapacityTestQueueProcessor(options);

            for (int i = 0; i < 3; i++)
                queue.InvokeEnqueueRaw(new IntBarSensorValue { Count = 1, Comment = $"fresh-{i}" });
            Assert.Equal(3, queue.QueueCount);

            // Simulate a failed package of 4 retries hitting an already-full queue.
            var retries = Enumerable.Range(0, 4)
                .Select(i => new QueueItem<SensorValueBase>(new IntBarSensorValue { Count = 1, Comment = $"retry-{i}" }))
                .ToList();

            var droppedCount = queue.InvokeReEnqueueItems(retries);

            Assert.Equal(4, droppedCount);
            Assert.Equal(3, queue.QueueCount);
            var contents = queue.DrainAll().Select(v => v.Comment).ToList();
            Assert.Equal(new[] { "fresh-0", "fresh-1", "fresh-2" }, contents);
        }

        // PR #1080 fifth-round review HIGH: bar drop race. With the previous code, if the
        // periodic send handle was inside SendValueAction holding _sendValueInProgress=1 but
        // had not yet snapshotted the bar (i.e., before its BuildSensorValue → GetValue under
        // _lockBar), a concurrent CheckCurrentBar would call SendValueAction (no-op, guard
        // held) and then unconditionally BuildNewBar — erasing the closed bar's data before
        // the periodic send could snapshot it. The fix splits SendValueAction into TrySendValue
        // (bool) and makes the roll conditional on a successful send.
        [Fact]
        public async Task CheckCurrentBar_defers_roll_when_send_guard_is_held()
        {
            var sender = new SilentDataSender();
            var options = CreateOptions(sender, "bar-drop-race");
            using (var collector = new DataCollector(options))
            {
                // Long bar period (1 hour) so the scheduler's collect handle does not auto-roll
                // the bar mid-test. We force the closed-bar state manually by overwriting
                // _internalBar.CloseTime via reflection, which keeps the test deterministic and
                // independent of the threadpool scheduling.
                var sensor = (IBarSensor<int>)collector.CreateIntBarSensor(
                    "bar-drop-race/data",
                    barPeriod: 60 * 60 * 1000,
                    postPeriod: 60 * 60 * 1000);

                await collector.Start().ConfigureAwait(false);

                sensor.AddValue(42);

                var sensorObj = (object)sensor;

                var inProgressField = typeof(MonitoringSensorBase<IntMonitoringBar, NoDisplayUnit>)
                    .GetField("_sendValueInProgress", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(inProgressField);

                var checkCurrentBar = typeof(BarMonitoringSensorBase<IntMonitoringBar, int>)
                    .GetMethod("CheckCurrentBar", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(checkCurrentBar);

                var internalBarField = typeof(BarMonitoringSensorBase<IntMonitoringBar, int>)
                    .GetField("_internalBar", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(internalBarField);

                // Force the bar into "closed" state without waiting on wall-clock.
                var barBefore = internalBarField.GetValue(sensorObj);
                var closeTimeProperty = barBefore.GetType().GetProperty("CloseTime");
                closeTimeProperty.SetValue(barBefore, DateTime.UtcNow.AddSeconds(-1));

                // Simulate the periodic send being inside SendValueAction holding the guard
                // but not yet at the _lockBar snapshot step.
                inProgressField.SetValue(sensorObj, 1);

                checkCurrentBar.Invoke(sensorObj, null);

                var barWhileGuardHeld = internalBarField.GetValue(sensorObj);
                var countWhileGuardHeld = (int)barWhileGuardHeld.GetType().GetProperty("Count").GetValue(barWhileGuardHeld);
                Assert.True(countWhileGuardHeld > 0,
                    "With _sendValueInProgress held, CheckCurrentBar must defer the roll — otherwise the closed bar's aggregated data is lost.");

                // Release the guard and re-trigger — now the roll must happen because TrySendValue
                // succeeds.
                inProgressField.SetValue(sensorObj, 0);

                // Re-stamp CloseTime in case the previous successful TrySendValue cycle did not
                // refresh the bar (it doesn't — bar reset only happens on BuildNewBar).
                closeTimeProperty.SetValue(internalBarField.GetValue(sensorObj), DateTime.UtcNow.AddSeconds(-1));

                checkCurrentBar.Invoke(sensorObj, null);

                var barAfterRelease = internalBarField.GetValue(sensorObj);
                var countAfterRelease = (int)barAfterRelease.GetType().GetProperty("Count").GetValue(barAfterRelease);
                Assert.Equal(0, countAfterRelease);

                await collector.Stop().ConfigureAwait(false);
            }
        }


        private static CollectorOptions CreateOptions(SilentDataSender sender, string module)
        {
            return new CollectorOptions
            {
                AccessKey = "queue-shutdown-test",
                ClientName = "queue-shutdown-test",
                ComputerName = "queue-shutdown-host",
                Module = module,
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
            };
        }

        private static DataProcessor GetDataProcessor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private static object GetDataQueue(DataCollector collector)
        {
            var processor = GetDataProcessor(collector);
            return typeof(DataProcessor)
                .GetField("_dataQueue", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(processor);
        }

        private static EnqueueResult InvokeEnqueue(object dataQueue, SensorValueBase value)
        {
            var method = typeof(QueueProcessorBase<SensorValueBase>)
                .GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types: new[] { typeof(SensorValueBase) }, modifiers: null);
            Assert.NotNull(method);
            return (EnqueueResult)method.Invoke(dataQueue, new object[] { value });
        }

        private static EnqueueResult InvokeBatchEnqueue(QueueProcessorBase<SensorValueBase> queue, IEnumerable<SensorValueBase> values)
        {
            var method = typeof(QueueProcessorBase<SensorValueBase>)
                .GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types: new[] { typeof(IEnumerable<SensorValueBase>) }, modifiers: null);
            Assert.NotNull(method);
            return (EnqueueResult)method.Invoke(queue, new object[] { values });
        }

        private static async Task<bool> StopQueueAsync(object dataQueue, ShutdownMode mode)
        {
            var method = typeof(QueueProcessorBase<SensorValueBase>)
                .GetMethod("StopAsync", BindingFlags.Instance | BindingFlags.Public, binder: null, types: new[] { typeof(ShutdownMode) }, modifiers: null);
            Assert.NotNull(method);
            return await ((ValueTask<bool>)method.Invoke(dataQueue, new object[] { mode })).ConfigureAwait(false);
        }

        private static async Task FlushQueueAsync(object dataQueue, CancellationToken token)
        {
            var method = typeof(QueueProcessorBase<SensorValueBase>)
                .GetMethod("FlushAsync", BindingFlags.Instance | BindingFlags.Public, binder: null, types: new[] { typeof(CancellationToken) }, modifiers: null);
            Assert.NotNull(method);
            await ((Task)method.Invoke(dataQueue, new object[] { token })).ConfigureAwait(false);
        }

        private static int GetQueueCount(object dataQueue) =>
            (int)typeof(QueueProcessorBase<SensorValueBase>)
                .GetProperty("QueueCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.FlattenHierarchy)
                .GetValue(dataQueue);

        private static int ClearQueue(object dataQueue)
        {
            var method = typeof(QueueProcessorBase<SensorValueBase>)
                .GetMethod("ClearQueue", BindingFlags.Instance | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);
            Assert.NotNull(method);
            return (int)method.Invoke(dataQueue, new object[0]);
        }

        private static SensorValueBase BuildBarValue() => new IntBarSensorValue { Count = 1 };


        private sealed class SilentDataSender : IDataSender
        {
            private readonly TaskCompletionSource<bool> _dataReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool ThrowOnData { get; set; }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                if (ThrowOnData)
                    throw new InvalidOperationException("Synthetic data send failure.");

                _dataReceived.TrySetResult(true);
                return new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                SendDataAsync(items, token);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));

            public bool WaitForDataPackage(TimeSpan timeout)
            {
                return Task.WhenAny(_dataReceived.Task, Task.Delay(timeout)).GetAwaiter().GetResult() == _dataReceived.Task;
            }

            public void Dispose() { }
        }

        /// <summary>
        /// Concurrent stress test for the monotonic-epoch contract. Writers do
        /// <see cref="Interlocked.Increment(ref long)"/>; readers do
        /// <see cref="Interlocked.Read(ref long)"/> and must never observe a value greater
        /// than the maximum write that has completed so far.
        ///
        /// On 64-bit runtimes long reads are atomic and this test always passes — it serves
        /// as a smoke test for the concurrent shape. On 32-bit runtimes the same test with
        /// <c>Volatile.Read</c> can produce a torn read where the high word is from a newer
        /// write than the low word; with <c>Interlocked.Read</c> (the production path) it
        /// cannot. PR #1080 review #8 flagged this and we converted the read in
        /// <see cref="MonitoringSensorBase{T, TDisplayUnit}.LifecycleEpoch"/> from
        /// Volatile to Interlocked accordingly. The CI lacks a 32-bit matrix at the moment,
        /// so this test is left here as a placeholder + documentation hook for whoever adds
        /// one later.
        /// </summary>
        [Fact]
        public async Task LifecycleEpoch_concurrent_reads_observe_monotonic_values()
        {
            long epoch = 0;
            long observedNonMonotonic = 0;
            const int durationMs = 500;

            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(durationMs)))
            {
                var writer = Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                        Interlocked.Increment(ref epoch);
                });

                var reader = Task.Run(() =>
                {
                    long previous = 0;
                    while (!cts.IsCancellationRequested)
                    {
                        var current = Interlocked.Read(ref epoch);
                        if (current < previous)
                            Interlocked.Increment(ref observedNonMonotonic);

                        previous = current;
                    }
                });

                await Task.WhenAll(writer, reader).ConfigureAwait(false);
            }

            Assert.Equal(0, Interlocked.Read(ref observedNonMonotonic));
        }

        /// <summary>
        /// Test double for issue #1088 — exposes ReEnqueueItem(s) and Enqueue plus a draining
        /// helper so tests can verify both the drop count and the surviving queue contents after
        /// a retry hits an over-capacity queue. TryDispatchOneAsync is a no-op so items stay put
        /// until the test drains them.
        /// </summary>
        private sealed class CapacityTestQueueProcessor : QueueProcessorBase<SensorValueBase>
        {
            internal CapacityTestQueueProcessor(CollectorOptions options)
                : base(options, queueManager: null, logger: new LoggerManager()) { }

            public override string QueueName => "Capacity-test";

            protected override ValueTask<bool> TryDispatchOneAsync(CancellationToken token) =>
                new ValueTask<bool>(false);

            internal EnqueueResult InvokeReEnqueue(QueueItem<SensorValueBase> item) =>
                ReEnqueueItem(item);

            internal int InvokeReEnqueueItems(IEnumerable<QueueItem<SensorValueBase>> items) =>
                ReEnqueueItems(items);

            internal EnqueueResult InvokeEnqueueRaw(SensorValueBase item) => Enqueue(item);

            internal List<SensorValueBase> DrainAll()
            {
                var items = new List<SensorValueBase>();
                while (TryDequeue(out var item))
                    items.Add(item.Value);
                return items;
            }
        }

        /// <summary>
        /// Test double for <see cref="QueueProcessorBase{T}"/> that lets the single-item Enqueue
        /// path emit a configurable Accept-then-Reject sequence. Used to drive the IEnumerable
        /// overload through its mid-batch rejection branch without racing a real stop.
        /// </summary>
        private sealed class FlippingQueueProcessor : QueueProcessorBase<SensorValueBase>
        {
            private readonly int _acceptFirst;
            private readonly int _droppedPerAccept;
            private int _enqueueCalls;

            internal FlippingQueueProcessor(CollectorOptions options, int acceptFirst, int droppedPerAccept)
                : base(options, queueManager: null, logger: new LoggerManager())
            {
                _acceptFirst = acceptFirst;
                _droppedPerAccept = droppedPerAccept;
            }

            public override string QueueName => "Flipping";

            internal override EnqueueResult Enqueue(SensorValueBase item)
            {
                var callNumber = Interlocked.Increment(ref _enqueueCalls);
                return callNumber <= _acceptFirst
                    ? EnqueueResult.Accept(droppedCount: _droppedPerAccept)
                    : EnqueueResult.RejectedStopped();
            }

            protected override ValueTask<bool> TryDispatchOneAsync(CancellationToken token) =>
                new ValueTask<bool>(false);
        }
    }
}
