using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorAdversarialTests
    {
        private readonly ITestOutputHelper _output;

        public CollectorAdversarialTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Rate_sensor_nan_value_does_not_spin_forever()
        {
            using (var collector = CreateCollector(new ProbeDataSender()))
            {
                var sensor = collector.CreateRateSensor("adversarial/rate/nan", null);

                sensor.AddValue(double.NaN);

                var addValueTask = Task.Run(() => sensor.AddValue(1));
                var completed = await Task.WhenAny(addValueTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                Assert.True(completed == addValueTask, "RateSensor.AddValue(double.NaN) should not spin forever and burn CPU.");
            }
        }

        [Fact]
        public async Task Stop_after_initialize_stops_data_delivery()
        {
            var sender = new ProbeDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/initialize-stop/data");

                collector.Initialize(false);
                sensor.AddValue(1);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                await collector.Stop().ConfigureAwait(false);

                var packagesAfterStop = sender.DataPackages;
                sensor.AddValue(2);

                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                Assert.Equal(packagesAfterStop, sender.DataPackages);
            }
        }

        [Fact]
        public async Task Stop_while_start_is_pending_does_not_leave_collector_running()
        {
            var sender = new ProbeDataSender();

            using (var collector = CreateCollector(sender))
            {
                collector.CreateDoubleSensor("adversarial/start-stop/data");

                var startTask = collector.Start(Task.Delay(TimeSpan.FromMilliseconds(300)));

                await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);
                await startTask.ConfigureAwait(false);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);
            }
        }

        [Fact]
        public async Task Dispose_cancels_blocked_data_sender()
        {
            var sender = new ProbeDataSender { BlockDataUntilCanceled = true };

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/blocked-data/data");

                collector.Initialize(false);
                sensor.AddValue(1);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                var disposeTask = Task.Run(() => collector.Dispose());
                var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

                Assert.True(completed == disposeTask, "Dispose should cancel a blocked data send.");
            }
        }

        [Fact]
        public async Task Permanent_data_sender_failures_do_not_block_dispose()
        {
            var sender = new ProbeDataSender { ThrowOnData = true };

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/data-failures/data");

                collector.Initialize(false);

                for (var i = 0; i < 1000; i++)
                    sensor.AddValue(i);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                var disposeTask = Task.Run(() => collector.Dispose());
                var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

                Assert.True(completed == disposeTask, "Dispose should complete after repeated data sender failures.");
            }
        }

        [Fact]
        public async Task Permanent_command_sender_failures_do_not_block_dispose()
        {
            var sender = new ProbeDataSender { ThrowOnCommand = true };

            using (var collector = CreateCollector(sender))
            {
                for (var i = 0; i < 100; i++)
                    collector.CreateDoubleSensor("adversarial/command-failures/" + i);

                collector.Initialize(false);

                Assert.True(await sender.WaitForCommandPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                var disposeTask = Task.Run(() => collector.Dispose());
                var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

                Assert.True(completed == disposeTask, "Dispose should complete after repeated command sender failures.");
            }
        }

        [Fact]
        public async Task Creating_sensors_after_initialize_under_command_failures_does_not_hang()
        {
            var sender = new ProbeDataSender { ThrowOnCommand = true };

            using (var collector = CreateCollector(sender))
            {
                collector.Initialize(false);

                var tasks = Enumerable.Range(0, 100)
                    .Select(i => Task.Run(() => collector.CreateDoubleSensor("adversarial/dynamic-sensors/" + i)))
                    .ToArray();
                var allTasks = Task.WhenAll(tasks);

                var completed = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

                Assert.True(completed == allTasks, "Dynamic sensor registration should not hang.");
                await allTasks.ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task Concurrent_add_value_during_dispose_does_not_throw_to_callers()
        {
            var sender = new ProbeDataSender { BlockDataUntilCanceled = true };
            var exceptions = new List<Exception>();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/concurrent-dispose/data");

                collector.Initialize(false);

                var producers = Enumerable.Range(0, 8)
                    .Select(worker => Task.Run(() =>
                    {
                        try
                        {
                            for (var i = 0; i < 2000; i++)
                                sensor.AddValue(worker * 2000 + i);
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions)
                                exceptions.Add(ex);
                        }
                    }))
                    .ToArray();

                await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                collector.Dispose();

                await Task.WhenAll(producers).ConfigureAwait(false);

                Assert.Empty(exceptions);
            }
        }

        [Fact]
        public async Task Queue_overflow_under_flood_keeps_collector_responsive()
        {
            var sender = new ProbeDataSender { BlockDataUntilCanceled = true };

            using (var collector = CreateCollector(sender, maxQueueSize: 100))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/overflow/data");

                collector.Initialize(false);

                for (var i = 0; i < 10000; i++)
                    sensor.AddValue(i);

                var disposeTask = Task.Run(() => collector.Dispose());
                var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

                Assert.True(completed == disposeTask, "Queue overflow should stay bounded enough for fast disposal.");
            }
        }

        [Fact]
        public async Task Repeated_start_stop_cycles_do_not_leave_sender_active()
        {
            var sender = new ProbeDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/repeated-cycles/data");

                for (var cycle = 0; cycle < 5; cycle++)
                {
                    await collector.Start().ConfigureAwait(false);
                    sensor.AddValue(cycle);
                    Assert.True(await sender.WaitForDataPackagesAsync(cycle + 1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));
                    await collector.Stop().ConfigureAwait(false);
                }

                var packagesAfterStop = sender.DataPackages;
                sensor.AddValue(100);
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                Assert.Equal(packagesAfterStop, sender.DataPackages);
            }
        }

        [Fact]
        public async Task Values_added_while_stopped_are_not_sent_after_restart()
        {
            var sender = new ProbeDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/stopped-values/data");

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(1);
                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                await collector.Stop().ConfigureAwait(false);

                var packagesAfterStop = sender.DataPackages;
                sensor.AddValue(2);

                await collector.Start().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                Assert.Equal(packagesAfterStop, sender.DataPackages);
            }
        }

        [Fact]
        public async Task Last_value_sensor_flushes_latest_value_on_stop()
        {
            var sender = new ProbeDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateLastValueDoubleSensor("adversarial/last-value-stop/data", 0);

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(42);

                await collector.Stop().ConfigureAwait(false);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "Last-value sensor should flush the latest value when the collector stops.");
            }
        }

        [Fact]
        public async Task Stop_flush_does_not_resend_same_value_when_sender_does_not_enumerate_items()
        {
            var sender = new ProbeDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateLastValueDoubleSensor("adversarial/lazy-sender-stop/data", 0);

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(42);

                await collector.Stop().ConfigureAwait(false);

                Assert.Equal(1, sender.DataPackages);
            }
        }

        [Fact]
        public async Task Blocked_function_timer_callback_does_not_block_collector_stop()
        {
            var sender = new ProbeDataSender();
            var callbackEntered = new TaskCompletionSource<bool>();
            var releaseCallback = new ManualResetEventSlim(false);
            var collector = CreateCollector(sender);
            Task stopTask = null;
            var stopCompletedBeforeRelease = false;

            try
            {
                collector.CreateFunctionSensor(
                    "adversarial/blocked-function-timer",
                    () =>
                    {
                        callbackEntered.TrySetResult(true);
                        releaseCallback.Wait();
                        return 1;
                    },
                    new FunctionSensorOptions
                    {
                        PostDataPeriod = TimeSpan.FromMilliseconds(50)
                    });

                await collector.Start().ConfigureAwait(false);

                var entered = await Task.WhenAny(callbackEntered.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                Assert.True(entered == callbackEntered.Task, "The blocking function callback should start before stop is tested.");

                stopTask = Task.Run(() => collector.Stop());
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                stopCompletedBeforeRelease = completed == stopTask;
            }
            finally
            {
                releaseCallback.Set();

                if (stopTask != null)
                {
                    var completedAfterRelease = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                    if (completedAfterRelease == stopTask)
                        await stopTask.ConfigureAwait(false);
                }

                collector.Dispose();
                releaseCallback.Dispose();
            }

            Assert.True(stopCompletedBeforeRelease, "Collector.Stop() should not wait forever for a blocked function sensor callback.");
        }

        [Fact]
        public async Task Lifecycle_event_handler_exception_does_not_escape_collector_stop()
        {
            using (var collector = CreateCollector(new ProbeDataSender()))
            {
                collector.ToStopped += () => throw new InvalidOperationException("Injected lifecycle event failure.");

                await collector.Start().ConfigureAwait(false);

                var exception = await Record.ExceptionAsync(() => collector.Stop()).ConfigureAwait(false);

                Assert.Null(exception);
                Assert.Equal(CollectorStatus.Stopped, collector.Status);
            }
        }

        [Fact]
        public void Data_sender_dispose_exception_does_not_escape_collector_dispose()
        {
            var sender = new ProbeDataSender { ThrowOnDispose = true };
            var collector = CreateCollector(sender);

            var exception = Record.Exception(() => collector.Dispose());

            Assert.Null(exception);
            Assert.Equal(CollectorStatus.Stopped, collector.Status);
        }

        [Fact]
        public async Task Start_after_dispose_does_not_resurrect_collector()
        {
            var sender = new ProbeDataSender();
            var collector = CreateCollector(sender);

            collector.CreateDoubleSensor("adversarial/start-after-dispose/data");
            collector.Dispose();

            var exception = await Record.ExceptionAsync(() => collector.Start()).ConfigureAwait(false);

            Assert.Null(exception);
            Assert.Equal(CollectorStatus.Stopped, collector.Status);
        }

        [Fact]
        public void Initialize_after_dispose_does_not_resurrect_collector()
        {
            var sender = new ProbeDataSender();
            var collector = CreateCollector(sender);

            collector.CreateDoubleSensor("adversarial/initialize-after-dispose/data");
            collector.Dispose();

            var exception = Record.Exception(() => collector.Initialize(false));

            Assert.Null(exception);
            Assert.Equal(CollectorStatus.Stopped, collector.Status);
        }

        [SuiteSoakFact]
        public async Task Adversarial_suite_repeated_for_duration_stays_green()
        {
            var duration = GetSuiteSoakDuration();
            var maxDuration = GetSuiteSoakMaxDuration();
            var before = SuiteSoakResourceSnapshot.Capture();
            var stopwatch = Stopwatch.StartNew();
            var cycles = 0;
            var scenarioRuns = 0;
            long addValueCalls = 0;
            long sensorCreateCalls = 0;
            long dataFailureBursts = 0;
            long commandFailureBursts = 0;

            while (stopwatch.Elapsed < duration)
            {
                cycles++;

                await Rate_sensor_nan_value_does_not_spin_forever().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 2;

                await Stop_after_initialize_stops_data_delivery().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 2;
                sensorCreateCalls++;

                await Stop_while_start_is_pending_does_not_leave_collector_running().ConfigureAwait(false);
                scenarioRuns++;
                sensorCreateCalls++;

                await Dispose_cancels_blocked_data_sender().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls++;
                sensorCreateCalls++;

                await Permanent_data_sender_failures_do_not_block_dispose().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 1000;
                sensorCreateCalls++;
                dataFailureBursts++;

                await Permanent_command_sender_failures_do_not_block_dispose().ConfigureAwait(false);
                scenarioRuns++;
                sensorCreateCalls += 100;
                commandFailureBursts++;

                await Creating_sensors_after_initialize_under_command_failures_does_not_hang().ConfigureAwait(false);
                scenarioRuns++;
                sensorCreateCalls += 100;
                commandFailureBursts++;

                await Concurrent_add_value_during_dispose_does_not_throw_to_callers().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 8 * 2000;
                sensorCreateCalls++;

                await Queue_overflow_under_flood_keeps_collector_responsive().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 10000;
                sensorCreateCalls++;

                await Repeated_start_stop_cycles_do_not_leave_sender_active().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 6;
                sensorCreateCalls++;

                await Values_added_while_stopped_are_not_sent_after_restart().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 2;
                sensorCreateCalls++;

                await Last_value_sensor_flushes_latest_value_on_stop().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls++;
                sensorCreateCalls++;

                await Stop_flush_does_not_resend_same_value_when_sender_does_not_enumerate_items().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls++;
                sensorCreateCalls++;

                await Start_after_dispose_does_not_resurrect_collector().ConfigureAwait(false);
                scenarioRuns++;
                sensorCreateCalls++;

                Initialize_after_dispose_does_not_resurrect_collector();
                scenarioRuns++;
                sensorCreateCalls++;

                AssertWithinSuiteSoakMax(stopwatch, maxDuration);
            }

            var after = SuiteSoakResourceSnapshot.Capture();
            SuiteSoakResourceSnapshot.WriteDelta(_output, "adversarialSuiteSoak", before, after);
            SuiteSoakResourceSnapshot.AssertNoCriticalGrowth(before, after);

            Assert.True(cycles > 0, "The adversarial suite soak should complete at least one suite cycle.");
            Assert.True(scenarioRuns >= 15, "The adversarial suite soak should execute the full scenario list at least once.");

            _output.WriteLine(
                "adversarialSuiteSoak; durationSeconds={0}; maxSeconds={1}; elapsedSeconds={2}; cycles={3}; scenarioRuns={4}; addValues={5}; sensorCreates={6}; dataFailureBursts={7}; commandFailureBursts={8}",
                duration.TotalSeconds,
                maxDuration.TotalSeconds,
                stopwatch.Elapsed.TotalSeconds,
                cycles,
                scenarioRuns,
                addValueCalls,
                sensorCreateCalls,
                dataFailureBursts,
                commandFailureBursts);
        }

        private static DataCollector CreateCollector(ProbeDataSender sender, int maxQueueSize = 1000)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "adversarial-test-key",
                ClientName = "adversarial-test-client",
                ComputerName = "adversarial-test-host",
                Module = "adversarial-test-module",
                DataSender = sender,
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100
            });
        }

        private static TimeSpan GetSuiteSoakDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(30);
        }

        private static TimeSpan GetSuiteSoakMaxDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromMinutes(2);
        }

        private static void AssertWithinSuiteSoakMax(Stopwatch stopwatch, TimeSpan maxDuration)
        {
            Assert.True(stopwatch.Elapsed <= maxDuration,
                $"Suite soak exceeded hard limit {maxDuration}. Target duration is soft, but exceeding the hard limit means the suite likely hung.");
        }

        private sealed class SuiteSoakFactAttribute : FactAttribute
        {
            public SuiteSoakFactAttribute()
            {
                if (!string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_SUITE_SOAK"), "1", StringComparison.Ordinal))
                    Skip = "Set HSM_COLLECTOR_RUN_SUITE_SOAK=1 to run repeated suite soak tests.";
            }
        }

        private sealed class ProbeDataSender : IDataSender
        {
            private readonly TaskCompletionSource<bool> _dataPackageReceived = new TaskCompletionSource<bool>();
            private readonly TaskCompletionSource<bool> _commandPackageReceived = new TaskCompletionSource<bool>();

            private int _dataPackages;
            private int _commandPackages;

            public bool BlockDataUntilCanceled { get; set; }

            public bool ThrowOnData { get; set; }

            public bool ThrowOnCommand { get; set; }

            public bool ThrowOnDispose { get; set; }

            public int DataPackages => Volatile.Read(ref _dataPackages);

            public int CommandPackages => Volatile.Read(ref _commandPackages);

            public void Dispose()
            {
                if (ThrowOnDispose)
                    throw new InvalidOperationException("Injected data sender dispose failure.");
            }

            public ValueTask<ConnectionResult> TestConnectionAsync()
            {
                return new ValueTask<ConnectionResult>(ConnectionResult.Ok);
            }

            public async ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                Interlocked.Increment(ref _dataPackages);
                _dataPackageReceived.TrySetResult(true);

                if (BlockDataUntilCanceled)
                    await WaitUntilCanceledAsync(token).ConfigureAwait(false);

                if (ThrowOnData)
                    throw new InvalidOperationException("Injected data sender failure.");

                return default;
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                return SendDataAsync(items, token);
            }

            public async ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                Interlocked.Increment(ref _commandPackages);
                _commandPackageReceived.TrySetResult(true);

                if (ThrowOnCommand)
                    throw new InvalidOperationException("Injected command sender failure.");

                await Task.Yield();
                return default;
            }

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
            {
                return default;
            }

            public async Task<bool> WaitForDataPackagesAsync(int count, TimeSpan timeout)
            {
                return await WaitForPackagesAsync(() => DataPackages >= count, _dataPackageReceived.Task, timeout).ConfigureAwait(false);
            }

            public async Task<bool> WaitForCommandPackagesAsync(int count, TimeSpan timeout)
            {
                return await WaitForPackagesAsync(() => CommandPackages >= count, _commandPackageReceived.Task, timeout).ConfigureAwait(false);
            }

            private static async Task<bool> WaitForPackagesAsync(Func<bool> condition, Task signal, TimeSpan timeout)
            {
                if (condition())
                    return true;

                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    var completed = await Task.WhenAny(signal, Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero)).ConfigureAwait(false);

                    if (condition())
                        return true;

                    if (completed != signal)
                        return false;
                }

                return condition();
            }

            private static Task WaitUntilCanceledAsync(CancellationToken token)
            {
                var source = new TaskCompletionSource<bool>();

                token.Register(() => source.TrySetCanceled());

                return source.Task;
            }
        }
    }
}
