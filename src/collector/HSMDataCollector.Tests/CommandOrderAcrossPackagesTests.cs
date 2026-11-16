#if NET5_0_OR_GREATER
#nullable disable
#endif

using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1750: an older command never reaches the server after a newer one, however many
    /// packages are queued and however the sends fail. The server keeps what came last, so an
    /// old registration of a sensor that arrives late overwrites the new one.
    ///
    /// A failed command package used to return to the tail of the queue, behind the packages
    /// issued after it. Its commands are now held and the next package starts with them.
    ///
    /// Every test issues more commands than one package takes (three), and holds the first
    /// send at a gate until the rest is queued, so what the queue holds when that send fails
    /// is the same on every run. A command is written as "path@description".
    /// Compiled into both collector test projects (net472 and net6.0).
    /// </summary>
    public sealed class CommandOrderAcrossPackagesTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private static readonly string[] Issued = { "order/a@old", "order/b1@", "order/b2@", "order/a@new", "order/c1@", "order/c2@" };

        [Fact]
        public async Task Failed_package_is_sent_before_the_commands_issued_after_it()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer);

            using (var collector = CreateCollector(sender))
            {
                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, Issued);

                sender.OpenGate();

                Assert.True(await sender.WaitForDeliveredAsync(Issued.Length), "The commands were never delivered: " + sender.Describe());

                await collector.Stop();
            }

            // The queue was [b1, b2, a@new, c1, c2] when the package [a@old] failed. Returned
            // to the tail, a@old was delivered after a@new and the server kept the old one.
            Assert.Equal(Issued, sender.Delivered);
            Assert.Equal("order/a@new", sender.Delivered.Last(command => command.StartsWith("order/a@", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task Order_is_kept_through_a_row_of_failed_sends()
        {
            // The gated send and the four after it get no answer: five packages in a row fail
            // while ten commands are queued. A failed package that goes to the tail rotates the
            // queue; with one, three, three, three and three commands failed, by three places.
            var sender = new ScriptedCommandSender(Answer.NoAnswer, Answer.NoAnswer, Answer.NoAnswer, Answer.NoAnswer, Answer.NoAnswer);
            var issued = Enumerable.Range(1, 10).Select(number => $"order/row{number:00}@").ToArray();

            using (var collector = CreateCollector(sender))
            {
                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, issued);

                sender.OpenGate();

                Assert.True(await sender.WaitForDeliveredAsync(issued.Length), "The commands were never delivered: " + sender.Describe());

                await collector.Stop();
            }

            Assert.Equal(issued, sender.Delivered);
        }

        [Fact]
        public async Task Stop_drain_sends_the_held_commands_first()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer);

            // An hour between cycles: the failed package is not sent again before the stop.
            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromHours(1)))
            {
                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, Issued);

                sender.OpenGate();

                await collector.Stop();
            }

            Assert.Equal(Issued, sender.Delivered);
        }

        [Fact]
        public async Task Held_commands_are_discarded_and_counted_at_a_stop_and_not_left_for_the_next_start()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer, Answer.NoAnswer);
            var log = new Log();

            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromHours(1)))
            {
                collector.AddCustomLogger(log);

                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, Issued);

                sender.OpenGate();

                // The drain sends one package, which fails as well: nothing is delivered.
                await collector.Stop();

                Assert.Contains($"Command queue discarded {Issued.Length} item(s) during collector shutdown.", log.Infos);
                Assert.Equal(0, CommandQueue(collector).QueueCount);

                var sendsOfTheFirstRun = sender.Sends.Count;

                await collector.Start();
                await collector.Stop();

                Assert.Equal(sendsOfTheFirstRun, sender.Sends.Count);
                Assert.Empty(sender.Delivered);
            }
        }

        [Fact]
        public async Task Retry_refused_by_a_full_queue_is_dropped_and_counted()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer);
            var log = new Log();
            var issued = new[] { "order/a@old", "order/b1@", "order/b2@", "order/b3@", "order/b4@" };

            // Four commands fill the queue while the first is being sent.
            using (var collector = CreateCollector(sender, maxQueueSize: 4))
            {
                collector.AddCustomLogger(log);

                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, issued);

                sender.OpenGate();

                Assert.True(await sender.WaitForDeliveredAsync(4), "The queued commands were never delivered: " + sender.Describe());

                await collector.Stop();
            }

            // The #1088 backstop: the failed command is the oldest, and it is the one that goes.
            Assert.Equal(issued.Skip(1), sender.Delivered);
            Assert.Contains(log.Errors, line => line.Contains("Collector stop: 1 value(s) dropped from the full send queue"));
        }

        [Fact]
        public async Task Overflow_evicts_a_held_command_before_a_queued_one()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer);
            var log = new Log();

            using (var collector = CreateCollector(sender, collectPeriod: TimeSpan.FromHours(1), maxQueueSize: 4))
            {
                collector.AddCustomLogger(log);

                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, new[] { "order/a@old", "order/b1@", "order/b2@", "order/b3@" });

                sender.OpenGate();

                // The failed command is held: the queue is full again, three queued and one held.
                Assert.True(await WaitForAsync(() => sender.Sends.Count == 1 && CommandQueue(collector).QueueCount == 4), "The failed command was never held.");

                // One more than the queue takes. The oldest goes, and that is the held one.
                Issue(collector, "order/b4@");

                await collector.Stop();
            }

            Assert.Equal(new[] { "order/b1@", "order/b2@", "order/b3@", "order/b4@" }, sender.Delivered);
            Assert.Contains(log.Errors, line => line.Contains("Collector stop: 1 value(s) dropped from the full send queue"));
        }

        [Fact]
        public async Task Refused_package_leaves_with_the_held_commands_it_carried()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer, Answer.Refused);
            var issued = new[] { "order/a@old", "order/b1@", "order/b2@", "order/c1@", "order/c2@" };

            using (var collector = CreateCollector(sender))
            {
                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, issued);

                sender.OpenGate();

                Assert.True(await sender.WaitForDeliveredAsync(2), "The commands behind the refused package were never delivered: " + sender.Describe());

                await collector.Stop();
            }

            var sends = sender.Sends;

            // The held command leads the next package, the server refuses that package (4xx),
            // and a refusal is final: none of its commands is sent again.
            Assert.Equal(new[] { "order/a@old", "order/b1@", "order/b2@" }, sends[1]);
            Assert.Equal(new[] { "order/c1@", "order/c2@" }, sender.Delivered);
            Assert.Equal(2, sends.Count(package => package.Contains("order/a@old")));
            Assert.Equal(1, sends.Count(package => package.Contains("order/b1@")));
        }

        [Fact]
        public async Task Only_the_commands_a_package_did_not_deliver_are_held()
        {
            var sender = new ScriptedCommandSender(Answer.NoAnswer, Answer.FirstCommandOnly);
            var issued = new[] { "order/a@old", "order/b1@", "order/b2@", "order/c1@", "order/c2@" };

            using (var collector = CreateCollector(sender))
            {
                await collector.Start();
                await IssueBehindAGatedFirstSendAsync(collector, sender, issued);

                sender.OpenGate();

                Assert.True(await sender.WaitForDeliveredAsync(issued.Length), "The commands were never delivered: " + sender.Describe());

                await collector.Stop();
            }

            // The second send delivers a@old and fails the rest (a sender that delivers a
            // package in parts, PackageSendingInfo.Settled). Only b1 and b2 are held; they
            // lead the third package, ahead of c1.
            Assert.Equal(issued, sender.Delivered);
            Assert.Equal(new[] { "order/b1@", "order/b2@", "order/c1@" }, sender.Sends[2]);
            Assert.Equal(2, sender.Sends.Count(package => package.Contains("order/a@old")));
        }


        // Issues the first command, waits until its send is held at the gate, and issues the
        // rest: they are all in the queue, in this order, when the first send fails.
        private static async Task IssueBehindAGatedFirstSendAsync(DataCollector collector, ScriptedCommandSender sender, string[] commands)
        {
            Issue(collector, commands[0]);

            Assert.True(ReferenceEquals(await Task.WhenAny(sender.FirstSendEntered.Task, Task.Delay(Timeout)), sender.FirstSendEntered.Task),
                        "The first command was never sent.");

            foreach (var command in commands.Skip(1))
                Issue(collector, command);
        }

        private static void Issue(DataCollector collector, string command)
        {
            var parts = command.Split('@');

            Processor(collector).AddCommand(null, new AddOrUpdateSensorRequest { Path = parts[0], Description = parts[1] });
        }

        private static DataProcessor Processor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private static QueueProcessorBase<CommandRequestBase> CommandQueue(DataCollector collector) =>
            (QueueProcessorBase<CommandRequestBase>)typeof(DataProcessor)
                .GetField("_commandQueue", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Processor(collector));

        private static DataCollector CreateCollector(IDataSender sender, TimeSpan? collectPeriod = null, int maxQueueSize = 1000)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "order-key",
                ClientName = "order-client",
                ComputerName = "order-host",
                Module = "order-module",
                DataSender = sender,
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = 3,
                // Also the pause before a failed package is sent again.
                PackageCollectPeriod = collectPeriod ?? TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                MaxDeduplicatedMessages = 100,
            });
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


        private enum Answer
        {
            /// <summary>The package is delivered.</summary>
            Accepted,

            /// <summary>No answer: the server is down.</summary>
            NoAnswer,

            /// <summary>The server refuses the package with a 400.</summary>
            Refused,

            /// <summary>The first command of the package is delivered and the send fails.</summary>
            FirstCommandOnly,
        }

        /// <summary>
        /// Answers the command sends by a script, one answer per send in the order the sends
        /// come; a send beyond the script is accepted. The first send waits at a gate before
        /// it is answered.
        /// </summary>
        private sealed class ScriptedCommandSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly Queue<Answer> _script;
            private readonly List<string[]> _sends = new List<string[]>();
            private readonly List<string> _delivered = new List<string>();
            private readonly TaskCompletionSource<bool> _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _gateUsed;

            internal ScriptedCommandSender(params Answer[] script)
            {
                _script = new Queue<Answer>(script);
            }

            internal TaskCompletionSource<bool> FirstSendEntered { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The packages the sender was given, in the order they came.</summary>
            internal List<string[]> Sends
            {
                get
                {
                    lock (_lock)
                        return _sends.ToList();
                }
            }

            /// <summary>The commands that were delivered, in the order the server got them.</summary>
            internal List<string> Delivered
            {
                get
                {
                    lock (_lock)
                        return _delivered.ToList();
                }
            }

            internal void OpenGate() => _gate.TrySetResult(true);

            internal string Describe() => string.Join(" | ", Sends.Select(package => string.Join(", ", package)));

            internal Task<bool> WaitForDeliveredAsync(int count) => WaitForAsync(() => Delivered.Count >= count);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public async ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                var package = commands.Select(command => $"{command.Path}@{((AddOrUpdateSensorRequest)command).Description}").ToArray();

                lock (_lock)
                    _sends.Add(package);

                if (Interlocked.Exchange(ref _gateUsed, 1) == 0)
                {
                    FirstSendEntered.TrySetResult(true);
                    await _gate.Task.ConfigureAwait(false);
                }

                lock (_lock)
                {
                    switch (_script.Count > 0 ? _script.Dequeue() : Answer.Accepted)
                    {
                        case Answer.NoAnswer:
                            return new PackageSendingInfo(1, null, "No connection could be made");

                        case Answer.Refused:
                            return new PackageSendingInfo(1, 400, "Refused");

                        case Answer.FirstCommandOnly:
                            _delivered.Add(package[0]);

                            return PackageSendingInfo.Failed(1, 503, "The last frame was not acknowledged",
                                                             package.Select((_, position) => position == 0).ToArray(), droppedCount: 0);

                        default:
                            _delivered.AddRange(package);

                            return new PackageSendingInfo(1, 200, null);
                    }
                }
            }

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));
        }

        private sealed class Log : ICollectorLogger
        {
            private readonly object _lock = new object();
            private readonly List<string> _infos = new List<string>();
            private readonly List<string> _errors = new List<string>();

            internal List<string> Infos
            {
                get
                {
                    lock (_lock)
                        return _infos.ToList();
                }
            }

            internal List<string> Errors
            {
                get
                {
                    lock (_lock)
                        return _errors.ToList();
                }
            }

            public void Debug(string message) { }

            public void Info(string message)
            {
                lock (_lock)
                    _infos.Add(message);
            }

            public void Error(string message)
            {
                lock (_lock)
                    _errors.Add(message);
            }

            public void Error(Exception ex)
            {
                lock (_lock)
                    _errors.Add(ex?.ToString() ?? string.Empty);
            }
        }
    }
}
