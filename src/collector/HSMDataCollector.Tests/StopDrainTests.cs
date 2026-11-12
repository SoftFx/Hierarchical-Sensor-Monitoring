using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1659 A6 and A7: what the drain at Stop and Dispose sends, in which order and for how long.
    /// A7: registrations go first (the values that follow need them), and the whole drain has one
    /// budget, not one per queue.
    /// A6: a package that was in flight when Dispose cancelled it is sent again by the drain, as
    /// it already was on Stop.
    /// </summary>
    public sealed class StopDrainTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        // --- A7: order ---

        [Fact]
        public async Task Stop_drain_sends_pending_registrations_before_values()
        {
            var sender = new DrainSender { Mode = SenderMode.Down };

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("drain/order");
                await collector.Start();

                // The registration is refused while the server is down and waits in the command
                // queue for its next cycle, which the hour-long collect period never reaches.
                Assert.True(await sender.WaitForAsync(SendKind.Command, 1, Timeout), "The registration was never attempted.");

                sensor.AddValue(1);

                sender.Mode = SenderMode.Up;

                await collector.Stop();
            }

            var drained = sender.SendsIn(SenderMode.Up);

            Assert.Contains(SendKind.Command, drained);
            Assert.Contains(SendKind.Data, drained);
            Assert.True(drained.IndexOf(SendKind.Command) < drained.IndexOf(SendKind.Data),
                        "The drain sent values before the registrations they need: " + string.Join(", ", drained));
        }

        // --- A7: one budget ---

        [Fact]
        public async Task Stop_drain_has_one_budget_for_all_queues()
        {
            var sender = new DrainSender { Mode = SenderMode.Down };

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("drain/budget/int");
                var file = collector.CreateFileSensor("drain/budget/file", "drain");
                await collector.Start();

                file.AddValue("content");

                // Three queues hold work for the drain: the registrations and the file value were
                // refused and wait for their next cycle, the int value waits for the collect period.
                Assert.True(await sender.WaitForAsync(SendKind.Command, 1, Timeout), "The registration was never attempted.");
                Assert.True(await sender.WaitForAsync(SendKind.File, 1, Timeout), "The file value was never attempted.");

                sensor.AddValue(1);

                // A server that accepts the connection and never answers: a send waits until
                // whoever started it gives up.
                sender.Mode = SenderMode.Hung;

                var watch = Stopwatch.StartNew();
                await collector.Stop();
                watch.Stop();

                var drained = sender.SendsIn(SenderMode.Hung);

                // One budget: the first hung send, the registrations, uses it up, and the queues
                // after it find it spent and start nothing. One budget per queue would start a
                // send for each of them.
                var summary = $"Sends inside the drain: {string.Join(", ", drained)}; the stop took {watch.Elapsed}.";

                Assert.True(drained.Contains(SendKind.Command), summary);
                Assert.False(drained.Contains(SendKind.Data), summary);
                Assert.False(drained.Contains(SendKind.File), summary);
            }
        }

        // --- A6 ---

        [Fact]
        public async Task Dispose_sends_again_the_package_it_cancelled_in_flight()
        {
            var sender = new DrainSender { Mode = SenderMode.HungOnce };

            var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromMilliseconds(20));
            try
            {
                var sensor = collector.CreateIntSensor("drain/in-flight");
                await collector.Start();

                sensor.AddValue(4242);

                Assert.True(await sender.WaitForParkedDataAsync(Timeout), "The value never reached the sender.");
                // The package that is in flight is the one with the value: a value that waited
                // in the queue behind another send would be delivered by any drain.
                Assert.Contains(4242, sender.ParkedInts);
            }
            finally
            {
                collector.Dispose();
            }

            Assert.Contains(4242, sender.DeliveredInts);
        }

        [Fact]
        public async Task Stop_sends_again_the_package_it_cancelled_in_flight()
        {
            var sender = new DrainSender { Mode = SenderMode.HungOnce };

            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromMilliseconds(20)))
            {
                var sensor = collector.CreateIntSensor("drain/in-flight");
                await collector.Start();

                sensor.AddValue(4242);

                Assert.True(await sender.WaitForParkedDataAsync(Timeout), "The value never reached the sender.");
                // The package that is in flight is the one with the value: a value that waited
                // in the queue behind another send would be delivered by any drain.
                Assert.Contains(4242, sender.ParkedInts);

                await collector.Stop();
            }

            Assert.Contains(4242, sender.DeliveredInts);
        }


        private static DataCollector CreateCollector(IDataSender sender, TimeSpan? collectPeriod = null)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "drain-key",
                ClientName = "drain-client",
                ComputerName = "drain-host",
                Module = "drain-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = collectPeriod ?? TimeSpan.FromHours(1),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                MaxDeduplicatedMessages = 100,
            });
        }


        private enum SendKind
        {
            Data,
            Priority,
            Command,
            File,
        }

        private enum SenderMode
        {
            /// <summary>Every send is accepted.</summary>
            Up,

            /// <summary>Every send fails at once, with no answer.</summary>
            Down,

            /// <summary>Every send waits until its token is cancelled.</summary>
            Hung,

            /// <summary>The first data send waits until its token is cancelled; the rest are accepted.</summary>
            HungOnce,
        }

        private sealed class DrainSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly List<KeyValuePair<SenderMode, SendKind>> _sends = new List<KeyValuePair<SenderMode, SendKind>>();
            private readonly List<int> _deliveredInts = new List<int>();
            private readonly List<int> _parkedInts = new List<int>();

            private int _mode;
            private int _hungOnceUsed;
            private int _parkedData;

            internal SenderMode Mode
            {
                get => (SenderMode)Volatile.Read(ref _mode);
                set => Volatile.Write(ref _mode, (int)value);
            }

            internal List<int> DeliveredInts
            {
                get
                {
                    lock (_lock)
                        return _deliveredInts.ToList();
                }
            }

            /// <summary>The int values of the data sends that waited in a hang.</summary>
            internal List<int> ParkedInts
            {
                get
                {
                    lock (_lock)
                        return _parkedInts.ToList();
                }
            }

            internal List<SendKind> SendsIn(SenderMode mode)
            {
                lock (_lock)
                    return _sends.Where(s => s.Key == mode).Select(s => s.Value).ToList();
            }

            internal Task<bool> WaitForAsync(SendKind kind, int count, TimeSpan timeout) =>
                PollAsync(() =>
                {
                    lock (_lock)
                        return _sends.Count(s => s.Value == kind) >= count;
                }, timeout);

            internal Task<bool> WaitForParkedDataAsync(TimeSpan timeout) =>
                PollAsync(() => Volatile.Read(ref _parkedData) > 0, timeout);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                SendAsync(SendKind.Data, items.ToList(), token);

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                SendAsync(SendKind.Priority, items.ToList(), token);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
                SendAsync(SendKind.Command, null, token);

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                SendAsync(SendKind.File, null, token);

            private async ValueTask<PackageSendingInfo> SendAsync(SendKind kind, List<SensorValueBase> values, CancellationToken token)
            {
                var mode = Mode;

                lock (_lock)
                    _sends.Add(new KeyValuePair<SenderMode, SendKind>(mode, kind));

                if (mode == SenderMode.Down)
                    return new PackageSendingInfo(1, null, "No connection could be made");

                var hangs = mode == SenderMode.Hung
                            || (mode == SenderMode.HungOnce && kind == SendKind.Data && Interlocked.Exchange(ref _hungOnceUsed, 1) == 0);

                if (hangs)
                {
                    if (kind == SendKind.Data)
                    {
                        lock (_lock)
                            _parkedInts.AddRange(values.OfType<IntSensorValue>().Select(v => v.Value));

                        Interlocked.Increment(ref _parkedData);
                    }

                    await Task.Delay(System.Threading.Timeout.Infinite, token).ConfigureAwait(false);
                }

                if (values != null)
                    lock (_lock)
                        _deliveredInts.AddRange(values.OfType<IntSensorValue>().Select(v => v.Value));

                return new PackageSendingInfo(1, 200, null);
            }

            private static async Task<bool> PollAsync(Func<bool> done, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (!done())
                {
                    if (DateTime.UtcNow >= deadline)
                        return false;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return true;
            }
        }
    }
}
