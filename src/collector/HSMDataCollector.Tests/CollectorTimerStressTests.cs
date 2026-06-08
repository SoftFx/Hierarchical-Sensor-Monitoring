using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    [CollectionDefinition(CollectionName, DisableParallelization = true)]
    public sealed class CollectorCpuSensitiveCollectionDefinition
    {
        public const string CollectionName = "Collector CPU-sensitive tests";
    }

    [Collection(CollectorCpuSensitiveCollectionDefinition.CollectionName)]
    public sealed class CollectorTimerStressTests
    {
        private readonly ITestOutputHelper _output;

        public CollectorTimerStressTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Function_sensors_with_varied_timer_periods_fire_without_cpu_spin()
        {
            var sender = new CountingDataSender();
            var periods = new[]
            {
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(60),
                TimeSpan.FromMilliseconds(90),
                TimeSpan.FromMilliseconds(130),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(300),
                TimeSpan.FromMilliseconds(500)
            };
            var counts = new int[periods.Length];

            using (var collector = CreateCollector(sender, "timer-varied-periods"))
            {
                for (var index = 0; index < periods.Length; index++)
                {
                    var localIndex = index;
                    collector.CreateFunctionSensor(
                        "timer/varied/" + index.ToString(CultureInfo.InvariantCulture),
                        () => Interlocked.Increment(ref counts[localIndex]),
                        new FunctionSensorOptions
                        {
                            PostDataPeriod = periods[index]
                        });
                }

                await collector.Start().ConfigureAwait(false);

                var cpuStart = CpuUsageSnapshot.Capture();
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                var cpu = CpuUsageSnapshot.Capture().Subtract(cpuStart);

                await collector.Stop().ConfigureAwait(false);

                WriteTimerStats("varied-periods", counts, periods, cpu, sender);

                Assert.All(counts, count => Assert.True(count > 0, "Every function sensor timer should fire at least once."));
                Assert.True(counts[0] > counts[counts.Length - 1], "Shorter timer period should produce more callbacks than the slowest period.");
                AssertCpuBudget(cpu, TimeSpan.FromSeconds(4), "varied function timer periods");
                Assert.True(sender.DataPackages > 0, "Timer-generated values should reach the data sender.");
            }
        }

        [Fact]
        public async Task Restarting_function_timer_under_load_changes_rate_without_callback_overlap()
        {
            var sender = new CountingDataSender();
            var count = 0;
            var inFlight = 0;
            var maxConcurrent = 0;

            using (var collector = CreateCollector(sender, "timer-restart"))
            {
                var sensor = collector.CreateFunctionSensor(
                    "timer/restart/function",
                    () =>
                    {
                        var concurrent = Interlocked.Increment(ref inFlight);
                        UpdateMax(ref maxConcurrent, concurrent);

                        try
                        {
                            Thread.Sleep(40);
                            return Interlocked.Increment(ref count);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref inFlight);
                        }
                    },
                    new FunctionSensorOptions
                    {
                        PostDataPeriod = TimeSpan.FromMilliseconds(100)
                    });

                await collector.Start().ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);
                var beforeRestart = Volatile.Read(ref count);

                var cpuStart = CpuUsageSnapshot.Capture();
                sensor.RestartTimer(TimeSpan.FromMilliseconds(25));
                await Task.Delay(TimeSpan.FromMilliseconds(900)).ConfigureAwait(false);
                var cpu = CpuUsageSnapshot.Capture().Subtract(cpuStart);

                await collector.Stop().ConfigureAwait(false);

                var afterRestart = Volatile.Read(ref count) - beforeRestart;
                _output.WriteLine(
                    "timerRestart; beforeRestartCallbacks={0}; afterRestartCallbacks={1}; maxConcurrent={2}; dataPackages={3}; dataValues={4}; wallMs={5}; cpuMs={6}; cpuCores={7:F3}",
                    beforeRestart,
                    afterRestart,
                    maxConcurrent,
                    sender.DataPackages,
                    sender.DataValues,
                    cpu.Wall.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cpu.Cpu.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cpu.CpuCores);

                Assert.True(beforeRestart > 0, "The timer should fire before restart.");
                Assert.True(afterRestart > beforeRestart, "Restarting to a shorter period should increase callback rate.");
                Assert.True(maxConcurrent == 1, "Scheduled function callbacks should not overlap when callback duration is longer than the period.");
                AssertCpuBudget(cpu, TimeSpan.FromSeconds(4), "function timer restart under load");
            }
        }

        [Fact]
        public async Task Thousand_function_sensor_timers_do_not_burn_cpu_or_threads()
        {
            const int sensorCount = 1000;

            var sender = new CountingDataSender();
            var totalCallbacks = 0;
            var threadsBefore = GetThreadCount();

            using (var collector = CreateCollector(sender, "timer-1000-function-sensors"))
            {
                for (var index = 0; index < sensorCount; index++)
                {
                    collector.CreateFunctionSensor(
                        "timer/many/" + index.ToString(CultureInfo.InvariantCulture),
                        () => Interlocked.Increment(ref totalCallbacks),
                        new FunctionSensorOptions
                        {
                            PostDataPeriod = TimeSpan.FromMilliseconds(100)
                        });
                }

                await collector.Start().ConfigureAwait(false);

                var cpuBaseline = await CaptureCpuBaselineAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                var cpuStart = CpuUsageSnapshot.Capture();
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                var cpu = CpuUsageSnapshot.Capture().Subtract(cpuStart);
                var adjustedCpu = cpu.SubtractBackground(cpuBaseline);
                var threadsAfter = GetThreadCount();

                await collector.Stop().ConfigureAwait(false);

                var callbacks = Volatile.Read(ref totalCallbacks);
                var threadDelta = threadsAfter - threadsBefore;

                _output.WriteLine(
                    "timerScale; sensors={0}; periodMs=100; callbacks={1}; dataPackages={2}; dataValues={3}; threadsBefore={4}; threadsAfter={5}; threadDelta={6}; wallMs={7}; cpuMs={8}; baselineCpuMs={9}; adjustedCpuMs={10}; adjustedCpuCores={11:F3}",
                    sensorCount,
                    callbacks,
                    sender.DataPackages,
                    sender.DataValues,
                    threadsBefore,
                    threadsAfter,
                    threadDelta,
                    cpu.Wall.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cpu.Cpu.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cpuBaseline.Cpu.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    adjustedCpu.Cpu.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    adjustedCpu.CpuCores);

                Assert.True(callbacks >= sensorCount * 5,
                    $"The scale test should exercise many timer callbacks. Callbacks={callbacks}.");
                Assert.True(threadDelta < 96,
                    $"Creating {sensorCount} periodic sensors should not create a thread per timer. Thread delta={threadDelta}.");
                AssertCpuBudget(adjustedCpu, TimeSpan.FromSeconds(1), "1000 function sensor timers");
            }
        }

        private static DataCollector CreateCollector(CountingDataSender sender, string module)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "timer-stress-test-key",
                ClientName = "timer-stress-test-client",
                ComputerName = "timer-stress-test-host",
                Module = module,
                DataSender = sender,
                MaxQueueSize = 20000,
                MaxValuesInPackage = 100,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(25),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 200
            });
        }

        private static int GetThreadCount()
        {
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                return process.Threads.Count;
            }
        }

        private void WriteTimerStats(string scenario, IReadOnlyList<int> counts, IReadOnlyList<TimeSpan> periods, CpuUsageSnapshot cpu, CountingDataSender sender)
        {
            var timerCounts = string.Join(
                ",",
                counts.Select((count, index) => periods[index].TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + "ms=" + count.ToString(CultureInfo.InvariantCulture)));

            _output.WriteLine(
                "timerStress; scenario={0}; callbacks={1}; dataPackages={2}; dataValues={3}; wallMs={4}; cpuMs={5}; cpuCores={6:F3}",
                scenario,
                timerCounts,
                sender.DataPackages,
                sender.DataValues,
                cpu.Wall.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                cpu.Cpu.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                cpu.CpuCores);
        }

        private static void AssertCpuBudget(CpuUsageSnapshot cpu, TimeSpan maxCpu, string scenario)
        {
            Assert.True(cpu.Cpu <= maxCpu,
                $"{scenario} should not burn excessive CPU. CPU={cpu.Cpu}, wall={cpu.Wall}.");
        }

        private static async Task<CpuUsageSnapshot> CaptureCpuBaselineAsync(TimeSpan duration)
        {
            var before = CpuUsageSnapshot.Capture();
            await Task.Delay(duration).ConfigureAwait(false);
            return CpuUsageSnapshot.Capture().Subtract(before);
        }

        private static void UpdateMax(ref int target, int value)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref target);

                if (value <= snapshot)
                    return;

                if (Interlocked.CompareExchange(ref target, value, snapshot) == snapshot)
                    return;
            }
        }

        private sealed class CountingDataSender : IDataSender
        {
            private int _dataPackages;
            private int _dataValues;
            private int _commandPackages;

            public int DataPackages => Volatile.Read(ref _dataPackages);

            public int DataValues => Volatile.Read(ref _dataValues);

            public int CommandPackages => Volatile.Read(ref _commandPackages);

            public void Dispose()
            {
            }

            public ValueTask<ConnectionResult> TestConnectionAsync()
            {
                return new ValueTask<ConnectionResult>(ConnectionResult.Ok);
            }

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                var values = items?.Count() ?? 0;
                Interlocked.Increment(ref _dataPackages);
                Interlocked.Add(ref _dataValues, values);

                return new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                return SendDataAsync(items, token);
            }

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                Interlocked.Increment(ref _commandPackages);
                return new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));
            }

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
            {
                return new ValueTask<PackageSendingInfo>(default(PackageSendingInfo));
            }
        }

        private sealed class CpuUsageSnapshot
        {
            private CpuUsageSnapshot(TimeSpan cpu, long timestamp, TimeSpan wall)
            {
                Cpu = cpu;
                Timestamp = timestamp;
                Wall = wall;
            }

            public TimeSpan Cpu { get; }

            private long Timestamp { get; }

            public TimeSpan Wall { get; }

            public double CpuCores => Wall.TotalMilliseconds <= 0
                ? 0
                : Cpu.TotalMilliseconds / Wall.TotalMilliseconds;

            public static CpuUsageSnapshot Capture()
            {
                using (var process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    return new CpuUsageSnapshot(process.TotalProcessorTime, Stopwatch.GetTimestamp(), TimeSpan.Zero);
                }
            }

            public CpuUsageSnapshot Subtract(CpuUsageSnapshot before)
            {
                var cpu = Cpu - before.Cpu;
                var wall = TimeSpan.FromSeconds((Timestamp - before.Timestamp) / (double)Stopwatch.Frequency);

                return new CpuUsageSnapshot(cpu, 0, wall);
            }

            public CpuUsageSnapshot SubtractBackground(CpuUsageSnapshot background)
            {
                if (background == null || background.Wall <= TimeSpan.Zero || Wall <= TimeSpan.Zero)
                    return this;

                var backgroundCpuTicks = background.Cpu.Ticks * Wall.TotalMilliseconds / background.Wall.TotalMilliseconds;
                var adjustedTicks = Cpu.Ticks - (long)backgroundCpuTicks;

                if (adjustedTicks < 0)
                    adjustedTicks = 0;

                return new CpuUsageSnapshot(TimeSpan.FromTicks(adjustedTicks), 0, Wall);
            }
        }
    }
}
