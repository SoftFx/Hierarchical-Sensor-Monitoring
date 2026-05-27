using HSMDataCollector.Core;
using HSMDataCollector.Exceptions;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        public async Task Stop_with_data_sender_that_ignores_cancellation_does_not_hang()
        {
            var sender = new ProbeDataSender { BlockDataIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(100));
            Task stopTask = null;

            try
            {
                var sensor = collector.CreateDoubleSensor("adversarial/uncancellable-data-sender/data");

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(1);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                stopTask = collector.Stop();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.True(completed == stopTask, "Collector.Stop() should not hang forever when IDataSender ignores cancellation.");
                await stopTask.ConfigureAwait(false);
            }
            finally
            {
                sender.ReleaseBlockedData();

                if (stopTask != null)
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                collector.Dispose();
            }
        }

        [Fact]
        public async Task Stop_with_priority_sender_that_ignores_cancellation_does_not_hang()
        {
            var sender = new ProbeDataSender { BlockDataIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(100));
            Task stopTask = null;

            try
            {
                var sensor = collector.CreateDoubleSensor(
                    "adversarial/uncancellable-priority-sender/data",
                    new InstantSensorOptions { IsPrioritySensor = true });

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(1);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                stopTask = collector.Stop();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.True(completed == stopTask, "Collector.Stop() should not hang forever when priority IDataSender ignores cancellation.");
                await stopTask.ConfigureAwait(false);
            }
            finally
            {
                sender.ReleaseBlockedData();

                if (stopTask != null)
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                collector.Dispose();
            }
        }

        [Fact]
        public async Task Stop_with_command_sender_that_ignores_cancellation_does_not_hang()
        {
            var sender = new ProbeDataSender { BlockCommandIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(100));
            Task stopTask = null;

            try
            {
                for (var i = 0; i < 10; i++)
                    collector.CreateDoubleSensor("adversarial/uncancellable-command-sender/" + i);

                await collector.Start().ConfigureAwait(false);

                Assert.True(await sender.WaitForCommandPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                stopTask = collector.Stop();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.True(completed == stopTask, "Collector.Stop() should not hang forever when command IDataSender ignores cancellation.");
                await stopTask.ConfigureAwait(false);
            }
            finally
            {
                sender.ReleaseBlockedCommand();

                if (stopTask != null)
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                collector.Dispose();
            }
        }

        [Fact]
        public async Task Stop_with_file_sender_that_ignores_cancellation_does_not_hang()
        {
            var sender = new ProbeDataSender { BlockFileIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(100));
            var filePath = Path.GetTempFileName();
            Task stopTask = null;

            try
            {
                File.WriteAllText(filePath, "adversarial file payload");

                await collector.Start().ConfigureAwait(false);
                Assert.True(await collector.SendFileAsync("adversarial/uncancellable-file-sender", filePath).ConfigureAwait(false));

                Assert.True(await sender.WaitForFilePackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                stopTask = collector.Stop();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.True(completed == stopTask, "Collector.Stop() should not hang forever when file IDataSender ignores cancellation.");
                await stopTask.ConfigureAwait(false);
            }
            finally
            {
                sender.ReleaseBlockedFile();

                if (stopTask != null)
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                collector.Dispose();
                File.Delete(filePath);
            }
        }

        [Fact]
        public async Task Stop_with_uncancellable_data_sender_and_backlog_does_not_hang()
        {
            var sender = new ProbeDataSender { BlockDataIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(100));
            Task stopTask = null;

            try
            {
                var sensor = collector.CreateDoubleSensor("adversarial/uncancellable-data-sender-backlog/data");

                await collector.Start().ConfigureAwait(false);

                for (var i = 0; i < 100; i++)
                    sensor.AddValue(i);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                stopTask = collector.Stop();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.True(completed == stopTask, "Collector.Stop() should not hang on flush when IDataSender ignores cancellation and backlog remains.");
                await stopTask.ConfigureAwait(false);
            }
            finally
            {
                sender.ReleaseBlockedData();

                if (stopTask != null)
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                collector.Dispose();
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
        public async Task Message_deduplicator_handles_concurrent_duplicate_bursts()
        {
            var messages = new ConcurrentQueue<string>();

            using (var deduplicator = new MessageDeduplicator(messages.Enqueue, TimeSpan.FromMilliseconds(20), 1000))
            {
                var addTasks = Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() =>
                    {
                        for (var i = 0; i < 250; i++)
                            deduplicator.AddMessage("adversarial/deduplicator/concurrent");
                    }))
                    .ToArray();

                var allAdds = Task.WhenAll(addTasks);
                var completed = await Task.WhenAny(allAdds, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.True(completed == allAdds, "Concurrent AddMessage calls should not block behind a global cache lock.");
                await allAdds.ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                deduplicator.AddMessage("adversarial/deduplicator/concurrent");
            }

            Assert.Contains("adversarial/deduplicator/concurrent", messages);
            Assert.Contains(messages, m => m.StartsWith("adversarial/deduplicator/concurrent ", StringComparison.Ordinal));
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
        public async Task Creating_sensor_while_stop_is_in_progress_does_not_start_it()
        {
            var sender = new ProbeDataSender { BlockDataIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(500));
            Task stopTask = null;

            try
            {
                var sensor = collector.CreateDoubleSensor("adversarial/create-during-stop/active");
                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(1);

                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                stopTask = collector.Stop();
                await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

                var commandPackagesBeforeCreate = sender.CommandPackages;
                collector.CreateDoubleSensor("adversarial/create-during-stop/new");
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

                Assert.Equal(commandPackagesBeforeCreate, sender.CommandPackages);

                sender.ReleaseBlockedData();
                await stopTask.ConfigureAwait(false);
            }
            finally
            {
                sender.ReleaseBlockedData();

                if (stopTask != null)
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

                collector.Dispose();
            }
        }

        [Fact]
        public void Concurrent_sensor_registration_for_same_path_returns_existing_sensor()
        {
            using (var collector = CreateCollector(new ProbeDataSender()))
            {
                var sensors = new ConcurrentBag<object>();
                var exceptions = new ConcurrentQueue<Exception>();

                Parallel.For(0, 50, _ =>
                {
                    try
                    {
                        sensors.Add(collector.CreateDoubleSensor("adversarial/concurrent-registration/same-path"));
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                });

                Assert.Empty(exceptions);
                Assert.Single(sensors.Distinct());
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
        public async Task Last_value_sensor_flushes_latest_value_on_dispose()
        {
            var sender = new ProbeDataSender();
            var collector = CreateCollector(sender);

            var sensor = collector.CreateLastValueDoubleSensor("adversarial/last-value-dispose/data", 0);

            await collector.Start().ConfigureAwait(false);
            sensor.AddValue(42);

            collector.Dispose();

            Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                "Last-value sensor should flush the latest value when the collector is disposed.");
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
        public async Task Stop_during_slow_data_sends_completes_without_parallel_flush()
        {
            var sender = new ProbeDataSender { DataSendDelay = TimeSpan.FromMilliseconds(200) };

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleSensor("adversarial/slow-stop/data");
                await collector.Start().ConfigureAwait(false);

                for (var i = 0; i < 500; i++)
                    sensor.AddValue(i);

                Assert.True(await sender.WaitForDataPackagesAsync(2, TimeSpan.FromSeconds(3)).ConfigureAwait(false),
                    "The test should observe at least two data packages to detect parallel queue processors.");

                await collector.Stop().ConfigureAwait(false);

                var stopTask = collector.Stop();
                var stopCompleted = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                Assert.True(stopCompleted == stopTask, "Collector.Stop() should complete while data sender is slow.");
                await stopTask.ConfigureAwait(false);

                Assert.True(sender.MaxConcurrentDataSends <= 1,
                    $"Stop flush should not run in parallel with the regular data processing loop, actual max concurrent sends: {sender.MaxConcurrentDataSends}.");
            }
        }

        [Fact]
        public async Task Concurrent_start_calls_initialize_collector_once()
        {
            var sender = new ProbeDataSender();
            var startGate = new TaskCompletionSource<bool>();

            using (var collector = CreateCollector(sender))
            {
                collector.CreateDoubleSensor("adversarial/concurrent-start/data");

                var startTasks = Enumerable.Range(0, 50)
                    .Select(_ => Task.Run(async () =>
                    {
                        await startGate.Task.ConfigureAwait(false);
                        await collector.Start().ConfigureAwait(false);
                    }))
                    .ToArray();

                startGate.SetResult(true);
                await Task.WhenAll(startTasks).ConfigureAwait(false);

                Assert.True(await sender.WaitForCommandPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The collector should initialize the sensor once.");

                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

                Assert.Equal(1, sender.CommandPackages);
            }
        }

        [Fact]
        public void Sensor_count_limit_rejects_excess_bar_sensors_before_start()
        {
            using (var collector = CreateCollector(new ProbeDataSender(), maxSensors: 3))
            {
                for (var i = 0; i < 3; i++)
                {
                    collector.CreateDoubleBarSensor(
                        "adversarial/sensor-limit/bar/" + i,
                        new BarSensorOptions
                        {
                            BarPeriod = TimeSpan.FromMinutes(1),
                            PostDataPeriod = TimeSpan.FromSeconds(1)
                        });
                }

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    collector.CreateDoubleBarSensor(
                        "adversarial/sensor-limit/bar/overflow",
                        new BarSensorOptions
                        {
                            BarPeriod = TimeSpan.FromMinutes(1),
                            PostDataPeriod = TimeSpan.FromSeconds(1)
                        }));

                Assert.Contains("Maximum sensor count", exception.Message);
            }
        }

        [Fact]
        public async Task Sensor_count_limit_is_enforced_after_collector_start()
        {
            using (var collector = CreateCollector(new ProbeDataSender(), maxSensors: 1))
            {
                await collector.Start().ConfigureAwait(false);

                collector.CreateDoubleSensor("adversarial/sensor-limit/running/0");

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    collector.CreateDoubleSensor("adversarial/sensor-limit/running/overflow"));

                Assert.Contains("Maximum sensor count", exception.Message);
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
        public async Task Stop_waits_for_short_function_timer_callback_before_disposing_sensor()
        {
            var sender = new ProbeDataSender();
            var callbackEntered = new TaskCompletionSource<bool>();
            var callbackCompleted = new TaskCompletionSource<bool>();

            using (var collector = CreateCollector(sender))
            {
                collector.CreateFunctionSensor(
                    "adversarial/short-function-timer",
                    () =>
                    {
                        callbackEntered.TrySetResult(true);
                        Thread.Sleep(TimeSpan.FromMilliseconds(100));
                        callbackCompleted.TrySetResult(true);
                        return 1;
                    },
                    new FunctionSensorOptions
                    {
                        PostDataPeriod = TimeSpan.FromMilliseconds(50)
                    });

                await collector.Start().ConfigureAwait(false);

                var entered = await Task.WhenAny(callbackEntered.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                Assert.True(entered == callbackEntered.Task, "The function callback should start before stop is tested.");

                await collector.Stop().ConfigureAwait(false);

                Assert.True(callbackCompleted.Task.IsCompleted, "Stop should wait for short in-flight function callbacks.");
                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The value produced by the in-flight callback should be flushed during stop.");
            }
        }

        [Fact]
        public async Task Start_after_stop_timeout_does_not_mark_collector_running_until_queues_finish()
        {
            var sender = new ProbeDataSender { BlockDataIgnoringCancellation = true };
            var collector = CreateCollector(sender, requestTimeout: TimeSpan.FromMilliseconds(100));

            try
            {
                var sensor = collector.CreateDoubleSensor("adversarial/restart-after-timeout/data");
                await collector.Start().ConfigureAwait(false);

                sensor.AddValue(1);
                Assert.True(await sender.WaitForDataPackagesAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The first package should enter the blocked sender before stop.");

                await collector.Stop().ConfigureAwait(false);
                Assert.Equal(CollectorStatus.Stopped, collector.Status);

                var packagesAfterStop = sender.DataPackages;
                await collector.Start().ConfigureAwait(false);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);

                sensor.AddValue(2);
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

                Assert.Equal(packagesAfterStop, sender.DataPackages);

                sender.ReleaseBlockedData();
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

                await collector.Start().ConfigureAwait(false);
                Assert.Equal(CollectorStatus.Running, collector.Status);

                sensor.AddValue(3);
                Assert.True(await sender.WaitForDataPackagesAsync(packagesAfterStop + 1, TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    "The collector should restart after the previous queue loop really exits.");
            }
            finally
            {
                sender.ReleaseBlockedData();
                await collector.Stop().ConfigureAwait(false);
                collector.Dispose();
            }
        }

        [Theory]
        [InlineData(false, "https://127.0.0.1:44330/api/sensors")]
        [InlineData(true, "http://127.0.0.1:44330/api/sensors")]
        public void Explicit_http_server_address_requires_plaintext_opt_in(bool allowPlaintextTransport, string expectedAddress)
        {
            var options = new CollectorOptions
            {
                ServerAddress = "http://127.0.0.1",
                AllowPlaintextTransport = allowPlaintextTransport
            };

            var endpointType = typeof(DataCollector).Assembly.GetType("HSMDataCollector.Client.Endpoints", throwOnError: true);
            var endpoints = Activator.CreateInstance(endpointType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { options }, null);
            var connectionAddress = (string)endpointType.GetProperty("ConnectionAddress", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(endpoints);

            Assert.Equal(expectedAddress, connectionAddress.TrimEnd('/'));
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
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
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
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
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
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
        }

        [Fact]
        public async Task Dispose_from_running_fires_stopping_and_stopped_events()
        {
            var sender = new ProbeDataSender();
            var collector = CreateCollector(sender);

            var stoppingFired = false;
            var stoppedFired = false;

            collector.ToStopping += () => stoppingFired = true;
            collector.ToStopped += () => stoppedFired = true;

            await collector.Start().ConfigureAwait(false);
            Assert.Equal(CollectorStatus.Running, collector.Status);

            collector.Dispose();

            Assert.True(stoppingFired, "ToStopping should fire during Dispose from Running.");
            Assert.True(stoppedFired, "ToStopped should fire during Dispose from Running.");
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
        }

        [Fact]
        public void Double_dispose_does_not_throw()
        {
            var sender = new ProbeDataSender();
            var collector = CreateCollector(sender);

            collector.Dispose();

            var exception = Record.Exception(() => collector.Dispose());

            Assert.Null(exception);
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
        }

        [Fact]
        public async Task Stop_after_dispose_is_noop()
        {
            var sender = new ProbeDataSender();
            var collector = CreateCollector(sender);

            await collector.Start().ConfigureAwait(false);
            collector.Dispose();

            var exception = await Record.ExceptionAsync(() => collector.Stop()).ConfigureAwait(false);

            Assert.Null(exception);
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
        }

        [Fact]
        public async Task Dispose_concurrent_with_stop_fires_ToStopped_exactly_once()
        {
            for (var iteration = 0; iteration < 25; iteration++)
            {
                var sender = new ProbeDataSender();
                var collector = CreateCollector(sender);

                await collector.Start().ConfigureAwait(false);

                var stoppingCount = 0;
                var stoppedCount = 0;
                collector.ToStopping += () => Interlocked.Increment(ref stoppingCount);
                collector.ToStopped += () => Interlocked.Increment(ref stoppedCount);

                // Start the Stop and let it begin draining before Dispose joins
                var stopTask = collector.Stop();
                collector.Dispose();

                // ToStopped must have been raised before Dispose() returned — Dispose either drove
                // CompleteStop itself or waited for Stop's continuation to do it. Either way, no event
                // may fire on a half-disposed collector.
                Assert.Equal(1, stoppedCount);

                await stopTask.ConfigureAwait(false);

                Assert.Equal(CollectorStatus.Disposed, collector.Status);
                Assert.Equal(1, stoppingCount);
                Assert.Equal(1, stoppedCount);
            }
        }


        [Fact]
        public async Task Concurrent_start_and_stop_does_not_leave_queues_running_after_status_stopped()
        {
            // Regression test for the start/stop ordering bug: if Stop runs StopAsync against queues
            // that haven't been spawned yet (because Start released _opLock between TryStart and
            // _dataProcessor.Start()), and Start then spawned them anyway and bailed without rollback,
            // background queue processors would stay alive while public Status reads Stopped.
            //
            // To make the leak observable, after the race we actively try to push data: create a
            // sensor and call AddValue. If the data queue is still alive despite Status == Stopped,
            // SendDataAsync will eventually be invoked on the sender and DataPackages will tick.
            for (var iteration = 0; iteration < 50; iteration++)
            {
                var sender = new ProbeDataSender();
                var collector = CreateCollector(sender);

                var startTask = Task.Run(() => collector.Start());
                var stopTask = Task.Run(() => collector.Stop());

                await Task.WhenAll(startTask, stopTask).ConfigureAwait(false);

                var status = collector.Status;
                Assert.True(
                    status == CollectorStatus.Stopped ||
                    status == CollectorStatus.Running ||
                    status == CollectorStatus.Starting,
                    $"Iteration {iteration}: unexpected status {status}");

                if (status == CollectorStatus.Stopped)
                {
                    var sendsBefore = sender.DataPackages;

                    // Provoke any zombie queue: a live queue will accept and forward this value.
                    // A properly stopped queue will reject (or drop) it.
                    var probe = collector.CreateIntSensor("adversarial/start-stop-race/probe");
                    for (var i = 0; i < 5; i++)
                        probe.AddValue(i);

                    // Wait at least one PackageCollectPeriod so a live worker would flush.
                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

                    var sendsAfter = sender.DataPackages;

                    Assert.True(sendsAfter == sendsBefore,
                        $"Iteration {iteration}: queue processors continued sending after Status == Stopped (before={sendsBefore}, after={sendsAfter}).");
                }

                collector.Dispose();
            }
        }

        [Fact]
        public async Task Concurrent_start_and_stop_keeps_event_order_consistent_with_status()
        {
            // Regression test for the event-ordering race: when Start and Stop race, subscribers
            // must see events in an order consistent with the underlying state machine
            // (no Stopping before Starting).
            for (var iteration = 0; iteration < 50; iteration++)
            {
                var sender = new ProbeDataSender();
                using (var collector = CreateCollector(sender))
                {
                    var events = new ConcurrentQueue<CollectorStatus>();
                    collector.ToStarting += () => events.Enqueue(CollectorStatus.Starting);
                    collector.ToRunning  += () => events.Enqueue(CollectorStatus.Running);
                    collector.ToStopping += () => events.Enqueue(CollectorStatus.Stopping);
                    collector.ToStopped  += () => events.Enqueue(CollectorStatus.Stopped);

                    var startTask = Task.Run(() => collector.Start());
                    var stopTask  = Task.Run(() => collector.Stop());

                    await Task.WhenAll(startTask, stopTask).ConfigureAwait(false);

                    var observed = events.ToArray();
                    // Stopping must not appear before Starting
                    var startingIdx = Array.IndexOf(observed, CollectorStatus.Starting);
                    var stoppingIdx = Array.IndexOf(observed, CollectorStatus.Stopping);

                    if (startingIdx >= 0 && stoppingIdx >= 0)
                    {
                        Assert.True(stoppingIdx > startingIdx,
                            $"Iteration {iteration}: Stopping fired before Starting. Events: [{string.Join(", ", observed)}]");
                    }
                }
            }
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

                await Stop_with_data_sender_that_ignores_cancellation_does_not_hang().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls++;
                sensorCreateCalls++;
                dataFailureBursts++;

                await Stop_with_uncancellable_data_sender_and_backlog_does_not_hang().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 100;
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

                await Last_value_sensor_flushes_latest_value_on_dispose().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls++;
                sensorCreateCalls++;

                await Stop_flush_does_not_resend_same_value_when_sender_does_not_enumerate_items().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls++;
                sensorCreateCalls++;

                await Stop_during_slow_data_sends_completes_without_parallel_flush().ConfigureAwait(false);
                scenarioRuns++;
                addValueCalls += 500;
                sensorCreateCalls++;

                await Concurrent_start_calls_initialize_collector_once().ConfigureAwait(false);
                scenarioRuns++;
                sensorCreateCalls++;

                Sensor_count_limit_rejects_excess_bar_sensors_before_start();
                scenarioRuns++;
                sensorCreateCalls += 4;

                await Sensor_count_limit_is_enforced_after_collector_start().ConfigureAwait(false);
                scenarioRuns++;
                sensorCreateCalls += 2;

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
            Assert.True(scenarioRuns >= 22, "The adversarial suite soak should execute the full scenario list at least once.");

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

        private static DataCollector CreateCollector(ProbeDataSender sender, int maxQueueSize = 1000, TimeSpan? requestTimeout = null, int maxSensors = 100000)
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
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
                MaxSensors = maxSensors
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
            private readonly TaskCompletionSource<bool> _filePackageReceived = new TaskCompletionSource<bool>();

            private int _dataPackages;
            private int _commandPackages;
            private int _filePackages;
            private int _currentDataSends;
            private int _maxConcurrentDataSends;
            private readonly ManualResetEventSlim _blockedDataRelease = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _blockedCommandRelease = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _blockedFileRelease = new ManualResetEventSlim(false);

            public bool BlockDataUntilCanceled { get; set; }

            public bool BlockDataIgnoringCancellation { get; set; }

            public bool BlockCommandIgnoringCancellation { get; set; }

            public bool BlockFileIgnoringCancellation { get; set; }

            public bool ThrowOnData { get; set; }

            public bool ThrowOnCommand { get; set; }

            public bool ThrowOnDispose { get; set; }

            public TimeSpan DataSendDelay { get; set; }

            public int DataPackages => Volatile.Read(ref _dataPackages);

            public int CommandPackages => Volatile.Read(ref _commandPackages);

            public int FilePackages => Volatile.Read(ref _filePackages);

            public int MaxConcurrentDataSends => Volatile.Read(ref _maxConcurrentDataSends);

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
                var concurrent = Interlocked.Increment(ref _currentDataSends);
                UpdateMaxConcurrentDataSends(concurrent);

                try
                {
                    Interlocked.Increment(ref _dataPackages);
                    _dataPackageReceived.TrySetResult(true);

                    if (BlockDataUntilCanceled)
                        await WaitUntilCanceledAsync(token).ConfigureAwait(false);

                    if (BlockDataIgnoringCancellation)
                        await Task.Run(() => _blockedDataRelease.Wait()).ConfigureAwait(false);

                    if (DataSendDelay > TimeSpan.Zero)
                        await Task.Delay(DataSendDelay, token).ConfigureAwait(false);

                    if (ThrowOnData)
                        throw new InvalidOperationException("Injected data sender failure.");

                    return default;
                }
                finally
                {
                    Interlocked.Decrement(ref _currentDataSends);
                }
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                return SendDataAsync(items, token);
            }

            public async ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                Interlocked.Increment(ref _commandPackages);
                _commandPackageReceived.TrySetResult(true);

                if (BlockCommandIgnoringCancellation)
                    await Task.Run(() => _blockedCommandRelease.Wait()).ConfigureAwait(false);

                if (ThrowOnCommand)
                    throw new InvalidOperationException("Injected command sender failure.");

                await Task.Yield();
                return default;
            }

            public async ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
            {
                Interlocked.Increment(ref _filePackages);
                _filePackageReceived.TrySetResult(true);

                if (BlockFileIgnoringCancellation)
                    await Task.Run(() => _blockedFileRelease.Wait()).ConfigureAwait(false);

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

            public async Task<bool> WaitForFilePackagesAsync(int count, TimeSpan timeout)
            {
                return await WaitForPackagesAsync(() => FilePackages >= count, _filePackageReceived.Task, timeout).ConfigureAwait(false);
            }

            public void ReleaseBlockedData()
            {
                _blockedDataRelease.Set();
            }

            public void ReleaseBlockedCommand()
            {
                _blockedCommandRelease.Set();
            }

            public void ReleaseBlockedFile()
            {
                _blockedFileRelease.Set();
            }

            private static async Task<bool> WaitForPackagesAsync(Func<bool> condition, Task signal, TimeSpan timeout)
            {
                if (condition())
                    return true;

                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (condition())
                        return true;

                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    var delay = remaining < TimeSpan.FromMilliseconds(25)
                        ? remaining
                        : TimeSpan.FromMilliseconds(25);

                    if (signal != null && !signal.IsCompleted)
                        await Task.WhenAny(signal, Task.Delay(delay)).ConfigureAwait(false);
                    else
                        await Task.Delay(delay).ConfigureAwait(false);
                }

                return condition();
            }

            private static Task WaitUntilCanceledAsync(CancellationToken token)
            {
                var source = new TaskCompletionSource<bool>();

                token.Register(() => source.TrySetCanceled());

                return source.Task;
            }

            private void UpdateMaxConcurrentDataSends(int value)
            {
                int current;
                do
                {
                    current = Volatile.Read(ref _maxConcurrentDataSends);
                    if (value <= current)
                        return;
                }
                while (Interlocked.CompareExchange(ref _maxConcurrentDataSends, value, current) != current);
            }
        }
    }
}
