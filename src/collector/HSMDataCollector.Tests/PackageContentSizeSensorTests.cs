using System;
using System.Reflection;

using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Diagnostic;
using HSMDataCollector.Prototypes.Collections;
using HSMDataCollector.SyncQueue.Data;

using HSMSensorDataObjects.SensorRequests;

using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// ".module/Collector queue stats/Package content size" reports KILOBYTES (#1459). It used to
    /// register MB, and a bar renders at 2-decimal precision, so a realistic package — a couple of
    /// kilobytes, 0.002 MB — rounded to 0.00: the sensor could not report anything but zero
    /// whatever the traffic. The native collector mirrors this (repo rule #10) and is covered by
    /// native_package_content_size_reports_kilobytes; the registered unit is pinned cross-language
    /// by default_sensors_contract:queue_content_size_registers_in_kilobytes.
    /// </summary>
    public class PackageContentSizeSensorTests : IDisposable
    {
        private readonly DataCollector _collector = new DataCollector(new CollectorOptions
        {
            AccessKey = "test-key",
            ServerAddress = "https://localhost",
            Port = 443,
        });


        public void Dispose() => _collector.Dispose();


        [Fact]
        public void Package_content_size_registers_kilobytes()
        {
            var options = new PackageContentSizePrototype().Get(null);

            Assert.Equal(Unit.KB, options.SensorUnit);
        }


        [Theory]
        // ContentSize counts CHARS, so a char is two bytes on the way to kilobytes:
        // 1024 chars = 2048 bytes = 2 KB. The middle row is the size from the issue — a ~2 KB
        // package, which read as a flat 0.00 while the sensor was in MB.
        [InlineData(1024, 2.0)]
        [InlineData(1075, 2.1)]
        [InlineData(512, 1.0)]
        public void Package_content_size_reports_kilobytes(double contentSize, double expectedKilobytes)
        {
            var options = new PackageContentSizePrototype().Get(null);
            options.DataProcessor = DataProcessorOf(_collector);

            var sensor = new PackageContentSizeSensor(options);

            sensor.AddValue(new PackageSendingInfo(contentSize));

            Assert.Equal(expectedKilobytes, sensor.Current.Mean, 1);
        }


        private static DataProcessor DataProcessorOf(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);
    }
}
