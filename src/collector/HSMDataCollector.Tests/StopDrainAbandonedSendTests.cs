#if NET5_0_OR_GREATER
#nullable disable
#endif

using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
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
    /// #1748: the stop budget fires while a package is being sent. The same token cancels the
    /// send and ends the drain's wait for it, so which of the two is seen first is a race:
    ///
    /// - the send comes back first: its package returns to the queue, the clear counts it, and
    ///   nothing is wrong with the sender, so nothing is said about it;
    /// - the drain gives the send up first (the sender is still away after a moment): its items
    ///   are counted as discarded, the Error line names the sender, and when the send does come
    ///   back it must not put the package into the queue the clear has already emptied. It used
    ///   to: a later Start then sent those values, which the log had called lost.
    ///
    /// Both orders are made by the test, not met by luck. The sender is gated: it returns when
    /// the test says so. The wait before the drain gives a send up is the test's as well
    /// (<c>WaitBeforeAbandon</c>): it ends when the test says so. The only thing awaited from
    /// the clock is the stop budget itself (one second), and it is awaited as an event.
    /// Compiled into both collector test projects (net472 and net6.0).
    /// </summary>
    public sealed class StopDrainAbandonedSendTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private const string GaveUp = "gave up a send";

        public enum Outcome
        {
            /// <summary>The send honours the cancellation, late.</summary>
            Cancelled,

            /// <summary>The send returns a failure.</summary>
            Failed,

            /// <summary>The send throws.</summary>
            Thrown,
        }

        [Theory]
        [InlineData("Data", Outcome.Cancelled)]
        [InlineData("Data", Outcome.Failed)]
        [InlineData("Data", Outcome.Thrown)]
        [InlineData("File", Outcome.Cancelled)]
        [InlineData("File", Outcome.Failed)]
        public async Task Send_the_drain_gave_up_is_counted_as_discarded_and_does_not_come_back(string queueName, Outcome outcome)
        {
            var sender = new GatedSender(queueName, outcome);
            var log = new Log();

            using (var collector = CreateCollector(sender))
            {
                collector.AddCustomLogger(log);

                var items = await FillQueueAsync(collector, sender, queueName);
                var queue = GetQueue(collector, queueName);
                var wait = new AbandonWait();

                SetWaitBeforeAbandon(queue, wait.Begin);
                sender.CloseGate();

                var stop = collector.Stop();

                // The budget has fired and the send is still in flight: the drain waits.
                Assert.True(await Within(wait.Begun.Task), "The drain never waited for the cancelled send.");

                // The wait ends and the sender has not come back: the drain gives the send up.
                wait.End();

                Assert.True(await Within(stop), "The stop did not return.");

                Assert.Contains(log.Errors, line => line.Contains(GaveUp));
                Assert.Contains($"{queueName} queue discarded {items} item(s) during collector shutdown.", log.Infos);

                // Now the send comes back. Its dispatch runs to its end before the test goes on.
                sender.OpenGate();

                Assert.True(await Within(Settled(wait.Dispatch)), "The send that was given up never returned.");
                Assert.Equal(0, QueueCount(queue));

                // And a later run sends nothing of it.
                await collector.Start();
                await collector.Stop();

                Assert.Empty(sender.AcceptedAfterTheGate);
            }
        }

        [Theory]
        [InlineData("Data")]
        [InlineData("File")]
        public async Task Send_that_returns_when_the_budget_cancels_it_is_not_blamed_and_is_counted_once(string queueName)
        {
            var sender = new GatedSender(queueName, Outcome.Cancelled);
            var log = new Log();

            using (var collector = CreateCollector(sender))
            {
                collector.AddCustomLogger(log);

                var items = await FillQueueAsync(collector, sender, queueName);
                var queue = GetQueue(collector, queueName);
                var wait = new AbandonWait();

                SetWaitBeforeAbandon(queue, wait.Begin);
                sender.CloseGate();

                var stop = collector.Stop();

                // The budget has fired and the send is still in flight. (A drain that gives the
                // send up at once, without a wait, lets the stop return instead.)
                await Within(Task.WhenAny(wait.Begun.Task, stop));

                // The send honours the cancellation; the wait is never ended by the test.
                sender.OpenGate();

                Assert.True(await Within(stop), "The stop did not return.");

                Assert.DoesNotContain(log.Errors, line => line.Contains(GaveUp));
                Assert.Contains($"{queueName} queue discarded {items} item(s) during collector shutdown.", log.Infos);
                Assert.Equal(1, log.Infos.Count(line => line.StartsWith($"{queueName} queue discarded", StringComparison.Ordinal)));
                Assert.Equal(0, QueueCount(queue));
            }
        }

        [Fact]
        public void Send_is_given_to_the_dispatch_or_to_the_drain_and_never_to_both()
        {
            var returnedFirst = new DrainSend();

            Assert.True(returnedFirst.TryReturn());
            Assert.False(returnedFirst.TryAbandon());
            Assert.True(returnedFirst.TryReturn());

            var abandonedFirst = new DrainSend();

            Assert.True(abandonedFirst.TryAbandon());
            Assert.False(abandonedFirst.TryReturn());
        }


        // Puts work for the drain into the queue and returns how many items it is.
        private static async Task<int> FillQueueAsync(DataCollector collector, GatedSender sender, string queueName)
        {
            if (queueName == "File")
            {
                var file = collector.CreateFileSensor("abandoned/file", "drain");
                await collector.Start();

                // The file queue sends at once; the send fails and the value waits in the queue
                // for a next cycle that the hour-long collect period never reaches.
                file.AddValue("content");
                Assert.True(await Within(sender.FileFailedBeforeTheGate.Task), "The file value was never attempted.");

                return 1;
            }

            var data = collector.CreateIntSensor("abandoned/data");
            await collector.Start();

            // Wait in the data queue for the collect period, which is an hour.
            for (var i = 1; i <= 3; i++)
                data.AddValue(i);

            return 3;
        }

        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "abandoned-key",
                ClientName = "abandoned-client",
                ComputerName = "abandoned-host",
                Module = "abandoned-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromHours(1),
                // The drain budget: one second.
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static object GetQueue(DataCollector collector, string queueName)
        {
            var processor = typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

            return typeof(DataProcessor)
                .GetField(queueName == "File" ? "_fileQueue" : "_dataQueue", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(processor);
        }

        // Both live on QueueProcessorBase<T>, whose T differs between the two queues.
        private static void SetWaitBeforeAbandon(object queue, Func<Task, Task> wait) =>
            queue.GetType().BaseType
                .GetProperty("WaitBeforeAbandon", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(queue, wait);

        private static int QueueCount(object queue) =>
            (int)queue.GetType().BaseType
                .GetProperty("QueueCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(queue);

        // A bound on a wait for something that must happen, so a broken build fails instead of
        // hanging. Never a margin: no test passes because this time went by.
        private static async Task<bool> Within(Task task) =>
            ReferenceEquals(await Task.WhenAny(task, Task.Delay(Timeout)).ConfigureAwait(false), task);

        private static async Task Settled(Task dispatch)
        {
            try
            {
                await dispatch.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // However the send ended, its dispatch is over.
            }
        }


        /// <summary>The wait before the drain gives a send up, in the hands of the test.</summary>
        private sealed class AbandonWait
        {
            private readonly TaskCompletionSource<bool> _over = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Completes when the drain starts to wait: the budget has fired.</summary>
            internal TaskCompletionSource<bool> Begun { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The dispatch the drain is waiting for.</summary>
            internal Task Dispatch { get; private set; }

            internal Task Begin(Task dispatch)
            {
                Dispatch = dispatch;
                Begun.TrySetResult(true);

                return _over.Task;
            }

            internal void End() => _over.TrySetResult(true);
        }

        /// <summary>
        /// Accepts registrations. A send of the gated queue fails at once until the gate is
        /// closed; the one send that meets the closed gate waits there, whatever its token
        /// says, until the gate is opened, and then ends as the test asked. Sends after that
        /// are accepted and recorded.
        /// </summary>
        private sealed class GatedSender : IDataSender
        {
            private readonly TaskCompletionSource<bool> _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<string> _acceptedAfterTheGate = new List<string>();
            private readonly string _queueName;
            private readonly Outcome _outcome;

            private int _gateClosed;
            private int _gateUsed;

            internal GatedSender(string queueName, Outcome outcome)
            {
                _queueName = queueName;
                _outcome = outcome;
            }

            internal TaskCompletionSource<bool> FileFailedBeforeTheGate { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal List<string> AcceptedAfterTheGate
            {
                get
                {
                    lock (_acceptedAfterTheGate)
                        return _acceptedAfterTheGate.ToList();
                }
            }

            internal void CloseGate() => Volatile.Write(ref _gateClosed, 1);

            internal void OpenGate() => _gate.TrySetResult(true);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                SendAsync("Data", string.Join(",", items.OfType<IntSensorValue>().Select(v => v.Value)), token);

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                SendAsync("File", file.Name, token);

            private async ValueTask<PackageSendingInfo> SendAsync(string queueName, string content, CancellationToken token)
            {
                if (queueName == _queueName && Volatile.Read(ref _gateClosed) == 0)
                {
                    FileFailedBeforeTheGate.TrySetResult(true);

                    return new PackageSendingInfo(1, null, "No connection could be made");
                }

                if (queueName == _queueName && Interlocked.Exchange(ref _gateUsed, 1) == 0)
                {
                    await _gate.Task.ConfigureAwait(false);

                    switch (_outcome)
                    {
                        case Outcome.Cancelled:
                            throw new OperationCanceledException(token);
                        case Outcome.Thrown:
                            throw new InvalidOperationException("The connection was reset");
                        default:
                            return new PackageSendingInfo(1, null, "No connection could be made");
                    }
                }

                lock (_acceptedAfterTheGate)
                    _acceptedAfterTheGate.Add($"{queueName}: {content}");

                return new PackageSendingInfo(1, 200, null);
            }
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
