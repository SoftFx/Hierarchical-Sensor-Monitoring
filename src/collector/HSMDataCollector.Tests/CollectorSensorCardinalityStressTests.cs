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
    public sealed class CollectorSensorCardinalityStressTests
    {
        private readonly ITestOutputHelper _output;

        public CollectorSensorCardinalityStressTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void High_cardinality_mixed_sensors_register_quickly_without_resource_spike()
        {
            const int sensorCount = 5000;

            var before = SuiteSoakResourceSnapshot.Capture();
            var result = RunRegistrationCycle(sensorCount, maxSensors: 100000, cycle: 1, rejectOverflow: false);
            var after = SuiteSoakResourceSnapshot.Capture();

            SuiteSoakResourceSnapshot.WriteDelta(_output, "fastCardinalitySmoke", before, after);
            WriteCycle("fastCardinalitySmoke", result);

            Assert.Equal(sensorCount, result.RegisteredSensors);
            Assert.True(result.Elapsed < TimeSpan.FromSeconds(10),
                $"Registering {sensorCount} mixed sensors should remain a quick smoke test. Elapsed={result.Elapsed}.");
            Assert.True(after.ThreadCount - before.ThreadCount < 16,
                "Registering stopped sensors should not create timer threads.");
            Assert.True(after.ManagedAfterFullGc - before.ManagedAfterFullGc < 96L * 1024 * 1024,
                "Fast high-cardinality registration should not leave large managed memory after GC.");
        }

        [CardinalityStressFact]
        public void Default_max_sensors_allows_configured_boundary_and_rejects_next()
        {
            var maxSensors = GetPositiveIntEnvironment(
                "HSM_COLLECTOR_CARDINALITY_STRESS_SENSORS",
                new CollectorOptions().MaxSensors);

            var before = SuiteSoakResourceSnapshot.Capture();
            var result = RunRegistrationCycle(maxSensors, maxSensors, cycle: 1, rejectOverflow: true);
            var after = SuiteSoakResourceSnapshot.Capture();

            SuiteSoakResourceSnapshot.WriteDelta(_output, "boundaryCardinalityStress", before, after);
            WriteCycle("boundaryCardinalityStress", result);

            Assert.Equal(maxSensors, result.RegisteredSensors);
            Assert.True(result.OverflowRejected, "The sensor immediately above MaxSensors should be rejected.");
            Assert.True(after.ThreadCount - before.ThreadCount < 32,
                "Registering the full boundary while stopped should not create many threads.");
        }

        [CardinalityNightlyFact]
        public void Cardinality_registration_repeated_for_duration_stays_bounded()
        {
            var sensorCount = GetPositiveIntEnvironment(
                "HSM_COLLECTOR_CARDINALITY_NIGHTLY_SENSORS",
                new CollectorOptions().MaxSensors);
            var duration = GetDurationEnvironment("HSM_COLLECTOR_CARDINALITY_NIGHTLY_SECONDS", TimeSpan.FromMinutes(5));
            var maxDuration = GetDurationEnvironment("HSM_COLLECTOR_CARDINALITY_NIGHTLY_MAX_SECONDS", TimeSpan.FromMinutes(6));

            var before = SuiteSoakResourceSnapshot.Capture();
            SuiteSoakResourceSnapshot firstAfterCycle = null;
            SuiteSoakResourceSnapshot lastAfterCycle = null;
            var stopwatch = Stopwatch.StartNew();
            var cycles = 0;
            long registeredSensors = 0;
            long overflowRejects = 0;
            TimeSpan totalRegistrationTime = TimeSpan.Zero;

            while (stopwatch.Elapsed < duration)
            {
                cycles++;

                var result = RunRegistrationCycle(sensorCount, sensorCount, cycles, rejectOverflow: true);
                WriteCycle("nightlyCardinalityCycle", result);

                registeredSensors += result.RegisteredSensors;
                if (result.OverflowRejected)
                    overflowRejects++;
                totalRegistrationTime += result.Elapsed;

                var afterCycle = SuiteSoakResourceSnapshot.Capture();
                if (firstAfterCycle == null)
                    firstAfterCycle = afterCycle;
                lastAfterCycle = afterCycle;

                AssertWithinMax(stopwatch, maxDuration);
            }

            var after = SuiteSoakResourceSnapshot.Capture();
            SuiteSoakResourceSnapshot.WriteDelta(_output, "nightlyCardinality", before, after);

            Assert.NotNull(firstAfterCycle);
            Assert.NotNull(lastAfterCycle);
            AssertNoRepeatedCycleLeak(firstAfterCycle, lastAfterCycle);

            _output.WriteLine(
                "nightlyCardinality; durationSeconds={0}; maxSeconds={1}; elapsedSeconds={2}; cycles={3}; sensorsPerCycle={4}; registeredSensors={5}; overflowRejects={6}; totalRegistrationMs={7}",
                duration.TotalSeconds,
                maxDuration.TotalSeconds,
                stopwatch.Elapsed.TotalSeconds,
                cycles,
                sensorCount,
                registeredSensors,
                overflowRejects,
                totalRegistrationTime.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture));

            Assert.True(cycles > 0, "The cardinality nightly test should complete at least one full registration cycle.");
            Assert.Equal(cycles, overflowRejects);
        }

        private CardinalityCycleResult RunRegistrationCycle(int sensorCount, int maxSensors, int cycle, bool rejectOverflow)
        {
            var counters = new CardinalityCounters();
            var sender = new CountingDataSender();
            var stopwatch = Stopwatch.StartNew();
            var overflowRejected = false;

            using (var collector = CreateCollector(sender, maxSensors))
            {
                for (var index = 0; index < sensorCount; index++)
                    RegisterMixedSensor(collector, counters, cycle, index);

                Assert.Equal(sensorCount, collector.DefaultSensors.Count());

                if (rejectOverflow)
                {
                    var exception = Assert.Throws<InvalidOperationException>(() =>
                        collector.CreateDoubleSensor(BuildPath(cycle, sensorCount, "overflow")));

                    Assert.Contains("Maximum sensor count", exception.Message);
                    Assert.Equal(sensorCount, collector.DefaultSensors.Count());
                    overflowRejected = true;
                }
            }

            stopwatch.Stop();

            return new CardinalityCycleResult(
                cycle,
                sensorCount,
                counters.InstantSensors,
                counters.LastValueSensors,
                counters.BarSensors,
                counters.FunctionSensors,
                overflowRejected,
                stopwatch.Elapsed,
                sender.CommandPackages,
                sender.DataPackages,
                sender.DataValues);
        }

        private static DataCollector CreateCollector(CountingDataSender sender, int maxSensors)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "cardinality-stress-key",
                ClientName = "cardinality-stress-client",
                ComputerName = "cardinality-stress-host",
                Module = "cardinality-stress-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 100,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromMilliseconds(500),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
                MaxSensors = maxSensors
            });
        }

        private static void RegisterMixedSensor(DataCollector collector, CardinalityCounters counters, int cycle, int index)
        {
            var path = BuildPath(cycle, index, "sensor");

            switch (index % 8)
            {
                case 0:
                    collector.CreateDoubleSensor(path);
                    counters.InstantSensors++;
                    break;
                case 1:
                    collector.CreateIntSensor(path);
                    counters.InstantSensors++;
                    break;
                case 2:
                    collector.CreateStringSensor(path);
                    counters.InstantSensors++;
                    break;
                case 3:
                    collector.CreateBoolSensor(path);
                    counters.InstantSensors++;
                    break;
                case 4:
                    collector.CreateLastValueDoubleSensor(path, 0);
                    counters.LastValueSensors++;
                    break;
                case 5:
                    collector.CreateIntBarSensor(path, CreateSlowBarOptions());
                    counters.BarSensors++;
                    break;
                case 6:
                    collector.CreateDoubleBarSensor(path, CreateSlowBarOptions());
                    counters.BarSensors++;
                    break;
                default:
                    collector.CreateFunctionSensor(
                        path,
                        () => 1,
                        new FunctionSensorOptions
                        {
                            PostDataPeriod = TimeSpan.FromHours(1)
                        });
                    counters.FunctionSensors++;
                    break;
            }
        }

        private static BarSensorOptions CreateSlowBarOptions()
        {
            return new BarSensorOptions
            {
                BarPeriod = TimeSpan.FromHours(1),
                PostDataPeriod = TimeSpan.FromHours(1)
            };
        }

        private static string BuildPath(int cycle, int index, string suffix)
        {
            return "cardinality/" +
                   cycle.ToString(CultureInfo.InvariantCulture) +
                   "/" +
                   index.ToString(CultureInfo.InvariantCulture) +
                   "/" +
                   suffix;
        }

        private void WriteCycle(string scenario, CardinalityCycleResult result)
        {
            _output.WriteLine(
                "{0}; cycle={1}; registered={2}; instant={3}; lastValue={4}; bar={5}; function={6}; overflowRejected={7}; elapsedMs={8}; commands={9}; dataPackages={10}; dataValues={11}",
                scenario,
                result.Cycle,
                result.RegisteredSensors,
                result.InstantSensors,
                result.LastValueSensors,
                result.BarSensors,
                result.FunctionSensors,
                result.OverflowRejected,
                result.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                result.CommandPackages,
                result.DataPackages,
                result.DataValues);
        }

        private static void AssertNoRepeatedCycleLeak(SuiteSoakResourceSnapshot first, SuiteSoakResourceSnapshot last)
        {
            Assert.True(last.ThreadCount - first.ThreadCount < 32,
                "Thread count should stay bounded between repeated high-cardinality cycles.");
            Assert.True(last.ManagedAfterFullGc - first.ManagedAfterFullGc < 128L * 1024 * 1024,
                "Managed memory after full GC should not trend upward between repeated high-cardinality cycles.");

            if (first.HandleCount >= 0 && last.HandleCount >= 0)
                Assert.True(last.HandleCount - first.HandleCount < 200,
                    "Process handle count should stay bounded between repeated high-cardinality cycles.");
        }

        private static int GetPositiveIntEnvironment(string variableName, int defaultValue)
        {
            var rawValue = Environment.GetEnvironmentVariable(variableName);

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;

            return defaultValue;
        }

        private static TimeSpan GetDurationEnvironment(string variableName, TimeSpan defaultValue)
        {
            var rawValue = Environment.GetEnvironmentVariable(variableName);

            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return defaultValue;
        }

        private static void AssertWithinMax(Stopwatch stopwatch, TimeSpan maxDuration)
        {
            Assert.True(stopwatch.Elapsed <= maxDuration,
                $"Cardinality stress exceeded hard limit {maxDuration}. Target duration is soft, but exceeding the hard limit means the suite likely hung.");
        }

        private sealed class CardinalityStressFactAttribute : FactAttribute
        {
            public CardinalityStressFactAttribute()
            {
                if (!string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_CARDINALITY_STRESS"), "1", StringComparison.Ordinal))
                    Skip = "Set HSM_COLLECTOR_RUN_CARDINALITY_STRESS=1 to run the 100000-sensor boundary stress test.";
            }
        }

        private sealed class CardinalityNightlyFactAttribute : FactAttribute
        {
            public CardinalityNightlyFactAttribute()
            {
                if (!string.Equals(Environment.GetEnvironmentVariable("HSM_COLLECTOR_RUN_CARDINALITY_NIGHTLY"), "1", StringComparison.Ordinal))
                    Skip = "Set HSM_COLLECTOR_RUN_CARDINALITY_NIGHTLY=1 to run repeated high-cardinality cycles.";
            }
        }

        private sealed class CardinalityCounters
        {
            public int InstantSensors { get; set; }

            public int LastValueSensors { get; set; }

            public int BarSensors { get; set; }

            public int FunctionSensors { get; set; }
        }

        private sealed class CardinalityCycleResult
        {
            public CardinalityCycleResult(
                int cycle,
                int registeredSensors,
                int instantSensors,
                int lastValueSensors,
                int barSensors,
                int functionSensors,
                bool overflowRejected,
                TimeSpan elapsed,
                int commandPackages,
                int dataPackages,
                int dataValues)
            {
                Cycle = cycle;
                RegisteredSensors = registeredSensors;
                InstantSensors = instantSensors;
                LastValueSensors = lastValueSensors;
                BarSensors = barSensors;
                FunctionSensors = functionSensors;
                OverflowRejected = overflowRejected;
                Elapsed = elapsed;
                CommandPackages = commandPackages;
                DataPackages = dataPackages;
                DataValues = dataValues;
            }

            public int Cycle { get; }

            public int RegisteredSensors { get; }

            public int InstantSensors { get; }

            public int LastValueSensors { get; }

            public int BarSensors { get; }

            public int FunctionSensors { get; }

            public bool OverflowRejected { get; }

            public TimeSpan Elapsed { get; }

            public int CommandPackages { get; }

            public int DataPackages { get; }

            public int DataValues { get; }
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
    }
}
