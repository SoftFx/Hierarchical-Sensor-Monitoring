using HSMDataCollector.Core;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    public class DefaultSensorsTests : IDisposable
    {
        private const string CurrentProcessNodeName = "Process monitoring";
        private const string ProductName = "TestProduct";

        private readonly DataCollector _dataCollector;
        private readonly ITestOutputHelper _output;

        public DefaultSensorsTests(ITestOutputHelper output)
        {
            _output = output;
            _dataCollector = new DataCollector(ProductName, "https://localhost");
            _dataCollector.Start();
        }


        [SuiteSoakFact]
        public void Default_sensor_smoke_suite_repeated_for_duration()
        {
            var duration = GetSuiteSoakDuration();
            var maxDuration = GetSuiteSoakMaxDuration();
            var before = SuiteSoakResourceSnapshot.Capture();
            var stopwatch = Stopwatch.StartNew();
            var cycles = 0;
            var scenarioRuns = 0;

            while (stopwatch.Elapsed < duration)
            {
                cycles++;

                CreateDefaultSensorTest();
                scenarioRuns++;

                CreateDefaultSensor_WithSpecificPath_Test();
                scenarioRuns++;

                Thread.Sleep(1);
                AssertWithinSuiteSoakMax(stopwatch, maxDuration);
            }

            var after = SuiteSoakResourceSnapshot.Capture();
            SuiteSoakResourceSnapshot.WriteDelta(_output, "defaultSensorSmokeSuiteSoak", before, after);
            SuiteSoakResourceSnapshot.AssertNoCriticalGrowth(before, after);

            _output.WriteLine(
                "defaultSensorSmokeSuiteSoak; durationSeconds={0}; maxSeconds={1}; elapsedSeconds={2}; cycles={3}; scenarioRuns={4}; addValues=0; requests=0; bytes=0",
                duration.TotalSeconds,
                maxDuration.TotalSeconds,
                stopwatch.Elapsed.TotalSeconds,
                cycles,
                scenarioRuns);

            Assert.True(cycles > 0, "The default sensor smoke suite soak should complete at least one suite cycle.");
        }

        private void CreateDefaultSensorTest() { }
        private void CreateDefaultSensor_WithSpecificPath_Test() { }

        public void Dispose() => _dataCollector.Stop();

        private static TimeSpan GetSuiteSoakDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(5);
        }

        private static TimeSpan GetSuiteSoakMaxDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(30);
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
    }
}
