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
    /// ".module/Collector queue stats/Package process time" (#1480): for each sent package, the
    /// average time in seconds its values waited in the send queue before the package was collected
    /// (<see cref="PackageInfo.AvrTimeInQueue"/>). The native collector measures the same quantity
    /// (it used to post the HTTP send duration) and is covered by
    /// native_package_process_time_is_the_average_queue_wait; the registered unit and description are
    /// pinned cross-language by default_sensors_contract:queue_process_time_registers_in_seconds.
    /// </summary>
    public class PackageProcessTimeSensorTests : IDisposable
    {
        private readonly DataCollector _collector = new DataCollector(new CollectorOptions
        {
            AccessKey = "test-key",
            ServerAddress = "https://localhost",
            Port = 443,
        });


        public void Dispose() => _collector.Dispose();


        [Fact]
        public void Package_process_time_registers_seconds_and_names_them()
        {
            var options = new PackageProcessTimePrototype().ApplyOptions(new CollectorOptions()).Get(null);

            Assert.Equal(Unit.Seconds, options.SensorUnit);
            Assert.Equal(
                "The sensor sends, for each sent package, the average time in seconds its values waited in the send queue " +
                "before the package was collected. Bar period is 5 minutes with updates every 5 seconds. " +
                "Package collect period = **15 seconds**.",
                options.Description);
        }


        [Fact]
        public void Package_wait_is_the_average_of_package_time_minus_enqueue_time()
        {
            // The package reads its collect time at construction, after these stamps, so each wait is
            // at least the offset below: 4 s and 1 s average to 2.5 s (plus the few ticks in between).
            var now = DateTime.UtcNow;
            var package = new DataPackage<int>(2);

            package.AddValue(new QueueItem<int>(1, now.AddSeconds(-4)));
            package.AddValue(new QueueItem<int>(2, now.AddSeconds(-1)));

            var info = package.GetInfo();

            Assert.Equal(2, info.ValuesCount);
            Assert.InRange(info.AvrTimeInQueue, 2.5, 3.0);
        }


        [Fact]
        public void Empty_package_reports_no_wait()
        {
            var info = new DataPackage<int>(0).GetInfo();

            Assert.Equal(0, info.ValuesCount);
            Assert.Equal(0.0, info.AvrTimeInQueue);
        }


        [Fact]
        public void Bar_takes_each_package_average_and_the_comment_smooths_per_queue()
        {
            var options = new PackageProcessTimePrototype().ApplyOptions(new CollectorOptions()).Get(null);
            options.DataProcessor = DataProcessorOf(_collector);

            var sensor = new PackageDataAvrProcessTimeSensor(options);

            sensor.AddValue("Data", new PackageInfo(2.0, 1));
            sensor.AddValue("Data", new PackageInfo(12.0, 2)); // a 2-value package averaging 6 s
            sensor.AddValue("Data", new PackageInfo(8.0, 1));
            sensor.AddValue("Priority data", new PackageInfo(3.0, 1));

            // The bar aggregates the raw per-package averages: 2, 6, 8 and 3.
            var bar = sensor.Current;
            Assert.Equal(4, bar.Count);
            Assert.Equal(2.0, bar.Min);
            Assert.Equal(8.0, bar.Max);

            // Only the bar COMMENT is smoothed, per queue, as (old + new) / 2: ((2 + 6) / 2 + 8) / 2 = 6.
            var comment = CommentOf(sensor);
            Assert.Contains("Data: 6", comment);
            Assert.Contains("Priority data: 3", comment);
        }


        private static string CommentOf(PackageDataAvrProcessTimeSensor sensor) =>
            (string)typeof(PackageDataAvrProcessTimeSensor)
                .GetMethod("GetComment", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sensor, null);

        private static DataProcessor DataProcessorOf(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);
    }
}
