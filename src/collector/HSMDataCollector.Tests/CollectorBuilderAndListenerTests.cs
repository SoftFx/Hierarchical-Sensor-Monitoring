using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorBuilderAndListenerTests
    {
        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "builder-listener-key",
                ClientName = "builder-listener-client",
                ComputerName = "builder-listener-host",
                Module = "builder-listener-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        // --- ILifecycleListener ---

        [Fact]
        public async Task Listener_receives_transitions_in_order()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var listener = new RecordingListener();
                collector.AddLifecycleListener(listener);

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                Assert.Equal(
                    new[] { "Starting", "Running", "Stopping", "Stopped" },
                    listener.Calls.ToArray());
            }
        }

        [Fact]
        public async Task Events_still_fire_alongside_listeners()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var eventCalls = new List<string>();
                collector.ToStarting += () => eventCalls.Add("Starting");
                collector.ToRunning += () => eventCalls.Add("Running");

                var listener = new RecordingListener();
                collector.AddLifecycleListener(listener);

                await collector.Start().ConfigureAwait(false);

                Assert.Contains("Starting", eventCalls);
                Assert.Contains("Running", eventCalls);
                Assert.Contains("Starting", listener.Calls);
                Assert.Contains("Running", listener.Calls);
            }
        }

        [Fact]
        public async Task Multiple_listeners_all_notified()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var a = new RecordingListener();
                var b = new RecordingListener();
                collector.AddLifecycleListener(a).AddLifecycleListener(b);

                await collector.Start().ConfigureAwait(false);

                Assert.Contains("Running", a.Calls);
                Assert.Contains("Running", b.Calls);
            }
        }

        [Fact]
        public async Task Listener_exception_is_isolated()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var healthy = new RecordingListener();
                collector.AddLifecycleListener(new ThrowingListener());
                collector.AddLifecycleListener(healthy);

                var ex = await Record.ExceptionAsync(() => collector.Start()).ConfigureAwait(false);

                Assert.Null(ex); // a throwing listener must not break Start
                Assert.Contains("Running", healthy.Calls); // nor the next listener
            }
        }

        [Fact]
        public void Null_listener_is_ignored()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var ex = Record.Exception(() => collector.AddLifecycleListener(null));
                Assert.Null(ex);
            }
        }

        [Fact]
        public async Task Listener_can_register_another_listener_reentrantly_without_deadlock()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var added = new RecordingListener();
                // A listener that, on the first callback, registers another listener. This re-enters
                // AddLifecycleListener (which takes _listenersLock) while the snapshot is being iterated —
                // must not deadlock because notification iterates a released snapshot.
                collector.AddLifecycleListener(new ReentrantAddListener(collector, added));

                var startTask = collector.Start();
                var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

                Assert.Same(startTask, completed); // no deadlock
                await startTask.ConfigureAwait(false);
            }
        }

        private sealed class ReentrantAddListener : ILifecycleListener
        {
            private readonly IDataCollector _collector;
            private readonly ILifecycleListener _toAdd;
            private int _added;

            public ReentrantAddListener(IDataCollector collector, ILifecycleListener toAdd)
            {
                _collector = collector;
                _toAdd = toAdd;
            }

            private void AddOnce()
            {
                if (Interlocked.Exchange(ref _added, 1) == 0)
                    _collector.AddLifecycleListener(_toAdd);
            }

            public void OnStarting() => AddOnce();
            public void OnRunning() => AddOnce();
            public void OnStopping() => AddOnce();
            public void OnStopped() => AddOnce();
        }

        // --- Sensor builder facade ---

        [Fact]
        public async Task InstantSensor_builder_creates_working_sensor()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.InstantSensor<int>("builder/instant/int")
                    .Description("built via builder")
                    .Build();

                Assert.NotNull(sensor);
                Assert.IsAssignableFrom<IInstantValueSensor<int>>(sensor);

                sensor.AddValue(123);
                Assert.True(await sender.WaitForDataAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false));
            }
        }

        [Fact]
        public async Task BarSensor_builder_creates_working_sensor()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.BarSensor<double>("builder/bar/double")
                    .BarPeriod(TimeSpan.FromMilliseconds(200))
                    .TickPeriod(TimeSpan.FromMilliseconds(50))
                    .PostPeriod(TimeSpan.FromMilliseconds(100))
                    .Precision(3)
                    .Build();

                Assert.NotNull(sensor);
                Assert.IsAssignableFrom<IBarSensor<double>>(sensor);

                sensor.AddValue(1.5);
                Assert.True(await sender.WaitForDataAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false));
            }
        }

        [Fact]
        public async Task RateSensor_builder_creates_working_sensor()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);

                var sensor = collector.RateSensor("builder/rate")
                    .PostPeriod(TimeSpan.FromMilliseconds(150))
                    .Description("rate via builder")
                    .Build();

                Assert.NotNull(sensor);
                sensor.AddValue(10);
                Assert.True(await sender.WaitForDataAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false));
            }
        }

        [Fact]
        public void InstantSensor_builder_rejects_unsupported_type()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                Assert.Throws<NotSupportedException>(() =>
                    collector.InstantSensor<DateTime>("builder/instant/unsupported").Build());
            }
        }

        [Fact]
        public void InstantSensor_builder_rejects_nullable_with_clear_message()
        {
            using (var collector = CreateCollector(new CountingSender()))
            {
                var ex = Assert.Throws<NotSupportedException>(() =>
                    collector.InstantSensor<int?>("builder/instant/nullable").Build());

                // The message must point at the non-nullable form, not just emit an opaque generic name.
                Assert.Contains("nullable", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Int32", ex.Message);
            }
        }

        [Fact]
        public async Task Builder_registered_before_start_queues_then_sends()
        {
            var sender = new CountingSender();
            using (var collector = CreateCollector(sender))
            {
                // Build while stopped — configuration phase, should queue.
                var sensor = collector.InstantSensor<int>("builder/before-start").Build();
                Assert.Contains(collector.DefaultSensors, s => s.SensorPath.Contains("before-start"));

                await collector.Start().ConfigureAwait(false);

                sensor.AddValue(5);
                Assert.True(await sender.WaitForDataAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false));
            }
        }

        private sealed class RecordingListener : ILifecycleListener
        {
            private readonly object _lock = new object();
            private readonly List<string> _calls = new List<string>();

            public List<string> Calls
            {
                get { lock (_lock) return new List<string>(_calls); }
            }

            private void Record(string name) { lock (_lock) _calls.Add(name); }

            public void OnStarting() => Record("Starting");
            public void OnRunning() => Record("Running");
            public void OnStopping() => Record("Stopping");
            public void OnStopped() => Record("Stopped");
        }

        private sealed class ThrowingListener : ILifecycleListener
        {
            public void OnStarting() => throw new InvalidOperationException("boom-starting");
            public void OnRunning() => throw new InvalidOperationException("boom-running");
            public void OnStopping() => throw new InvalidOperationException("boom-stopping");
            public void OnStopped() => throw new InvalidOperationException("boom-stopped");
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
