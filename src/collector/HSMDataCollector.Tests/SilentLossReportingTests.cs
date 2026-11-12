using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Windows.Process;
using HSMDataCollector.Logging;
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

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1659 A5 and A8: two places where the collector lost something and said nothing.
    /// A5: values evicted from a full queue were counted only by the <c>Queue overflow</c>
    /// sensor, so a collector without that sensor dropped them in silence.
    /// A8: a failed top-CPU sample was swallowed by a bare <c>catch</c>.
    /// </summary>
    public sealed class SilentLossReportingTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        // --- A5 ---

        [Fact]
        public async Task Overflow_without_the_overflow_sensor_is_reported_at_stop()
        {
            var logger = new RecordingLogger();

            using (var collector = CreateCollector(maxQueueSize: 5))
            {
                collector.AddCustomLogger(logger);
                var sensor = collector.CreateIntSensor("loss/overflow");
                await collector.Start();

                // The collect period is an hour, so nothing leaves the queue while it fills:
                // 12 values into a queue of 5 evict exactly 7.
                for (var i = 0; i < 12; i++)
                    sensor.AddValue(i);

                await collector.Stop();
            }

            var reports = logger.Errors.Where(e => e.Contains("dropped from the full send queue")).ToList();

            Assert.True(reports.Count == 1, "Expected one overflow report, got: " + string.Join(" | ", logger.Errors));
            Assert.Contains("7 value(s)", reports[0]);
            Assert.Contains("Queue overflow sensor not registered", reports[0]);
        }

        [Fact]
        public async Task Overflow_with_the_overflow_sensor_is_left_to_the_sensor()
        {
            var logger = new RecordingLogger();

            using (var collector = CreateCollector(maxQueueSize: 5))
            {
                collector.AddCustomLogger(logger);
                AddQueueOverflowSensor(collector);
                var sensor = collector.CreateIntSensor("loss/overflow");
                await collector.Start();

                for (var i = 0; i < 12; i++)
                    sensor.AddValue(i);

                await collector.Stop();
            }

            Assert.DoesNotContain(logger.Errors, e => e.Contains("dropped from the full send queue"));
        }

        [Fact]
        public async Task Run_without_overflow_reports_nothing()
        {
            var logger = new RecordingLogger();

            using (var collector = CreateCollector(maxQueueSize: 100))
            {
                collector.AddCustomLogger(logger);
                var sensor = collector.CreateIntSensor("loss/none");
                await collector.Start();

                sensor.AddValue(1);

                await collector.Stop();
            }

            Assert.DoesNotContain(logger.Errors, e => e.Contains("dropped from the full send queue"));
        }

        [Fact]
        public async Task Overflow_count_is_not_carried_into_the_next_run()
        {
            var logger = new RecordingLogger();

            using (var collector = CreateCollector(maxQueueSize: 5))
            {
                collector.AddCustomLogger(logger);
                var sensor = collector.CreateIntSensor("loss/overflow");
                await collector.Start();

                for (var i = 0; i < 12; i++)
                    sensor.AddValue(i);

                await collector.Stop();
                await collector.Start();

                sensor.AddValue(100);

                await collector.Stop();
            }

            Assert.Single(logger.Errors.Where(e => e.Contains("dropped from the full send queue")));
        }

        // --- A8 ---

        [Fact]
        public async Task Failed_top_cpu_sample_is_reported_once_per_window()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var logger = new RecordingLogger();
            var samples = 0;

            using (var collector = CreateCollector(maxQueueSize: 100))
            {
                collector.AddCustomLogger(logger);

                var storage = (SensorsStorage)typeof(DataCollector)
                    .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                var monitor = new WindowsTopCpuMonitor(storage, count: 3, minPercent: 1.0, period: TimeSpan.FromMilliseconds(20),
                    listProcesses: () =>
                    {
                        Interlocked.Increment(ref samples);
                        throw new InvalidOperationException("process list is unavailable");
                    });

                await collector.Start();
                await monitor.InitAsync();
                await monitor.StartAsync();

                Assert.True(await WaitForAsync(() => Volatile.Read(ref samples) >= 4), "The monitor did not sample.");

                await monitor.StopAsync();
                monitor.Dispose();
                await collector.Stop();
            }

            var reports = logger.Errors.Where(e => e.Contains("top-CPU sampling error")).ToList();

            Assert.True(reports.Count == 1, "Expected one sampling error report, got: " + string.Join(" | ", logger.Errors));
            Assert.Contains("process list is unavailable", reports[0]);
        }


        private static DataCollector CreateCollector(int maxQueueSize)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "loss-key",
                ClientName = "loss-client",
                ComputerName = "loss-host",
                Module = "loss-module",
                DataSender = new AcceptingSender(),
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromHours(1),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static void AddQueueOverflowSensor(DataCollector collector)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                collector.Windows.AddQueueOverflow();
            else
                collector.Unix.AddQueueOverflow();
        }

        private static async Task<bool> WaitForAsync(Func<bool> done)
        {
            var deadline = DateTime.UtcNow + Timeout;

            while (!done())
            {
                if (DateTime.UtcNow >= deadline)
                    return false;

                await Task.Delay(10).ConfigureAwait(false);
            }

            return true;
        }


        private sealed class AcceptingSender : IDataSender
        {
            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Accepted();

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Accepted();

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => Accepted();

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => Accepted();

            private static ValueTask<PackageSendingInfo> Accepted() =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));
        }


        private sealed class RecordingLogger : ICollectorLogger
        {
            private readonly List<string> _errors = new List<string>();

            internal List<string> Errors
            {
                get
                {
                    lock (_errors)
                        return _errors.ToList();
                }
            }

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Error(string message)
            {
                lock (_errors)
                    _errors.Add(message);
            }

            public void Error(Exception ex)
            {
                lock (_errors)
                    _errors.Add(ex.ToString());
            }
        }
    }
}
