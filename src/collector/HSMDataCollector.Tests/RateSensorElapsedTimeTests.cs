using System;
using System.Diagnostics;
using System.Reflection;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.Sensors;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-E2: the rate sensor divided the accumulated sum by the CONFIGURED PostDataPeriod, not by
    /// the time that actually elapsed since the previous sample. After machine sleep/suspend the
    /// scheduler skips missed ticks, so the sum spans the whole gap while the divisor stays one
    /// period — a single sample inflated by gap/period.
    /// </summary>
    public sealed class RateSensorElapsedTimeTests
    {
        private static readonly MethodInfo GetValueMethod =
            typeof(MonitoringRateSensor).GetMethod("GetValue", BindingFlags.Instance | BindingFlags.NonPublic);

        [Fact]
        public void Sample_after_a_gap_is_divided_by_actual_elapsed_time()
        {
            long nowTicks = 100L * Stopwatch.Frequency;

            var sensor = CreateRateSensor(postDataPeriod: TimeSpan.FromSeconds(10), () => nowTicks);

            // Baseline sample establishes the "previous tick" timestamp.
            InvokeGetValue(sensor);

            // 50 seconds pass (e.g. machine sleep) while 100 units accumulate.
            sensor.AddValue(100);
            nowTicks += 50L * Stopwatch.Frequency;

            // Rate must be 100 / 50s = 2/s, not 100 / 10s (configured period) = 10/s.
            Assert.Equal(2.0, InvokeGetValue(sensor), precision: 6);
        }

        [Fact]
        public void Regular_cadence_matches_the_configured_period()
        {
            long nowTicks = 100L * Stopwatch.Frequency;

            var sensor = CreateRateSensor(postDataPeriod: TimeSpan.FromSeconds(10), () => nowTicks);

            InvokeGetValue(sensor);

            sensor.AddValue(50);
            nowTicks += 10L * Stopwatch.Frequency;

            Assert.Equal(5.0, InvokeGetValue(sensor), precision: 6);
        }

        [Fact]
        public void First_sample_falls_back_to_the_configured_period()
        {
            long nowTicks = 123 * Stopwatch.Frequency;

            var sensor = CreateRateSensor(postDataPeriod: TimeSpan.FromSeconds(10), () => nowTicks);

            sensor.AddValue(100);

            // No previous tick to measure from -> divide by the configured period (legacy behavior).
            Assert.Equal(10.0, InvokeGetValue(sensor), precision: 6);
        }

        [Fact]
        public void Zero_elapsed_does_not_divide_by_zero()
        {
            long nowTicks = 100L * Stopwatch.Frequency;

            var sensor = CreateRateSensor(postDataPeriod: TimeSpan.FromSeconds(10), () => nowTicks);

            InvokeGetValue(sensor);
            sensor.AddValue(100);

            // Same timestamp as the previous tick -> fall back to the configured period.
            Assert.Equal(10.0, InvokeGetValue(sensor), precision: 6);
        }

        [Fact]
        public void Restart_resets_the_elapsed_baseline()
        {
            long nowTicks = 100L * Stopwatch.Frequency;

            var sensor = CreateRateSensor(postDataPeriod: TimeSpan.FromSeconds(10), () => nowTicks);

            InvokeGetValue(sensor); // Baseline tick of the first run.

            // The sensor is stopped for an hour, then restarted (InitAsync). The first sample of
            // the new run must NOT divide by the whole stopped gap — that would deflate the rate
            // by gap/period (100 / 3600 instead of 100 / 10).
            nowTicks += 3600L * Stopwatch.Frequency;
            sensor.InitAsync().GetAwaiter().GetResult();

            sensor.AddValue(100);

            Assert.Equal(10.0, InvokeGetValue(sensor), precision: 6);
        }

        [Fact]
        public void Concurrent_adds_are_fully_conserved_across_ticks()
        {
            // 4 writers x 10k AddValue(1) race against periodic ticks. Every added unit must be
            // accounted for in exactly one emitted sample: sum(rate * elapsed) == 40000. Uses an
            // 8-second step so the divide/multiply round-trip is exact in binary floating point.
            long nowTicks = 100L * Stopwatch.Frequency;
            var sensor = CreateRateSensor(postDataPeriod: TimeSpan.FromSeconds(8), () => Volatile.Read(ref nowTicks));

            InvokeGetValue(sensor); // Baseline tick.

            const int writers = 4;
            const int addsPerWriter = 10_000;
            var collected = 0.0;

            var threads = new Thread[writers];
            for (var i = 0; i < writers; i++)
            {
                threads[i] = new Thread(() =>
                {
                    for (var n = 0; n < addsPerWriter; n++)
                        sensor.AddValue(1);
                });
                threads[i].Start();
            }

            while (Array.Exists(threads, t => t.IsAlive))
            {
                Interlocked.Add(ref nowTicks, 8L * Stopwatch.Frequency);
                collected += InvokeGetValue(sensor) * 8.0;
                Thread.Sleep(1);
            }

            foreach (var thread in threads)
                thread.Join();

            // Drain whatever accumulated after the last mid-flight tick.
            Interlocked.Add(ref nowTicks, 8L * Stopwatch.Frequency);
            collected += InvokeGetValue(sensor) * 8.0;

            Assert.Equal(writers * (double)addsPerWriter, collected, precision: 6);
        }

        private static double InvokeGetValue(MonitoringRateSensor sensor) =>
            (double)GetValueMethod.Invoke(sensor, null);

        private static MonitoringRateSensor CreateRateSensor(TimeSpan postDataPeriod, Func<long> timestampProvider)
        {
            using (var collector = new DataCollector(new CollectorOptions
            {
                AccessKey = "rate-elapsed-key",
                ClientName = "rate-elapsed-client",
                ComputerName = "rate-elapsed-host",
                Module = "rate-elapsed-module",
                DataSender = new NullDataSender(),
            }))
            {
                var dataProcessor = (DataProcessor)typeof(DataCollector)
                    .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector);

                var sensor = new MonitoringRateSensor(new RateSensorOptions
                {
                    ComputerName = collector.ComputerName,
                    Module = collector.Module,
                    Path = "rate-elapsed/" + Guid.NewGuid().ToString("N"),
                    PostDataPeriod = postDataPeriod,
                    DataProcessor = dataProcessor,
                })
                {
                    TimestampProvider = timestampProvider,
                };

                return sensor;
            }
        }

        private sealed class NullDataSender : IDataSender
        {
            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() =>
                new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendCommandAsync(IEnumerable<HSMSensorDataObjects.CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;
        }
    }
}
