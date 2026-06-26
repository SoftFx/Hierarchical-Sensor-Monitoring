using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorMonitoringSensorTests
    {
        [Fact]
        public async Task Rate_sensor_rejects_invalid_status_updates()
        {
            var sender = new RecordingDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateRateSensor(
                    "contract/rate/invalid-status",
                    new RateSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(50) });

                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForCountAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));

                sender.Clear();
                sensor.AddValue(100, (SensorStatus)99, "bad-status");

                Assert.True(await sender.WaitForCountAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));
            }

            Assert.DoesNotContain(sender.Values, value => (int)value.Status == 99);
        }

        [Fact]
        public async Task Double_bar_sensor_rejects_nan_values_without_sending_bar()
        {
            var sender = new RecordingDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleBarSensor(
                    "contract/bar/nan",
                    new BarSensorOptions
                    {
                        BarPeriod = TimeSpan.FromMilliseconds(50),
                        BarTickPeriod = TimeSpan.FromMilliseconds(10),
                        PostDataPeriod = TimeSpan.FromMilliseconds(50),
                    });

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(double.NaN);

                Assert.False(await sender.WaitForCountAsync(1, TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));
            }
        }

        [Fact]
        public async Task Function_sensor_rejects_nan_values_without_sending_payload()
        {
            var sender = new RecordingDataSender();

            using (var collector = CreateCollector(sender))
            {
                collector.CreateFunctionSensor(
                    "contract/function/nan",
                    () => double.NaN,
                    new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(50) });

                await collector.Start().ConfigureAwait(false);

                Assert.False(await sender.WaitForCountAsync(1, TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));
            }
        }

        [Fact]
        public void Double_bar_sensor_rejects_negative_precision()
        {
            using (var collector = CreateCollector(new RecordingDataSender()))
            {
                var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    collector.CreateDoubleBarSensor(
                        "contract/bar/negative-precision",
                        new BarSensorOptions
                        {
                            BarPeriod = TimeSpan.FromMilliseconds(50),
                            BarTickPeriod = TimeSpan.FromMilliseconds(10),
                            PostDataPeriod = TimeSpan.FromMilliseconds(50),
                            Precision = -1,
                        }));

                Assert.Contains("Precision", exception.Message);
            }
        }

        [Fact]
        public async Task Double_bar_sensor_rejects_inconsistent_partial_values_without_sending_bar()
        {
            var sender = new RecordingDataSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateDoubleBarSensor(
                    "contract/bar/inconsistent-partial",
                    new BarSensorOptions
                    {
                        BarPeriod = TimeSpan.FromMilliseconds(50),
                        BarTickPeriod = TimeSpan.FromMilliseconds(10),
                        PostDataPeriod = TimeSpan.FromMilliseconds(50),
                    });

                await collector.Start().ConfigureAwait(false);
                sensor.AddPartial(min: 10, max: 1, mean: 5, first: 10, last: 1, count: 3);

                Assert.False(await sender.WaitForCountAsync(1, TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));
            }
        }

        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "conformance-key",
                ServerAddress = "https://localhost",
                ClientName = "conformance-client",
                ComputerName = "conformance-host",
                Module = "conformance-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 10,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
            });
        }

        private sealed class RecordingDataSender : IDataSender
        {
            private readonly List<SensorValueBase> _values = new List<SensorValueBase>();
            private readonly object _lock = new object();

            public IReadOnlyList<SensorValueBase> Values
            {
                get
                {
                    lock (_lock)
                        return _values.ToArray();
                }
            }

            public void Clear()
            {
                lock (_lock)
                    _values.Clear();
            }

            public void Dispose()
            {
            }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                lock (_lock)
                    _values.AddRange(items);

                return default;
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                SendDataAsync(items, token);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
                default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                default;

            public async Task<bool> WaitForCountAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    lock (_lock)
                    {
                        if (_values.Count >= count)
                            return true;
                    }

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }
        }
    }
}
