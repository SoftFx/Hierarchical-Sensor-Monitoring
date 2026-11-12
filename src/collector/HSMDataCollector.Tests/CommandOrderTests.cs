using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1659 A3: a command package that failed returns to the tail of the queue, behind the
    /// commands issued while it was being sent. The server applies a package in the order its
    /// commands come, so they are sent in the order they were issued in: an older registration
    /// of a sensor must not follow, and overwrite, a newer one.
    /// </summary>
    public sealed class CommandOrderTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        [Fact]
        public async Task Commands_issued_during_a_failed_send_are_sent_after_the_ones_it_carried()
        {
            var sender = new GatedCommandSender();

            using (var collector = new DataCollector(new CollectorOptions
            {
                AccessKey = "order-key",
                ClientName = "order-client",
                ComputerName = "order-host",
                Module = "order-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
            }))
            {
                collector.CreateIntSensor("order/first");
                await collector.Start();

                Assert.True(await Task.WhenAny(sender.FirstSendEntered.Task, Task.Delay(Timeout)) == sender.FirstSendEntered.Task, "The first registration was never sent.");

                // Issued while the first package is still in flight: it is in the queue before
                // the failed package comes back, so the queue holds [second, first].
                collector.CreateIntSensor("order/second");

                sender.FailFirstSend.SetResult(true);

                Assert.True(await sender.WaitForAcceptedAsync("order/second", Timeout), "The second registration was never sent.");

                await collector.Stop();
            }

            var together = sender.Accepted.Single(p => p.Any(path => path.EndsWith("order/second", StringComparison.Ordinal)));
            var first = Array.FindIndex(together, path => path.EndsWith("order/first", StringComparison.Ordinal));
            var second = Array.FindIndex(together, path => path.EndsWith("order/second", StringComparison.Ordinal));

            Assert.True(first >= 0, "The failed registration did not travel with the later one: " + string.Join(", ", together));
            Assert.True(first < second, "The registrations were sent out of the order they were issued in: " + string.Join(", ", together));
        }


        /// <summary>
        /// Holds the first command package that carries "order/first" until the test lets it
        /// fail without an answer, and accepts every package after it.
        /// </summary>
        private sealed class GatedCommandSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly List<string[]> _accepted = new List<string[]>();

            private int _gated;

            internal TaskCompletionSource<bool> FirstSendEntered { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal TaskCompletionSource<bool> FailFirstSend { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal List<string[]> Accepted
            {
                get
                {
                    lock (_lock)
                        return _accepted.ToList();
                }
            }

            internal async Task<bool> WaitForAcceptedAsync(string path, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (!Accepted.Any(p => p.Any(sent => sent.EndsWith(path, StringComparison.Ordinal))))
                {
                    if (DateTime.UtcNow >= deadline)
                        return false;

                    await Task.Delay(20).ConfigureAwait(false);
                }

                return true;
            }

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public async ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                var paths = commands.Select(c => c.Path).ToArray();

                if (paths.Any(p => p.EndsWith("order/first", StringComparison.Ordinal)) && Interlocked.Exchange(ref _gated, 1) == 0)
                {
                    FirstSendEntered.TrySetResult(true);
                    await FailFirstSend.Task.ConfigureAwait(false);

                    return new PackageSendingInfo(1, null, "No connection could be made");
                }

                lock (_lock)
                    _accepted.Add(paths);

                return Ok();
            }

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(Ok());

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(Ok());

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(Ok());

            private static PackageSendingInfo Ok()
            {
                using (var response = new HttpResponseMessage(HttpStatusCode.OK))
                    return new PackageSendingInfo(1, response);
            }
        }
    }
}
