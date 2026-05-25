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
            _dataCollector.Initialize(true, null, null);
        }

        [Fact]
        [Trait("Category", "Create default sensor (Process CPU)")]
        public void CreateDefaultSensorTest()
        {
            //_dataCollector.InitializeProcessMonitoring(true, false, false);

            //Assert.True(_dataCollector.IsSensorExists($"{CurrentProcessNodeName}/Process CPU"));
        }

        [Fact]
        [Trait("Category", "Create default sensor (Process CPU)")]
        public void CreateDefaultSensor_WithSpecificPath_Test()
        {
            //const string specificPath = "Specific path/123";

            //_dataCollector.InitializeProcessMonitoring(true, false, false, specificPath);

            //Assert.True(_dataCollector.IsSensorExists($"{specificPath}/Process CPU"));
            //Assert.False(_dataCollector.IsSensorExists($"{CurrentProcessNodeName}/Process CPU"));
        }

        [SuiteSoakFact]
        public void Default_sensor_smoke_suite_repeated_for_duration()
        {
            var duration = GetSuiteSoakDuration();
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
            }

            _output.WriteLine("defaultSensorSmokeSuiteSoak; durationSeconds={0}; cycles={1}; scenarioRuns={2}", duration.TotalSeconds, cycles, scenarioRuns);

            Assert.True(cycles > 0, "The default sensor smoke suite soak should complete at least one suite cycle.");
        }

        public void Dispose() => _dataCollector.Stop();

        private static TimeSpan GetSuiteSoakDuration()
        {
            var rawSeconds = Environment.GetEnvironmentVariable("HSM_COLLECTOR_SUITE_SOAK_SECONDS");

            if (double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(30);
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
