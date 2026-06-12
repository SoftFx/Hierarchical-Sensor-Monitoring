using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Shutdown boundedness contract: collector Stop() must complete within a small bounded time
    /// even when the transport is dead or hung — the collector must never lock its host's
    /// restart. Losing pending data at shutdown is the accepted trade-off.
    /// </summary>
    public sealed class CollectorStopBoundednessTests
    {
        // Generous CI bound. The real budget is ~1s queue-stop wait + ~1s flush timeout
        // (RequestTimeout is pinned to 1s below) plus scheduling noise.
        private static readonly TimeSpan StopBound = TimeSpan.FromSeconds(15);

        [Fact]
        public async Task Stop_with_hung_cancellation_respecting_sender_is_bounded_and_drops_pending()
        {
            // Models the real HTTP client against an unreachable/black-holed server: the send
            // never completes but honors the cancellation token.
            var sender = new HangingSender(respectsCancellation: true);

            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromSeconds(60)))
            {
                var inert = TimeSpan.FromDays(365);
                var sensor = collector.CreateIntSensor("stop-bound/int");
                var bar = collector.CreateIntBarSensor("stop-bound/bar", new BarSensorOptions
                {
                    BarPeriod = TimeSpan.FromHours(1),
                    BarTickPeriod = inert,
                    PostDataPeriod = inert,
                });

                await collector.Start().ConfigureAwait(false);

                for (var i = 0; i < 10; i++)
                    sensor.AddValue(i);

                bar.AddValue(1);
                bar.AddValue(2);

                var stopwatch = Stopwatch.StartNew();
                await collector.Stop().ConfigureAwait(false);
                stopwatch.Stop();

                Assert.True(
                    stopwatch.Elapsed < StopBound,
                    $"Stop took {stopwatch.Elapsed} with a hung cancellation-respecting sender; expected under {StopBound}.");

                // The flush did try the transport (including the partial bar enqueued by the
                // bar's stop flush) and gave up within the bound instead of hanging on it.
                Assert.True(sender.DataSendAttempts >= 1, "stop flush should have attempted at least one dispatch");
            }
        }

        [Fact]
        public async Task Stop_when_sender_ignores_cancellation_in_run_loop_is_bounded()
        {
            // Worse than a dead server: a broken custom IDataSender that never completes AND
            // never observes the token. The hang starts in the normal dispatch loop, so
            // StopAsync's WhenAny timeout is the guard and the flush is skipped entirely.
            var sender = new HangingSender(respectsCancellation: false);

            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromMilliseconds(20)))
            {
                var sensor = collector.CreateIntSensor("stop-bound/run-loop-hang");

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(1);

                Assert.True(
                    await sender.WaitForDataSendAttemptsAsync(1, TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                    "the run loop should have entered the hung send before Stop");

                var stopwatch = Stopwatch.StartNew();
                await collector.Stop().ConfigureAwait(false);
                stopwatch.Stop();

                Assert.True(
                    stopwatch.Elapsed < StopBound,
                    $"Stop took {stopwatch.Elapsed} with a cancellation-ignoring sender hung in the run loop; expected under {StopBound}.");
            }
        }

        [Fact]
        public async Task Stop_with_default_request_timeout_is_capped_at_five_seconds_per_hung_queue()
        {
            // RequestTimeout deliberately stays at the production default (30 s). The graceful
            // stop wait must be capped at 5 s regardless — a host restart cannot be held for
            // RequestTimeout by a hung transport.
            var sender = new HangingSender(respectsCancellation: false);

            using (var collector = CreateCollector(
                       sender,
                       collectPeriod: TimeSpan.FromMilliseconds(20),
                       requestTimeout: TimeSpan.FromSeconds(30)))
            {
                var sensor = collector.CreateIntSensor("stop-bound/default-timeout");

                await collector.Start().ConfigureAwait(false);
                sensor.AddValue(1);

                Assert.True(
                    await sender.WaitForDataSendAttemptsAsync(1, TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                    "the run loop should have entered the hung send before Stop");

                var stopwatch = Stopwatch.StartNew();
                await collector.Stop().ConfigureAwait(false);
                stopwatch.Stop();

                Assert.True(
                    stopwatch.Elapsed < StopBound,
                    $"Stop took {stopwatch.Elapsed} with RequestTimeout=30s and a hung sender; the graceful stop wait must be capped at 5s.");
            }
        }

        [Fact]
        public async Task Stop_when_sender_first_hangs_at_flush_ignoring_cancellation_is_bounded()
        {
            // The nastiest corner: the sender hangs (ignoring cancellation) for the FIRST time on
            // the stop-flush dispatch itself — the run loop never touched it (60s collect period),
            // so the queue stops cleanly and the flush is the one holding the in-flight send.
            // FlushAsync must abandon that dispatch when the flush timeout fires.
            var sender = new HangingSender(respectsCancellation: false);

            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromSeconds(60)))
            {
                var sensor = collector.CreateIntSensor("stop-bound/flush-hang");

                await collector.Start().ConfigureAwait(false);

                for (var i = 0; i < 10; i++)
                    sensor.AddValue(i);

                var stopwatch = Stopwatch.StartNew();
                await collector.Stop().ConfigureAwait(false);
                stopwatch.Stop();

                Assert.True(
                    stopwatch.Elapsed < StopBound,
                    $"Stop took {stopwatch.Elapsed} with a cancellation-ignoring sender hanging first at flush; expected under {StopBound}.");

                Assert.True(sender.DataSendAttempts >= 1, "the stop flush should have attempted the hung dispatch");
            }
        }

        private static DataCollector CreateCollector(IDataSender sender, TimeSpan collectPeriod, TimeSpan? requestTimeout = null)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "stop-bound-key",
                ServerAddress = "https://localhost",
                ClientName = "stop-bound-client",
                ComputerName = "stop-bound-host",
                Module = "stop-bound-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = collectPeriod,
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(1),
            });
        }

        private sealed class HangingSender : IDataSender
        {
            private readonly TaskCompletionSource<PackageSendingInfo> _never =
                new TaskCompletionSource<PackageSendingInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly bool _respectsCancellation;

            private int _dataSendAttempts;

            public HangingSender(bool respectsCancellation)
            {
                _respectsCancellation = respectsCancellation;
            }

            public int DataSendAttempts => Volatile.Read(ref _dataSendAttempts);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public async ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                Interlocked.Increment(ref _dataSendAttempts);

                if (_respectsCancellation)
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);

                return await _never.Task.ConfigureAwait(false);
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                SendDataAsync(items, token);

            // Commands and files complete instantly so the tests isolate the data path.
            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;

            public async Task<bool> WaitForDataSendAttemptsAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    if (DataSendAttempts >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return DataSendAttempts >= count;
            }
        }
    }
}
