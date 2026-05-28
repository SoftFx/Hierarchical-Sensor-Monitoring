using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMDataCollector.SyncQueue.Data;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Verifies that the Windows performance-counter sensors are decoupled from the real
    /// System.Diagnostics.PerformanceCounter via IPerformanceCounterFactory, so they can be exercised
    /// on any OS by substituting a fake factory. Before the isolation these classes could not be
    /// unit-tested off Windows at all.
    /// </summary>
    public sealed class PerformanceCounterIsolationTests
    {
        [Fact]
        public async Task WindowsSensor_reads_value_through_injected_factory()
        {
            using (var collector = CreateCollector())
            {
                var dataProcessor = GetDataProcessor(collector);
                var factory = new FakeFactory(sampleValue: 42.0);
                var options = BarOptions(dataProcessor, "perf/iso/windows");

                var sensor = new TestWindowsSensor(options, factory);

                Assert.True(await sensor.CallInitAsync().ConfigureAwait(false));

                // The factory was consulted with the sensor's category/counter/instance.
                Assert.Equal("TestCategory", factory.LastCategory);
                Assert.Equal("TestCounter", factory.LastCounter);

                // Bar data comes from the fake counter, not a real OS counter.
                Assert.Equal(42.0, sensor.CallGetBarData());

                await sensor.CallStopAsync().ConfigureAwait(false);
                Assert.True(factory.CreatedCounter.Disposed, "Counter should be disposed on stop.");
            }
        }

        [Fact]
        public async Task WindowsSensor_init_fails_when_factory_returns_null_instance()
        {
            using (var collector = CreateCollector())
            {
                var dataProcessor = GetDataProcessor(collector);
                var factory = new FakeFactory(sampleValue: 1.0) { ReturnNull = true };
                var options = BarOptions(dataProcessor, "perf/iso/missing-instance");

                var sensor = new TestWindowsSensor(options, factory) { Instance = "no-such-instance" };

                // Factory returns null for a missing instance → InitAsync must fail gracefully (no throw).
                Assert.False(await sensor.CallInitAsync().ConfigureAwait(false));
            }
        }

        [Fact]
        public void Production_factory_default_is_the_windows_factory()
        {
            using (var collector = CreateCollector())
            {
                var dataProcessor = GetDataProcessor(collector);
                var options = BarOptions(dataProcessor, "perf/iso/default-factory");

                // A sensor that does not override the seam uses the real Windows factory by default.
                var sensor = new DefaultFactoryWindowsSensor(options);

                Assert.IsType<WindowsPerformanceCounterFactory>(sensor.ExposedFactory);
            }
        }

        // --- helpers ---

        private static DataCollector CreateCollector()
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "perf-iso-key",
                ClientName = "perf-iso-client",
                ComputerName = "perf-iso-host",
                Module = "perf-iso-module",
                DataSender = new NoopSender(),
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static DataProcessor GetDataProcessor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private static BarSensorOptions BarOptions(DataProcessor dataProcessor, string path) =>
            new BarSensorOptions
            {
                Path = path,
                DataProcessor = dataProcessor,
                PostDataPeriod = TimeSpan.FromMilliseconds(100),
                BarTickPeriod = TimeSpan.FromMilliseconds(50),
                BarPeriod = TimeSpan.FromMilliseconds(200),
            };

        private sealed class FakeFactory : IPerformanceCounterFactory
        {
            public FakeFactory(double sampleValue) => CreatedCounter = new FakeCounter(sampleValue);

            public FakeCounter CreatedCounter { get; }
            public bool ReturnNull { get; set; }
            public string LastCategory { get; private set; }
            public string LastCounter { get; private set; }
            public string LastInstance { get; private set; }

            public IPerformanceCounter Create(string category, string counter, string instanceFilter = null)
            {
                LastCategory = category;
                LastCounter = counter;
                LastInstance = instanceFilter;
                return ReturnNull ? null : CreatedCounter;
            }
        }

        private sealed class FakeCounter : IPerformanceCounter
        {
            private readonly double _value;
            public FakeCounter(double value) => _value = value;
            public bool Disposed { get; private set; }
            public double NextValue() => _value;
            public void Dispose() => Disposed = true;
        }

        private sealed class TestWindowsSensor : WindowsSensorBase
        {
            private readonly IPerformanceCounterFactory _factory;

            public TestWindowsSensor(BarSensorOptions options, IPerformanceCounterFactory factory) : base(options)
            {
                _factory = factory;
            }

            public string Instance { get; set; }

            internal override IPerformanceCounterFactory PerformanceCounterFactory => _factory;
            protected override string CategoryName => "TestCategory";
            protected override string CounterName => "TestCounter";
            protected override string InstanceName => Instance;

            public ValueTask<bool> CallInitAsync() => InitAsync();
            public ValueTask CallStopAsync() => StopAsync();
            public double? CallGetBarData() => GetBarData();
        }

        private sealed class DefaultFactoryWindowsSensor : WindowsSensorBase
        {
            public DefaultFactoryWindowsSensor(BarSensorOptions options) : base(options) { }

            protected override string CategoryName => "X";
            protected override string CounterName => "Y";

            public IPerformanceCounterFactory ExposedFactory => PerformanceCounterFactory;
        }

        private sealed class NoopSender : IDataSender
        {
            public void Dispose() { }
            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);
            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;
            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;
            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;
            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;
        }
    }
}
