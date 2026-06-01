using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorStabilityTests
    {
        private readonly ITestOutputHelper _output;

        public CollectorStabilityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Bug: CollectorScheduler.Loop() only catches OperationCanceledException.
        /// Any other exception terminates the scheduler thread silently,
        /// and all timers stop firing forever.
        /// </summary>
        [Fact]
        public async Task Scheduler_loop_survives_unexpected_exception()
        {
            var sender = new StabilityDataSender();
            using (var collector = CreateCollector(sender, "scheduler-resilience"))
            {
                await collector.Start().ConfigureAwait(false);

                // Schedule a sensor whose callback throws.
                // The scheduler's Loop() method must not die.
                var throwCount = 0;
                collector.CreateFunctionSensor<int>(
                    "stability/throwing",
                    () => { Interlocked.Increment(ref throwCount); throw new InvalidOperationException("boom"); },
                    new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(50) });

                // Give the throwing sensor time to fire and potentially kill the scheduler.
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                // Now schedule a normal sensor and verify the scheduler is still alive.
                var goodCount = 0;
                collector.CreateFunctionSensor(
                    "stability/normal",
                    () => Interlocked.Increment(ref goodCount),
                    new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(50) });

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                await collector.Stop().ConfigureAwait(false);

                Assert.True(throwCount > 0, "The throwing sensor should have fired at least once.");
                Assert.True(goodCount > 0, "The normal sensor should fire after the throwing sensor ran. " +
                    "If this fails, the scheduler loop terminated on the unexpected exception.");
            }
        }

        /// <summary>
        /// Bug: DoubleMonitoringBar.CountAvr uses wrong operator precedence.
        /// 'first + second / 2' computes 'first + (second / 2)' instead of '(first + second) / 2'.
        /// IntMonitoringBar has the correct formula: '(first + second) / 2'.
        /// </summary>
        [Fact]
        public void DoubleMonitoringBar_CountAvr_computes_correct_average()
        {
            var bar = new DoubleMonitoringBar();

            var method = typeof(DoubleMonitoringBar).GetMethod(
                "CountAvr",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);

            var result = (double)method.Invoke(bar, new object[] { 10.0, 20.0 });

            // Expected: (10 + 20) / 2 = 15.0
            // Buggy:    10 + 20 / 2   = 20.0
            Assert.Equal(15.0, result);
        }

        /// <summary>
        /// Bug: SensorsStorage.Register calls '_ = AddAndStart(sensor)' when IsStarted is true.
        /// The fire-and-forget task discards exceptions, creating unobserved task exceptions.
        /// </summary>
        [Fact]
        public async Task Register_after_start_does_not_create_unobserved_task_exception()
        {
            var sender = new StabilityDataSender { ThrowOnCommand = true };
            var unobservedException = false;

            EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
            {
                unobservedException = true;
                e.SetObserved();
            };

            TaskScheduler.UnobservedTaskException += handler;

            try
            {
                using (var collector = CreateCollector(sender, "unobserved-exception"))
                {
                    await collector.Start().ConfigureAwait(false);

                    // Creating a sensor after start triggers the fire-and-forget AddAndStart path.
                    // InitAsync sends a command via the sender, which throws.
                    collector.CreateFunctionSensor(
                        "stability/init-fail",
                        () => 42,
                        new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(100) });

                    // Give the fire-and-forget task time to fault.
                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                }

                // Force GC to trigger finalizer that raises UnobservedTaskException.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Assert.False(unobservedException,
                    "Fire-and-forget task in SensorsStorage.Register should not leak unobserved exceptions.");
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        /// <summary>
        /// Verifies that the processing loop recovers after a send failure.
        /// Data from failed sends is lost (acceptable by design — GetPackage() dequeues
        /// before SendDataAsync), but the loop must continue and deliver data enqueued
        /// after the failure.
        /// </summary>
        [Fact]
        public async Task Processing_loop_recovers_after_send_failure()
        {
            var sender = new StabilityDataSender { FailFirstNSends = 1 };
            using (var collector = CreateCollector(sender, "send-failure-retry"))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.CreateDoubleSensor(
                    "stability/send-retry",
                    new InstantSensorOptions());

                // First batch — will be dequeued and lost when SendDataAsync throws.
                for (int i = 0; i < 5; i++)
                    sensor.AddValue(i);

                // Wait for the failed send to occur.
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                // Second batch — should be delivered after the failure mode clears.
                for (int i = 5; i < 10; i++)
                    sensor.AddValue(i);

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                await collector.Stop().ConfigureAwait(false);

                _output.WriteLine(
                    "sendFailure; totalSentValues={0}; sendCalls={1}; failedSends={2}",
                    sender.TotalDataValuesSent,
                    sender.DataSendCalls,
                    sender.FailedSends);

                Assert.True(sender.FailedSends >= 1,
                    "Expected at least one failed send to occur.");

                Assert.True(sender.TotalDataValuesSent >= 5,
                    $"Expected >= 5 values delivered after send failure recovery, got {sender.TotalDataValuesSent}. " +
                    "The processing loop should recover and deliver data enqueued after the failure.");
            }
        }

        /// <summary>
        /// Bug: GetPackage() dequeues items, filters by Validate(), and returns the filtered list.
        /// If all items fail validation, an empty collection is sent to the server.
        /// </summary>
        [Fact]
        public async Task Empty_package_not_sent_when_all_items_fail_validation()
        {
            var sender = new StabilityDataSender();
            using (var collector = CreateCollector(sender, "empty-package"))
            {
                await collector.Start().ConfigureAwait(false);

                // Get the data queue directly and enqueue items that will fail validation.
                // BarSensorValueBase with Count <= 0 is rejected by Validate().
                var dataProcessor = (DataProcessor)typeof(DataCollector)
                    .GetField("_dataProcessor", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(collector);

                var dataQueue = (DataQueueProcessor)typeof(DataProcessor)
                    .GetField("_dataQueue", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(dataProcessor);

                for (int i = 0; i < 5; i++)
                    dataQueue.Enqeue(new IntBarSensorValue { Count = 0 });

                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

                await collector.Stop().ConfigureAwait(false);

                Assert.False(sender.ReceivedEmptyDataPackage,
                    "Sender should never receive an empty data package. " +
                    "GetPackage() dequeues items that all fail Validate(), resulting in an empty send.");
            }
        }

        /// <summary>
        /// Bug: DefaultSensorsCollection.Dispose() calls QueueOverflowSensor.Dispose() and
        /// CollectorErrors.Dispose() without null-conditional operator, while other sensors use '?.'
        /// If these sensors were never added, Dispose throws NullReferenceException.
        /// </summary>
        [Fact]
        public void DefaultSensorsCollection_Dispose_does_not_throw_on_partial_registration()
        {
            // Create a collection and call Dispose without registering all sensors.
            // QueueOverflowSensor and CollectorErrors are null by default.
            var collection = CreatePartialDefaultSensorsCollection();

            // Should not throw NullReferenceException.
            collection.Dispose();
        }

        /// <summary>
        /// Bug: _queueCount is tracked manually alongside ConcurrentQueue with Interlocked.
        /// Under concurrent enqueue + GetPackage drain, the count can diverge from actual queue size.
        /// The overflow trimming in Enqeue is not atomic with the dequeue, causing over-trimming.
        /// </summary>
        [Fact]
        public async Task Queue_count_stays_consistent_under_concurrent_access()
        {
            var sender = new StabilityDataSender { DataSendDelay = TimeSpan.FromMilliseconds(10) };
            var options = CreateOptions(sender, "queue-count");
            options.MaxQueueSize = 50;
            options.MaxValuesInPackage = 10;

            using (var collector = new DataCollector(options))
            {
                await collector.Start().ConfigureAwait(false);

                // Flood with values from multiple threads.
                var sensor = collector.CreateDoubleSensor(
                    "stability/queue-flood",
                    new InstantSensorOptions());

                var tasks = new Task[8];
                for (int t = 0; t < tasks.Length; t++)
                {
                    tasks[t] = Task.Run(() =>
                    {
                        for (int i = 0; i < 200; i++)
                            sensor.AddValue(i);
                    });
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);

                // Let the queue drain.
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                await collector.Stop().ConfigureAwait(false);

                // After stop, the queue should be drained. Get the queue count.
                var dataProcessor = (DataProcessor)typeof(DataCollector)
                    .GetField("_dataProcessor", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(collector);

                var dataQueue = (DataQueueProcessor)typeof(DataProcessor)
                    .GetField("_dataQueue", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(dataProcessor);

                var queueCount = typeof(QueueProcessorBase<SensorValueBase>)
                    .GetProperty("QueueCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty | BindingFlags.FlattenHierarchy)
                    .GetValue(dataQueue);

                _output.WriteLine("queueCount after drain = {0}", queueCount);

                Assert.True((int)queueCount >= 0,
                    $"Queue count should never be negative, got {queueCount}. " +
                    "Manual _queueCount tracking diverges from actual ConcurrentQueue state.");
            }
        }


        private static DataCollector CreateCollector(StabilityDataSender sender, string module)
        {
            return new DataCollector(CreateOptions(sender, module));
        }

        private static CollectorOptions CreateOptions(StabilityDataSender sender, string module)
        {
            return new CollectorOptions
            {
                AccessKey = "stability-test-key",
                ClientName = "stability-test-client",
                ComputerName = "stability-test-host",
                Module = module,
                DataSender = sender,
                MaxQueueSize = 10000,
                MaxValuesInPackage = 100,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(5),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 200
            };
        }

        private static DefaultSensorsCollection CreatePartialDefaultSensorsCollection()
        {
            // Create a SensorsStorage (internal) with minimal options.
            var sender = new StabilityDataSender();
            var options = CreateOptions(sender, "partial-default");

            var loggerField = typeof(DataCollector).GetField("_logger",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // Use reflection to construct a DefaultSensorsCollection subclass.
            // WindowsSensorsCollection or UnixSensorsCollection are the concrete types.
            // Since we can't easily construct them, create a test subclass.
            return new TestDefaultSensorsCollection();
        }

        /// <summary>
        /// Test-only IDataSender with configurable failure modes.
        /// </summary>
        private sealed class StabilityDataSender : IDataSender
        {
            private int _dataSendCalls;
            private int _failedSends;
            private int _totalDataValuesSent;
            private int _commandSendCalls;
            private bool _receivedEmptyDataPackage;

            public bool ThrowOnCommand { get; set; }
            public bool ThrowOnData { get; set; }
            public int FailFirstNSends { get; set; }
            public TimeSpan DataSendDelay { get; set; }

            public int DataSendCalls => Volatile.Read(ref _dataSendCalls);
            public int FailedSends => Volatile.Read(ref _failedSends);
            public int TotalDataValuesSent => Volatile.Read(ref _totalDataValuesSent);
            public int CommandSendCalls => Volatile.Read(ref _commandSendCalls);
            public bool ReceivedEmptyDataPackage => Volatile.Read(ref _receivedEmptyDataPackage);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync()
                => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(
                IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                var itemList = items?.ToList() ?? new List<SensorValueBase>();

                if (itemList.Count == 0)
                    Volatile.Write(ref _receivedEmptyDataPackage, true);

                return SendWithFailureModeAsync(itemList, token);
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(
                IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                return SendDataAsync(items, token);
            }

            private async ValueTask<PackageSendingInfo> SendWithFailureModeAsync(
                List<SensorValueBase> itemList, CancellationToken token)
            {
                Interlocked.Increment(ref _dataSendCalls);

                if (DataSendDelay != TimeSpan.Zero)
                    await Task.Delay(DataSendDelay, token).ConfigureAwait(false);

                if (ThrowOnData)
                    throw new InvalidOperationException("Sender configured to throw on data.");

                if (FailFirstNSends > 0)
                {
                    var failed = Interlocked.Increment(ref _failedSends);
                    if (failed <= FailFirstNSends)
                        throw new InvalidOperationException($"Simulated send failure #{failed}");
                }

                Interlocked.Add(ref _totalDataValuesSent, itemList.Count);

                return default(PackageSendingInfo);
            }

            public ValueTask<PackageSendingInfo> SendCommandAsync(
                IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                Interlocked.Increment(ref _commandSendCalls);

                if (ThrowOnCommand)
                    throw new InvalidOperationException("Sender configured to throw on commands.");

                return new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));
            }

            public ValueTask<PackageSendingInfo> SendFileAsync(
                FileSensorValue file, CancellationToken token)
            {
                return new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));
            }
        }

        /// <summary>
        /// Minimal DefaultSensorsCollection subclass for testing Dispose without full registration.
        /// </summary>
        private sealed class TestDefaultSensorsCollection : DefaultSensorsCollection
        {
            public TestDefaultSensorsCollection()
                : base(null, null)
            {
            }

            protected override bool IsCorrectOs => false;
        }
    }
}
