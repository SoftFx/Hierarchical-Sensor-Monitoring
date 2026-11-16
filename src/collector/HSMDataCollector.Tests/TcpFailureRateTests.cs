#if NET5_0_OR_GREATER
#nullable disable
#endif

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
        public void Monitor_refuses_a_period_it_cannot_wait_for()
        {
            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsTcpFailureRateMonitor(storage, TimeSpan.Zero));
                // Task.Delay counts in whole milliseconds: half of one is a loop that never waits.
                Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 2)));
                Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromDays(30)));
            }
        }

        [Fact]
        public async Task Monitor_started_twice_leaves_no_loop_behind_its_stop()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            long reads = 0;

            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                var monitor = new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromMilliseconds(5),
                    readV4: () => new FamilyReading((ulong)Interlocked.Increment(ref reads)),
                    readV6: () => FamilyReading.Unreadable);

                // What a monitor added while the collector is starting gets: a start from its
                // registration and another from the Start under way.
                await monitor.StartAsync();
                await monitor.StartAsync();

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

                while (Interlocked.Read(ref reads) < 3 && DateTime.UtcNow < deadline)
                    await Task.Delay(5);

                await monitor.StopAsync();

                var afterStop = Interlocked.Read(ref reads);

                Assert.True(afterStop >= 3, "The monitor never sampled.");

                // Twenty periods: a loop the stop could not reach would have read many times.
                await Task.Delay(100);

                Assert.Equal(afterStop, Interlocked.Read(ref reads));

                monitor.Dispose();
            }
        }

        [Fact]
        public async Task Monitor_started_from_two_threads_at_once_leaves_no_loop_behind_its_stop()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                // The two starts of a monitor added while the collector is starting come from
                // two threads. Many rounds: the window in which one start could miss the token
                // source of the other is a few instructions wide.
                for (var round = 0; round < 200; round++)
                {
                    long reads = 0;

                    var monitor = new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromMilliseconds(1),
                        readV4: () => new FamilyReading((ulong)Interlocked.Increment(ref reads)),
                        readV6: () => FamilyReading.Unreadable);

                    using (var gate = new Barrier(2))
                    {
                        var starts = new[]
                        {
                            Task.Run(() => { gate.SignalAndWait(); return monitor.StartAsync().AsTask(); }),
                            Task.Run(() => { gate.SignalAndWait(); return monitor.StartAsync().AsTask(); }),
                        };

                        await Task.WhenAll(starts);
                    }

                    // A loop that waits for one nobody cancelled never ends, and the stop waits
                    // for it: that is how the race shows when the two starts are not one step.
                    var stop = monitor.StopAsync().AsTask();

                    Assert.True(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(15))) == stop, $"Round {round}: the stop never returned.");

                    var afterStop = Interlocked.Read(ref reads);

                    // Ten periods: a loop the stop could not reach would have read by now.
                    await Task.Delay(10);

                    Assert.True(afterStop == Interlocked.Read(ref reads), $"Round {round}: a loop went on sampling after the stop.");

                    monitor.Dispose();

                    Assert.False(await monitor.StartAsync(), "A disposed monitor was started.");
                }
            }
        }

        [Fact]
        public async Task Monitor_reports_when_neither_family_can_be_read()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var logger = new ErrorRecordingLogger();

            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                collector.AddCustomLogger(logger);

                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                var monitor = new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromMilliseconds(20),
                    readV4: () => FamilyReading.Unreadable,
                    readV6: () => FamilyReading.Unreadable);

                await collector.Start();
                await monitor.InitAsync();

                Assert.Contains(logger.Errors, e => e.Contains("neither IPv4 nor IPv6"));

                monitor.Dispose();
                await collector.Stop();
            }
        }

        [Fact]
        public void Monitor_reads_the_counter_of_this_machine()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            // The one line of the monitor that differs between the two builds of the collector:
            // the OS read behind IPGlobalProperties. IPv4 is always installed.
            var reading = WindowsTcpFailureRateMonitor.ReadFamily(properties => properties.GetTcpIPv4Statistics());

            Assert.True(reading.Readable, "The IPv4 TCP statistics of this machine could not be read.");
        }

        [Fact]
        public async Task Monitor_does_not_post_what_failed_while_it_was_stopped()
        {
            long v4 = 100;

            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                var monitor = new WindowsTcpFailureRateMonitor(storage, TimeSpan.FromHours(1),
                    readV4: () => new FamilyReading((ulong)Interlocked.Read(ref v4)),
                    readV6: () => FamilyReading.Unreadable);

                // First run: the baseline is 100.
                await monitor.InitAsync();
                await monitor.StartAsync();
                await monitor.StopAsync();

                // 5000 attempts fail while the monitor is stopped. The next run starts from a
                // baseline of its own, so they are nobody's increase.
                Interlocked.Exchange(ref v4, 5100);

                await monitor.InitAsync();

                Assert.Equal(0, monitor.PushedTotal);

                monitor.Dispose();
            }
        }

        [Fact]
        public void Adding_the_sensor_twice_keeps_one_monitor()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var logger = new ErrorRecordingLogger();

            using (var collector = CreateCollector(new RateRecordingSender()))
            {
                collector.AddCustomLogger(logger);

                collector.Windows.AddNetworkConnectionFailuresRate();
                collector.Windows.AddNetworkConnectionFailuresRate();

                // The same request again changes nothing and is nothing to report.
                Assert.DoesNotContain(logger.Errors, e => e.Contains("already added"));

                collector.Windows.AddNetworkConnectionFailuresRate(TimeSpan.FromSeconds(5));

                Assert.Single(collector.DefaultSensors.Where(s => s.SensorPath == WindowsTcpFailureRateMonitor.MonitorPath));
                Assert.Single(collector.DefaultSensors.Where(s => s.SensorPath.EndsWith("/Connection failures rate", StringComparison.Ordinal)));

                // The first period stays (the rule of both collectors, #1752; the native case is
                // native_tcp_failure_rate_repeated_enable_keeps_the_first_period), and the host
                // is told that the second was not applied.
                var monitor = (WindowsTcpFailureRateMonitor)collector.DefaultSensors.Single(s => s.SensorPath == WindowsTcpFailureRateMonitor.MonitorPath);

                Assert.Equal(TimeSpan.FromMinutes(1), monitor.Period);

                // A period that cannot be used throws on a repeated call as on the first.
                Assert.Throws<ArgumentOutOfRangeException>(() => collector.Windows.AddNetworkConnectionFailuresRate(TimeSpan.Zero));
                Assert.Contains(logger.Errors, e => e.Contains("already added with a period of 00:01:00") && e.Contains("00:00:05"));
            }
        }


        // The monitor and its rate sensor are registered one after the other, and the storage can
        // take the first and refuse the second. The two cases below are the two ways it does.

        [Fact]
        public async Task Monitor_refused_because_the_collector_began_to_stop_leaves_no_rate_sensor_behind()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var stopHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logger = new ErrorRecordingLogger();
            DataCollector collector = null;
            Task stop = null;

            // The storage says so when it has taken the rate sensor, and the monitor is not
            // registered yet: the stop begins exactly between the two registrations, on the
            // registering thread, and is held in Stopping until the test lets it go on.
            logger.OnInfo = message =>
            {
                if (stop == null && message.StartsWith("New sensor has been added", StringComparison.Ordinal)
                                 && message.EndsWith("/Connection failures rate", StringComparison.Ordinal))
                    stop = collector.Stop(stopHeld.Task);
            };

            using (collector = CreateCollector(new RateRecordingSender()))
            {
                collector.AddCustomLogger(logger);

                await collector.Start();

                collector.Windows.AddNetworkConnectionFailuresRate();

                Assert.True(stop != null, "The stop did not begin between the two registrations.");
                Assert.Equal(CollectorStatus.Stopping, collector.Status);
                Assert.Contains(logger.Errors, e => e.Contains("Cannot register sensor") && e.Contains(WindowsTcpFailureRateMonitor.MonitorPath));

                // The monitor was refused, and the rate sensor it had put into the storage left
                // with it: the next Start has no sensor to register that nothing would feed.
                Assert.DoesNotContain(collector.DefaultSensors, s => s.SensorPath == WindowsTcpFailureRateMonitor.MonitorPath);
                Assert.DoesNotContain(collector.DefaultSensors, s => s.SensorPath.EndsWith("/Connection failures rate", StringComparison.Ordinal));

                stopHeld.SetResult(true);
                await stop;

                // The next request, to a collector that takes registrations again, builds the pair.
                collector.Windows.AddNetworkConnectionFailuresRate();

                Assert.Single(collector.DefaultSensors.Where(s => s.SensorPath == WindowsTcpFailureRateMonitor.MonitorPath));
                Assert.Single(collector.DefaultSensors.Where(s => s.SensorPath.EndsWith("/Connection failures rate", StringComparison.Ordinal)));
            }
        }

        [Fact]
        public void Monitor_refused_at_the_sensor_limit_leaves_no_rate_sensor_behind()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var options = CreateOptions(new RateRecordingSender());

            using (var collector = new DataCollector(options))
            {
                // Room for the rate sensor and none for its monitor.
                options.MaxSensors = collector.DefaultSensors.Count() + 1;

                Assert.Throws<InvalidOperationException>(() => collector.Windows.AddNetworkConnectionFailuresRate());

                Assert.DoesNotContain(collector.DefaultSensors, s => s.SensorPath == WindowsTcpFailureRateMonitor.MonitorPath);
                Assert.DoesNotContain(collector.DefaultSensors, s => s.SensorPath.EndsWith("/Connection failures rate", StringComparison.Ordinal));

                // With room for both, the next request builds the pair.
                options.MaxSensors += 1;

                collector.Windows.AddNetworkConnectionFailuresRate();

                Assert.Single(collector.DefaultSensors.Where(s => s.SensorPath == WindowsTcpFailureRateMonitor.MonitorPath));
                Assert.Single(collector.DefaultSensors.Where(s => s.SensorPath.EndsWith("/Connection failures rate", StringComparison.Ordinal)));
            }
        }


        private static DataCollector CreateCollector(IDataSender sender) => new DataCollector(CreateOptions(sender));

        private static CollectorOptions CreateOptions(IDataSender sender)
        {
            return new CollectorOptions
            {
                AccessKey = "tcp-rate-key",
                ClientName = "tcp-rate-client",
                ComputerName = "tcp-rate-host",
                Module = "tcp-rate-module",
                DataSender = sender,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
            };
        }

        private sealed class ErrorRecordingLogger : HSMDataCollector.Logging.ICollectorLogger
        {
            private readonly object _lock = new object();
            private readonly List<string> _errors = new List<string>();

            /// <summary>Called with every Info line, on the thread that wrote it.</summary>
            internal Action<string> OnInfo { get; set; }

            internal List<string> Errors
            {
                get
                {
                    lock (_lock)
                        return _errors.ToList();
                }
            }

            public void Debug(string message) { }

            public void Info(string message) => OnInfo?.Invoke(message);

            public void Error(string message)
            {
                lock (_lock)
                    _errors.Add(message);
            }

            public void Error(Exception ex)
            {
                lock (_lock)
                    _errors.Add(ex.ToString());
            }
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
