using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Windows.Network;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FamilyReading = HSMDataCollector.DefaultSensors.Windows.Network.TcpFailureRateAccumulator.FamilyReading;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1659 B2: the TCP connection failure rate sensor.
    ///
    /// The accumulator cases are the native suite's <c>tcp_failure_accumulator_*</c> cases
    /// (src/native/collector/tests/hsm_collector_tests.cpp) with the same readings and the same
    /// expected deltas: root rule 10 asks for one algorithm in both collectors, and these numbers
    /// are what holds the two to it. The registration is pinned for both drivers by the
    /// conformance fixture collector/windows/tcp_failure_rate_contract.hsmtest.
    /// </summary>
    public sealed class TcpFailureRateTests
    {
        private static FamilyReading Read(ulong attemptFails) => new FamilyReading(attemptFails);

        private static FamilyReading Unreadable => FamilyReading.Unreadable;

        [Fact]
        public void Accumulator_seeds_the_baseline_on_the_first_tick()
        {
            var accumulator = new TcpFailureRateAccumulator();

            Assert.Null(accumulator.Accumulate(Read(100), Read(5)));
        }

        [Fact]
        public void Accumulator_returns_the_delta_after_the_seed()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(0));

            Assert.Equal(7.0, accumulator.Accumulate(Read(107), Read(0)));
        }

        [Fact]
        public void Accumulator_sums_both_families()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(200));

            Assert.Equal(8.0, accumulator.Accumulate(Read(103), Read(205)));
        }

        [Fact]
        public void Accumulator_returns_nothing_for_a_quiet_interval()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(200));

            Assert.Null(accumulator.Accumulate(Read(100), Read(200)));
        }

        [Fact]
        public void Accumulator_counter_reset_contributes_zero_and_takes_the_new_baseline()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(200));

            Assert.Null(accumulator.Accumulate(Read(5), Read(200)));
            Assert.Equal(3.0, accumulator.Accumulate(Read(8), Read(200)));
        }

        [Fact]
        public void Accumulator_reset_in_one_family_keeps_the_other_delta()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(200));

            Assert.Equal(5.0, accumulator.Accumulate(Read(1), Read(205)));
        }

        [Fact]
        public void Accumulator_unreadable_family_keeps_its_baseline()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(50));

            Assert.Equal(2.0, accumulator.Accumulate(Unreadable, Read(52)));
            Assert.Equal(5.0, accumulator.Accumulate(Read(105), Read(52)));
        }

        [Fact]
        public void Accumulator_returns_nothing_when_neither_family_is_readable()
        {
            var accumulator = new TcpFailureRateAccumulator();
            accumulator.Accumulate(Read(100), Read(200));

            Assert.Null(accumulator.Accumulate(Unreadable, Unreadable));
            Assert.Equal(10.0, accumulator.Accumulate(Read(104), Read(206)));
        }

        [Fact]
        public void Accumulator_families_seed_independently()
        {
            var accumulator = new TcpFailureRateAccumulator();

            Assert.Null(accumulator.Accumulate(Read(100), Unreadable));
            Assert.Equal(3.0, accumulator.Accumulate(Read(103), Unreadable));
            Assert.Null(accumulator.Accumulate(Read(103), Read(500)));
            Assert.Equal(4.0, accumulator.Accumulate(Read(103), Read(504)));
        }

        [Fact]
        public async Task Monitor_posts_the_failures_it_read_as_a_rate()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var sender = new RateRecordingSender();
            long v4 = 100;

            using (var collector = CreateCollector(sender))
            {
                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                // IPv4 fails 5 more attempts on every reading; IPv6 cannot be read at all.
                var monitor = new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromMilliseconds(20),
                    readV4: () => new FamilyReading((ulong)Interlocked.Add(ref v4, 5)),
                    readV6: () => FamilyReading.Unreadable);

                await collector.Start();
                await monitor.InitAsync();
                await monitor.StartAsync();

                Assert.True(await sender.WaitForPositiveRateAsync(TimeSpan.FromSeconds(15)),
                            "No positive rate was posted for the failures the monitor read.");

                await monitor.StopAsync();
                monitor.Dispose();
                await collector.Stop();
            }

            Assert.All(sender.RatePaths, path => Assert.EndsWith("/.computer/Network/Connection failures rate", path));
        }

        [Fact]
        public void Monitor_refuses_a_period_that_is_not_positive()
        {
            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsTcpFailureRateMonitor(storage, TimeSpan.Zero));
            }
        }


        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "tcp-rate-key",
                ClientName = "tcp-rate-client",
                ComputerName = "tcp-rate-host",
                Module = "tcp-rate-module",
                DataSender = sender,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
            });
        }

        private sealed class RateRecordingSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly List<RateSensorValue> _rates = new List<RateSensorValue>();

            internal List<string> RatePaths
            {
                get
                {
                    lock (_lock)
                        return _rates.Select(r => r.Path).ToList();
                }
            }

            internal async Task<bool> WaitForPositiveRateAsync(TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    lock (_lock)
                        if (_rates.Any(r => r.Value > 0))
                            return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                lock (_lock)
                    _rates.AddRange(items.OfType<RateSensorValue>());

                return Accepted();
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => SendDataAsync(items, token);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => Accepted();

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => Accepted();

            private static ValueTask<PackageSendingInfo> Accepted() =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));
        }
    }
}
