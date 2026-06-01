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
        /// Bug: GetPackage() dequeues items from the queue before calling SendDataAsync.
        /// If SendDataAsync throws a non-cancellation exception, those items are permanently lost.
        /// </summary>
        [Fact]
        public async Task Send_failure_preserves_dequeued_values()
        {
            var sender = new StabilityDataSender { FailFirstNSends = 2 };
            using (var collector = CreateCollector(sender, "send-failure-retry"))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.CreateDoubleSensor(
                    "stability/send-retry",
                    new InstantSensorOptions());

                for (int i = 0; i < 10; i++)
                    sensor.AddValue(i);

                // Wait long enough for failed sends + retries + successful sends.
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                await collector.Stop().ConfigureAwait(false);

                _output.WriteLine(
                    "sendFailure; totalSentValues={0}; sendCalls={1}; failedSends={2}",
                    sender.TotalDataValuesSent,
                    sender.DataSendCalls,
                    sender.FailedSends);

                // All 10 values should eventually be delivered, even though first sends failed.
                Assert.True(sender.TotalDataValuesSent >= 10,
                    $"Expected >= 10 values delivered after send failures, got {sender.TotalDataValuesSent}. " +
                    "Items lost because GetPackage() dequeues before SendDataAsync.");
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

        [Fact]
        public async Task Transient_command_send_failure_retries_registration_command()
        {
            var sender = new StabilityDataSender { FailFirstNCommandSends = 1 };
            using (var collector = CreateCollector(sender, "command-send-retry"))
            {
                await collector.Start().ConfigureAwait(false);

                collector.CreateDoubleSensor("stability/command-retry");

                Assert.True(
                    await sender.WaitForCommandRequestsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "A transient command send failure should not permanently drop the sensor registration command.");

                await collector.Stop().ConfigureAwait(false);
            }

            Assert.True(sender.CommandSendCalls >= 2, "The command queue should retry after the first failed send.");
        }

        [Fact]
        public async Task Transient_file_send_failure_retries_file_payload()
        {
            var sender = new StabilityDataSender { FailFirstNFileSends = 1 };
            var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            System.IO.File.WriteAllText(filePath, "payload");

            try
            {
                using (var collector = CreateCollector(sender, "file-send-retry"))
                {
                    await collector.Start().ConfigureAwait(false);

                    Assert.True(await collector.SendFileAsync("stability/file-retry", filePath).ConfigureAwait(false));
                    Assert.True(
                        await sender.WaitForFileRequestsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                        "A transient file send failure should not permanently drop an accepted file payload.");

                    await collector.Stop().ConfigureAwait(false);
                }
            }
            finally
            {
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            Assert.True(sender.FileSendCalls >= 2, "The file queue should retry after the first failed send.");
        }

        [Fact]
        public async Task Failed_package_sending_info_retries_dequeued_values()
        {
            var sender = new StabilityDataSender { FailFirstNDataResults = 1 };
            using (var collector = CreateCollector(sender, "failed-result-retry"))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.CreateDoubleSensor(
                    "stability/failed-result-retry",
                    new InstantSensorOptions());

                for (var i = 0; i < 5; i++)
                    sensor.AddValue(i);

                Assert.True(
                    await sender.WaitForDataValuesAsync(5, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "A failed PackageSendingInfo result should not permanently drop dequeued values.");

                await collector.Stop().ConfigureAwait(false);
            }

            Assert.True(sender.DataSendCalls >= 2, "The data queue should retry after an explicit failed send result.");
        }

        [Fact]
        public async Task Failed_command_package_sending_info_retries_registration_command()
        {
            var sender = new StabilityDataSender { FailFirstNCommandResults = 1 };
            using (var collector = CreateCollector(sender, "failed-command-result-retry"))
            {
                await collector.Start().ConfigureAwait(false);

                collector.CreateDoubleSensor("stability/failed-command-result-retry");

                Assert.True(
                    await sender.WaitForCommandRequestsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "A failed PackageSendingInfo result should not permanently drop registration commands.");

                await collector.Stop().ConfigureAwait(false);
            }

            Assert.True(sender.CommandSendCalls >= 2, "The command queue should retry after an explicit failed send result.");
        }

        [Fact]
        public async Task Failed_file_package_sending_info_retries_file_payload()
        {
            var sender = new StabilityDataSender { FailFirstNFileResults = 1 };
            var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            System.IO.File.WriteAllText(filePath, "payload");

            try
            {
                using (var collector = CreateCollector(sender, "failed-file-result-retry"))
                {
                    await collector.Start().ConfigureAwait(false);

                    Assert.True(await collector.SendFileAsync("stability/failed-file-result-retry", filePath).ConfigureAwait(false));
                    Assert.True(
                        await sender.WaitForFileRequestsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                        "A failed PackageSendingInfo result should not permanently drop an accepted file payload.");

                    await collector.Stop().ConfigureAwait(false);
                }
            }
            finally
            {
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            Assert.True(sender.FileSendCalls >= 2, "The file queue should retry after an explicit failed send result.");
        }

        [Fact]
        public async Task Values_function_sensor_cache_stays_bounded_under_concurrent_producers()
        {
            const int maxCacheSize = 1;
            var sender = new StabilityDataSender();
            var maxObservedCount = 0;

            using (var collector = CreateCollector(sender, "values-function-cache-bound"))
            {
                var sensor = collector.CreateValuesFunctionSensor<int, int>(
                    "stability/values-function-cache-bound",
                    values =>
                    {
                        var count = values.Count;
                        UpdateMax(ref maxObservedCount, count);
                        return count;
                    },
                    new ValuesFunctionSensorOptions
                    {
                        MaxCacheSize = maxCacheSize,
                        PostDataPeriod = TimeSpan.FromMilliseconds(1),
                    });

                await collector.Start().ConfigureAwait(false);

                var producers = Enumerable.Range(0, 32)
                    .Select(worker => Task.Run(() =>
                    {
                        for (var i = 0; i < 20000; i++)
                            sensor.AddValue(worker * 20000 + i);
                    }))
                    .ToArray();

                await Task.WhenAll(producers).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);
            }

            Assert.True(
                maxObservedCount <= maxCacheSize,
                $"Values function cache should be bounded to {maxCacheSize}, observed {maxObservedCount}.");
        }

        [Fact]
        public async Task Accepted_file_payloads_are_flushed_when_stop_races_file_queue()
        {
            var sender = new StabilityDataSender { FileSendDelay = TimeSpan.FromMilliseconds(200) };
            var filePaths = Enumerable.Range(0, 4)
                .Select(_ => System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt"))
                .ToArray();

            foreach (var filePath in filePaths)
                System.IO.File.WriteAllText(filePath, "payload");

            try
            {
                using (var collector = CreateCollector(sender, "file-stop-flush"))
                {
                    await collector.Start().ConfigureAwait(false);

                    var accepted = 0;
                    foreach (var filePath in filePaths)
                    {
                        if (await collector.SendFileAsync("stability/file-stop-flush/" + accepted, filePath).ConfigureAwait(false))
                            accepted++;
                    }

                    await collector.Stop().ConfigureAwait(false);

                    Assert.Equal(accepted, sender.TotalFileRequestsSent);
                }
            }
            finally
            {
                foreach (var filePath in filePaths)
                {
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }
            }
        }

        [Fact]
        public async Task Accepted_registration_commands_are_flushed_when_stop_cancels_command_queue()
        {
            var sender = new StabilityDataSender { CommandSendDelay = TimeSpan.FromMilliseconds(200) };
            using (var collector = CreateCollector(sender, "command-stop-flush"))
            {
                await collector.Start().ConfigureAwait(false);

                const int acceptedRegistrations = 4;
                for (var index = 0; index < acceptedRegistrations; index++)
                    collector.CreateDoubleSensor("stability/command-stop-flush/" + index);

                Assert.True(
                    await sender.WaitForCommandSendCallsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The command queue should start sending accepted registration commands before stop races it.");

                await collector.Stop().ConfigureAwait(false);

                Assert.True(
                    sender.TotalCommandRequestsSent >= acceptedRegistrations,
                    $"Stop should flush accepted registration commands. Accepted={acceptedRegistrations}, sent={sender.TotalCommandRequestsSent}.");
            }
        }

        [Fact]
        public async Task Accepted_data_values_are_flushed_when_stop_cancels_in_flight_data_send()
        {
            var sender = new StabilityDataSender { DataSendDelay = TimeSpan.FromMilliseconds(200) };
            using (var collector = CreateCollector(sender, "data-stop-flush"))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.CreateDoubleSensor(
                    "stability/data-stop-flush",
                    new InstantSensorOptions());

                const int acceptedValues = 4;
                for (var index = 0; index < acceptedValues; index++)
                    sensor.AddValue(index);

                Assert.True(
                    await sender.WaitForDataSendCallsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The data queue should start sending accepted values before stop races it.");

                await collector.Stop().ConfigureAwait(false);

                Assert.True(
                    sender.TotalDataValuesSent >= acceptedValues,
                    $"Stop should flush accepted data values. Accepted={acceptedValues}, sent={sender.TotalDataValuesSent}.");
            }
        }

        [Fact]
        public async Task Values_added_by_custom_stopping_task_are_flushed_or_rejected()
        {
            var sender = new StabilityDataSender();
            using (var collector = CreateCollector(sender, "custom-stop-flush"))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.CreateIntSensor(
                    "stability/custom-stop-flush",
                    new InstantSensorOptions());

                var customStoppingTask = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
                    sensor.AddValue(42);
                });

                await collector.Stop(customStoppingTask).ConfigureAwait(false);

                Assert.True(
                    sender.TotalDataValuesSent >= 1,
                    $"Values accepted by the custom stopping task should be flushed before stop completes. Sent={sender.TotalDataValuesSent}.");
            }
        }

        [Fact]
        public async Task Long_function_callback_released_after_data_flush_is_rejected_or_flushed_during_stop()
        {
            var sender = new StabilityDataSender { CommandSendDelay = TimeSpan.FromSeconds(2) };
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (var releaseCallback = new ManualResetEventSlim(false))
            using (var collector = CreateCollector(sender, "late-function-stop"))
            {
                collector.CreateFunctionSensor(
                    "stability/late-function-stop",
                    () =>
                    {
                        callbackEntered.TrySetResult(true);
                        releaseCallback.Wait(TimeSpan.FromSeconds(5));
                        return 777;
                    },
                    new FunctionSensorOptions
                    {
                        PostDataPeriod = TimeSpan.FromMilliseconds(50)
                    });

                await collector.Start().ConfigureAwait(false);

                Assert.True(
                    await sender.WaitForCommandSendCallsAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The registration command should be in flight before stop begins.");

                Assert.True(
                    await WaitOrTimeoutAsync(callbackEntered.Task, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The function callback should enter before stop begins.");

                var stopTask = collector.Stop();

                Assert.True(
                    await sender.WaitForCommandSendCallsAsync(2, TimeSpan.FromSeconds(4)).ConfigureAwait(false),
                    "Command flush should keep the collector in Stopping after data flush.");

                releaseCallback.Set();
                await stopTask.ConfigureAwait(false);

                var queuedDataValues = GetDataQueueCount(collector);
                Assert.True(
                    sender.TotalDataValuesSent > 0 || queuedDataValues == 0,
                    $"A late function callback must be flushed or rejected, not accepted into a stopped queue. Sent={sender.TotalDataValuesSent}, queued={queuedDataValues}.");
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

        private static void UpdateMax(ref int target, int value)
        {
            int snapshot;
            do
            {
                snapshot = Volatile.Read(ref target);
                if (value <= snapshot)
                    return;
            }
            while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
        }

        private static async Task<bool> WaitOrTimeoutAsync(Task task, TimeSpan timeout)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            return completed == task;
        }

        private static int GetDataQueueCount(DataCollector collector)
        {
            var dataProcessor = (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(collector);

            var dataQueue = (DataQueueProcessor)typeof(DataProcessor)
                .GetField("_dataQueue", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(dataProcessor);

            return (int)typeof(QueueProcessorBase<SensorValueBase>)
                .GetProperty("QueueCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty | BindingFlags.FlattenHierarchy)
                .GetValue(dataQueue);
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
            private int _failedCommandSends;
            private int _totalCommandRequestsSent;
            private int _fileSendCalls;
            private int _failedFileSends;
            private int _totalFileRequestsSent;
            private int _failedDataResults;
            private int _failedCommandResults;
            private int _failedFileResults;

            public bool ThrowOnCommand { get; set; }
            public bool ThrowOnData { get; set; }
            public int FailFirstNSends { get; set; }
            public int FailFirstNDataResults { get; set; }
            public int FailFirstNCommandSends { get; set; }
            public int FailFirstNCommandResults { get; set; }
            public int FailFirstNFileSends { get; set; }
            public int FailFirstNFileResults { get; set; }
            public TimeSpan DataSendDelay { get; set; }
            public TimeSpan CommandSendDelay { get; set; }
            public TimeSpan FileSendDelay { get; set; }

            public int DataSendCalls => Volatile.Read(ref _dataSendCalls);
            public int FailedSends => Volatile.Read(ref _failedSends);
            public int TotalDataValuesSent => Volatile.Read(ref _totalDataValuesSent);
            public int CommandSendCalls => Volatile.Read(ref _commandSendCalls);
            public int TotalCommandRequestsSent => Volatile.Read(ref _totalCommandRequestsSent);
            public int FileSendCalls => Volatile.Read(ref _fileSendCalls);
            public int TotalFileRequestsSent => Volatile.Read(ref _totalFileRequestsSent);
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

                if (FailFirstNDataResults > 0)
                {
                    var failed = Interlocked.Increment(ref _failedDataResults);
                    if (failed <= FailFirstNDataResults)
                        return new PackageSendingInfo(contentSize: itemList.Count, response: null, exception: $"Simulated failed send result #{failed}");
                }

                Interlocked.Add(ref _totalDataValuesSent, itemList.Count);

                return default(PackageSendingInfo);
            }

            public async ValueTask<PackageSendingInfo> SendCommandAsync(
                IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                Interlocked.Increment(ref _commandSendCalls);

                if (CommandSendDelay != TimeSpan.Zero)
                    await Task.Delay(CommandSendDelay, token).ConfigureAwait(false);

                if (ThrowOnCommand)
                    throw new InvalidOperationException("Sender configured to throw on commands.");

                if (FailFirstNCommandSends > 0)
                {
                    var failed = Interlocked.Increment(ref _failedCommandSends);
                    if (failed <= FailFirstNCommandSends)
                        throw new InvalidOperationException($"Simulated command send failure #{failed}");
                }

                var commandCount = commands?.Count() ?? 0;
                if (FailFirstNCommandResults > 0)
                {
                    var failed = Interlocked.Increment(ref _failedCommandResults);
                    if (failed <= FailFirstNCommandResults)
                        return new PackageSendingInfo(contentSize: commandCount, response: null, exception: $"Simulated failed command send result #{failed}");
                }

                Interlocked.Add(ref _totalCommandRequestsSent, commandCount);

                return default(PackageSendingInfo);
            }

            public async ValueTask<PackageSendingInfo> SendFileAsync(
                FileSensorValue file, CancellationToken token)
            {
                Interlocked.Increment(ref _fileSendCalls);

                if (FileSendDelay != TimeSpan.Zero)
                    await Task.Delay(FileSendDelay, token).ConfigureAwait(false);

                if (FailFirstNFileSends > 0)
                {
                    var failed = Interlocked.Increment(ref _failedFileSends);
                    if (failed <= FailFirstNFileSends)
                        throw new InvalidOperationException($"Simulated file send failure #{failed}");
                }

                if (FailFirstNFileResults > 0)
                {
                    var failed = Interlocked.Increment(ref _failedFileResults);
                    if (failed <= FailFirstNFileResults)
                        return new PackageSendingInfo(contentSize: 1, response: null, exception: $"Simulated failed file send result #{failed}");
                }

                Interlocked.Increment(ref _totalFileRequestsSent);

                return default(PackageSendingInfo);
            }

            public async Task<bool> WaitForCommandRequestsAsync(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (TotalCommandRequestsSent >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }

            public async Task<bool> WaitForCommandSendCallsAsync(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (CommandSendCalls >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }

            public async Task<bool> WaitForFileRequestsAsync(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (TotalFileRequestsSent >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }

            public async Task<bool> WaitForDataValuesAsync(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (TotalDataValuesSent >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }

            public async Task<bool> WaitForDataSendCallsAsync(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (DataSendCalls >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
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
