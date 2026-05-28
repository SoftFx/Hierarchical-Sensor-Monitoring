using HSMDataCollector.Core;
using HSMDataCollector.Options;
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
    /// <summary>
    /// Verifies the two-phase sensor registration contract (#1058):
    ///   - Configuration phase (Stopped): registration is allowed; sensors are queued and started
    ///     by the next Start().
    ///   - Operational phase (Starting/Running): registration is allowed; sensors start immediately.
    ///   - Shutdown (Stopping) and terminal (Disposed): registration is rejected — the sensor is not
    ///     added to storage.
    /// </summary>
    public sealed class CollectorRegistrationContractTests
    {
        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "register-contract-key",
                ClientName = "register-contract-client",
                ComputerName = "register-contract-host",
                Module = "register-contract-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        // --- IsAcceptingRegistrations reflects the phase ---

        [Fact]
        public void IsAcceptingRegistrations_true_when_stopped()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.True(collector.IsAcceptingRegistrations);
            }
        }

        [Fact]
        public async Task IsAcceptingRegistrations_true_when_running()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                await collector.Start().ConfigureAwait(false);

                Assert.Equal(CollectorStatus.Running, collector.Status);
                Assert.True(collector.IsAcceptingRegistrations);
            }
        }

        [Fact]
        public async Task IsAcceptingRegistrations_false_when_stopping()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                await collector.Start().ConfigureAwait(false);

                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopTask = collector.Stop(release.Task);

                Assert.Equal(CollectorStatus.Stopping, collector.Status);
                Assert.False(collector.IsAcceptingRegistrations);

                release.SetResult(true);
                await stopTask.ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task IsAcceptingRegistrations_false_after_dispose()
        {
            var collector = CreateCollector(new CountingSender());
            await collector.Start().ConfigureAwait(false);

            collector.Dispose();

            Assert.Equal(CollectorStatus.Disposed, collector.Status);
            Assert.False(collector.IsAcceptingRegistrations);
        }

        // --- Configuration phase: register before Start ---

        [Fact]
        public async Task Sensors_registered_before_start_all_send_after_start()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                var sensors = Enumerable.Range(0, 5)
                    .Select(i => collector.CreateIntSensor($"contract/before-start/{i}"))
                    .ToList();

                // All five must be registered (queued) while stopped.
                Assert.Equal(5, collector.DefaultSensors.Count(s => s.SensorPath.Contains("before-start")));

                // Nothing is sent before Start — the collector drops values while stopped.
                foreach (var s in sensors)
                    s.AddValue(1);
                await Task.Delay(150).ConfigureAwait(false);
                Assert.Equal(0, sender.DataPackages);

                await collector.Start().ConfigureAwait(false);

                // After Start, every queued sensor is operational and its values reach the sender.
                foreach (var s in sensors)
                    s.AddValue(2);

                var received = await sender.WaitForDataAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                Assert.True(received, "Values from sensors registered before Start should be sent after Start.");
            }
        }

        // --- Operational phase: register during Running ---

        [Fact]
        public async Task Sensor_registered_during_running_sends_immediately()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.CreateIntSensor("contract/during-running/0");
                Assert.Contains(collector.DefaultSensors, s => s.SensorPath.Contains("during-running"));

                sensor.AddValue(42);

                var received = await sender.WaitForDataAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                Assert.True(received, "A sensor registered while Running should send values immediately.");
            }
        }

        // --- Shutdown phase: register during Stopping is rejected ---

        [Fact]
        public async Task Register_during_stopping_is_rejected()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                await collector.Start().ConfigureAwait(false);

                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopTask = collector.Stop(release.Task);

                Assert.Equal(CollectorStatus.Stopping, collector.Status);

                var sensor = collector.CreateIntSensor("contract/during-stopping/0");

                // The sensor must NOT be added to storage while stopping.
                Assert.DoesNotContain(collector.DefaultSensors, s => s.SensorPath.Contains("during-stopping"));

                // Using the rejected sensor must not throw.
                var ex = Record.Exception(() => sensor.AddValue(1));
                Assert.Null(ex);

                release.SetResult(true);
                await stopTask.ConfigureAwait(false);
            }
        }

        // --- Terminal: register after Dispose is rejected ---

        [Fact]
        public async Task Register_after_dispose_is_rejected_and_does_not_throw()
        {
            var collector = CreateCollector(new CountingSender());
            await collector.Start().ConfigureAwait(false);
            collector.Dispose();

            var sensor = collector.CreateIntSensor("contract/after-dispose/0");

            Assert.DoesNotContain(collector.DefaultSensors, s => s.SensorPath.Contains("after-dispose"));

            var ex = Record.Exception(() => sensor.AddValue(1));
            Assert.Null(ex);
        }

        // --- Restart: register after Stop queues for the next Start ---

        [Fact]
        public async Task Register_after_stop_queues_for_next_start()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.True(collector.IsAcceptingRegistrations);

                var sensor = collector.CreateIntSensor("contract/after-stop/0");
                Assert.Contains(collector.DefaultSensors, s => s.SensorPath.Contains("after-stop"));

                await collector.Start().ConfigureAwait(false);

                sensor.AddValue(7);
                var received = await sender.WaitForDataAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                Assert.True(received, "A sensor registered after Stop should send after the next Start.");
            }
        }

        private sealed class CountingSender : IDataSender
        {
            private int _dataPackages;
            private readonly TaskCompletionSource<bool> _dataReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int DataPackages => Volatile.Read(ref _dataPackages);

            public Task<bool> WaitForDataAsync(TimeSpan timeout) => WaitAsync(_dataReceived.Task, timeout);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                Interlocked.Increment(ref _dataPackages);
                _dataReceived.TrySetResult(true);
                return default;
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
                => SendDataAsync(items, token);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;

            private static async Task<bool> WaitAsync(Task signal, TimeSpan timeout)
            {
                var completed = await Task.WhenAny(signal, Task.Delay(timeout)).ConfigureAwait(false);
                return ReferenceEquals(completed, signal);
            }
        }
    }
}
