#if NET5_0_OR_GREATER
#nullable disable
#endif

using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1747: the first failed value send ends the drain of a stop. What the value queues still
    /// hold is discarded and counted, and no queue behind the one that failed tries the same
    /// server again. The rule is the queues', so these tests use a host's own
    /// <see cref="IDataSender"/>: nothing here depends on the built-in HTTP client.
    ///
    /// Nothing is timed: a send that should not happen is seen as a recorded send, not as a
    /// stop that took long. Compiled into both collector test projects (net472 and net6.0).
    /// </summary>
    public sealed class StopDrainFailedSendTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        [Fact]
        public async Task Stop_with_a_sender_that_fails_makes_one_value_attempt_and_counts_the_rest()
        {
            var sender = new ScriptedSender { Mode = SenderMode.Down };
            var log = new InfoLog();

            using (var collector = CreateCollector(sender))
            {
                collector.AddCustomLogger(log);

                var data = collector.CreateIntSensor("failed-send/data");
                var file = collector.CreateFileSensor("failed-send/file", "drain");
                await collector.Start();

                // Refused while the server is down; both wait in their queues for a next cycle
                // that the hour-long collect period never reaches.
                Assert.True(await sender.WaitForAsync(SendKind.Command, 1, Timeout), "The registration was never attempted.");

                file.AddValue("content");
                Assert.True(await sender.WaitForAsync(SendKind.File, 1, Timeout), "The file value was never attempted.");

                // Three packages of values (MaxValuesInPackage is 50).
                for (var i = 0; i < 120; i++)
                    data.AddValue(i);

                sender.Mode = SenderMode.DownAtStop;

                await collector.Stop();
            }

            var drained = sender.SendsIn(SenderMode.DownAtStop);
            var summary = "Sends inside the drain: " + string.Join(", ", drained);

            // One data package of three, and nothing from the file queue behind it.
            Assert.Equal(1, drained.Count(kind => kind == SendKind.Data));
            Assert.False(drained.Contains(SendKind.File), summary);

            // The remainder is counted as a discarded remainder always was.
            Assert.Contains("Data queue discarded 120 item(s) during collector shutdown.", log.Infos);
            Assert.Contains("File queue discarded 1 item(s) during collector shutdown.", log.Infos);
        }

        [Fact]
        public async Task Failed_priority_send_ends_the_drain_for_the_value_queues_behind_it()
        {
            var sender = new ScriptedSender { Mode = SenderMode.Down };
            var log = new InfoLog();

            using (var collector = CreateCollector(sender))
            {
                collector.AddCustomLogger(log);

                var priority = collector.CreateIntSensor("failed-send/priority", new InstantSensorOptions { IsPrioritySensor = true });
                var data = collector.CreateIntSensor("failed-send/data");
                var file = collector.CreateFileSensor("failed-send/file", "drain");
                await collector.Start();

                Assert.True(await sender.WaitForAsync(SendKind.Command, 1, Timeout), "The registration was never attempted.");

                priority.AddValue(1);
                Assert.True(await sender.WaitForAsync(SendKind.Priority, 1, Timeout), "The priority value was never attempted.");

                file.AddValue("content");
                Assert.True(await sender.WaitForAsync(SendKind.File, 1, Timeout), "The file value was never attempted.");

                data.AddValue(2);

                sender.Mode = SenderMode.DownAtStop;

                await collector.Stop();
            }

            // The registrations are tried once, then the first value queue once; that send
            // fails and is the last send of the stop.
            Assert.Equal(new[] { SendKind.Command, SendKind.Priority }, sender.SendsIn(SenderMode.DownAtStop));

            Assert.Contains("Priority data queue discarded 1 item(s) during collector shutdown.", log.Infos);
            Assert.Contains("Data queue discarded 1 item(s) during collector shutdown.", log.Infos);
            Assert.Contains("File queue discarded 1 item(s) during collector shutdown.", log.Infos);
        }

        [Fact]
        public async Task Failed_registration_send_does_not_end_the_drain_of_the_values()
        {
            var sender = new ScriptedSender { Mode = SenderMode.CommandsDown };

            using (var collector = CreateCollector(sender))
            {
                var data = collector.CreateIntSensor("failed-send/registration");
                await collector.Start();

                Assert.True(await sender.WaitForAsync(SendKind.Command, 1, Timeout), "The registration was never attempted.");

                data.AddValue(4242);

                sender.Mode = SenderMode.CommandsDownAtStop;

                await collector.Stop();
            }

            // As in the native drain: the pending registrations are posted once, and the values
            // are drained whatever that post returned.
            Assert.Equal(new[] { SendKind.Command, SendKind.Data }, sender.SendsIn(SenderMode.CommandsDownAtStop));
            Assert.Contains(4242, sender.DeliveredInts);
        }


        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "failed-send-key",
                ClientName = "failed-send-client",
                ComputerName = "failed-send-host",
                Module = "failed-send-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromHours(1),
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
            /// <summary>Every send fails at once, with no answer.</summary>
            Down,

            /// <summary>As <see cref="Down"/>; a name of its own for the sends of the stop.</summary>
            DownAtStop,

            /// <summary>Registrations fail at once, with no answer; values are accepted.</summary>
            CommandsDown,

            /// <summary>As <see cref="CommandsDown"/>; a name of its own for the sends of the stop.</summary>
            CommandsDownAtStop,
        }

        private sealed class ScriptedSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly List<KeyValuePair<SenderMode, SendKind>> _sends = new List<KeyValuePair<SenderMode, SendKind>>();
            private readonly List<int> _deliveredInts = new List<int>();

            private int _mode;

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

            internal List<SendKind> SendsIn(SenderMode mode)
            {
                lock (_lock)
                    return _sends.Where(s => s.Key == mode).Select(s => s.Value).ToList();
            }

            internal async Task<bool> WaitForAsync(SendKind kind, int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (true)
                {
                    lock (_lock)
                        if (_sends.Count(s => s.Value == kind) >= count)
                            return true;

                    if (DateTime.UtcNow >= deadline)
                        return false;

                    await Task.Delay(10).ConfigureAwait(false);
                }
            }

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                Send(SendKind.Data, items.ToList());

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                Send(SendKind.Priority, items.ToList());

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
                Send(SendKind.Command, null);

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                Send(SendKind.File, null);

            private ValueTask<PackageSendingInfo> Send(SendKind kind, List<SensorValueBase> values)
            {
                var mode = Mode;

                lock (_lock)
                    _sends.Add(new KeyValuePair<SenderMode, SendKind>(mode, kind));

                var commandsOnly = mode == SenderMode.CommandsDown || mode == SenderMode.CommandsDownAtStop;

                if (!commandsOnly || kind == SendKind.Command)
                    return new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, null, "No connection could be made"));

                if (values != null)
                    lock (_lock)
                        _deliveredInts.AddRange(values.OfType<IntSensorValue>().Select(v => v.Value));

                return new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));
            }
        }

        private sealed class InfoLog : ICollectorLogger
        {
            private readonly List<string> _infos = new List<string>();

            internal List<string> Infos
            {
                get
                {
                    lock (_infos)
                        return _infos.ToList();
                }
            }

            public void Debug(string message) { }

            public void Info(string message)
            {
                lock (_infos)
                    _infos.Add(message);
            }

            public void Error(string message) { }

            public void Error(Exception ex) { }
        }
    }
}
