using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorAdversarialTests
    {
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

        private sealed class ProbeDataSender : IDataSender
        {
            private readonly TaskCompletionSource<bool> _dataPackageReceived = new TaskCompletionSource<bool>();
            private readonly TaskCompletionSource<bool> _commandPackageReceived = new TaskCompletionSource<bool>();

            private int _dataPackages;
            private int _commandPackages;

            public bool BlockDataUntilCanceled { get; set; }

            public bool ThrowOnData { get; set; }

            public bool ThrowOnCommand { get; set; }

            public int DataPackages => Volatile.Read(ref _dataPackages);

            public int CommandPackages => Volatile.Read(ref _commandPackages);

            public void Dispose()
            {
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
