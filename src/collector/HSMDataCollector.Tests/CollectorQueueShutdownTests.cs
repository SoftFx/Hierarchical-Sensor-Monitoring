using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
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
    }
}
