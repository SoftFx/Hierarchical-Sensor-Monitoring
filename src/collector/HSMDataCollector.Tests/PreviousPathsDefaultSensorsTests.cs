using System;
using System.Collections.Generic;
using HSMDataCollector.Converters;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Sensor move (#1724): <c>PreviousPaths</c> reaches a default sensor built from the caller's
    /// options, and a helper that builds several sensors from ONE options object refuses options
    /// that carry previous paths — a shared list would move one sensor's history into whichever
    /// of them registered first.
    /// </summary>
    public sealed class PreviousPathsDefaultSensorsTests
    {
        [Fact]
        public void A_single_default_sensor_carries_the_callers_previous_paths()
        {
            var prototypes = new PrototypesCollection(new CollectorOptions { ComputerName = "host", Module = "module" }, null);

            var registration = prototypes.TotalCPU.Get(new BarSensorOptions
            {
                PreviousPaths = new List<string> { "garage-server/System monitoring/Total CPU" },
            }).ToApi();

            Assert.Equal(new[] { "garage-server/System monitoring/Total CPU" }, registration.PreviousPaths);
        }

        [Fact]
        public void A_default_sensor_without_previous_paths_sends_none()
        {
            var prototypes = new PrototypesCollection(new CollectorOptions { ComputerName = "host", Module = "module" }, null);

            Assert.Null(prototypes.TotalCPU.Get(new BarSensorOptions()).ToApi().PreviousPaths);
            Assert.Null(prototypes.TotalCPU.Get(null).ToApi().PreviousPaths);
        }

        [Fact]
        public void Helpers_that_build_several_sensors_refuse_previous_paths()
        {
            var paths = new List<string> { "old/path" };

            using (var collector = new DataCollector(new CollectorOptions { AccessKey = "key", ComputerName = "host", Module = "module" }))
            {
                Assert.Throws<ArgumentException>(() => collector.Windows.AddSystemMonitoringSensors(new BarSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Windows.AddProcessMonitoringSensors(new BarSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Windows.AddAllDisksMonitoringSensors(new DiskSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Windows.AddFreeDisksSpace(new DiskSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Windows.AddAllQueueDiagnosticSensors(new BarSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Windows.AddAllNetworkSensors(new NetworkSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Windows.AddAllWindowsLogs(new InstantSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Unix.AddSystemMonitoringSensors(new BarSensorOptions { PreviousPaths = paths }));
                Assert.Throws<ArgumentException>(() => collector.Unix.AddDiskMonitoringSensors(new DiskSensorOptions { PreviousPaths = paths }));

                // Only the bar options carry the paths: refused before any free-space sensor is added.
                Assert.Throws<ArgumentException>(() => collector.Windows.AddAllDisksMonitoringSensors(null, new DiskBarSensorOptions { PreviousPaths = paths }));

                // The wire's blank rule (ASCII whitespace): a non-breaking space is a real entry.
                Assert.Throws<ArgumentException>(() => collector.Windows.AddSystemMonitoringSensors(
                    new BarSensorOptions { PreviousPaths = new List<string> { "\u00A0" } }));
            }
        }

        [Fact]
        public void Blank_previous_paths_do_not_trip_the_guard()
        {
            var options = new BarSensorOptions { PreviousPaths = new List<string> { null, " " } };

            Assert.Same(options, SensorOptions.ForSeveralSensors(options, "helper"));
            Assert.Null(SensorOptions.ForSeveralSensors<BarSensorOptions>(null, "helper"));
        }
    }
}
