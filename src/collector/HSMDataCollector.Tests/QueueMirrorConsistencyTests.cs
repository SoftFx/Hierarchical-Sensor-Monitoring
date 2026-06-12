using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-C2: the BuildDate mirror must stay in lockstep with the channel. Before the fix the
    /// producer's Writer.TryWrite and _buildDateMirror.Enqueue were not atomic — a consumer could
    /// pop the channel while the mirror was still empty (near-empty queue boundary), so the
    /// producer's later mirror write became a PERMANENT orphan tick. Orphans shift the peeked head
    /// to stale values and silently weaken the #1090 stale-retry filter for the rest of the
    /// process lifetime.
    /// </summary>
    public sealed class QueueMirrorConsistencyTests
    {
        [Fact]
        public void Mirror_has_no_orphans_after_producer_consumer_contention()
        {
            var options = new CollectorOptions
            {
                AccessKey = "mirror-test-key",
                ClientName = "mirror-test-client",
                DataSender = new NullSender(),
                MaxQueueSize = 1_000_000, // No overflow eviction — every enqueued item is consumed exactly once.
                MaxValuesInPackage = 100,
            };

            // The race fires at the near-empty queue boundary (mirror empty while the channel
            // briefly holds an item whose tick has not landed yet), so the consumer hammers
            // GetPackage directly instead of the 100 ms processing loop, keeping the queue at the
            // boundary almost continuously. _queueManager is unused on these paths.
            var queue = new DataQueueProcessor(options, null, new LoggerManager());

            try
            {
                OpenWritesGate(queue);

                for (var round = 0; round < 10; round++)
                {
                    const int producers = 2;
                    const int itemsPerProducer = 10_000;
                    var consumed = 0L;
                    var producingDone = 0;

                    var producerThreads = new Thread[producers];
                    for (var i = 0; i < producers; i++)
                    {
                        producerThreads[i] = new Thread(() =>
                        {
                            for (var n = 0; n < itemsPerProducer; n++)
                                queue.Enqueue(new DoubleSensorValue { Value = n, Path = "mirror/contention" });
                        });
                        producerThreads[i].Start();
                    }

                    var consumerThread = new Thread(() =>
                    {
                        while (Volatile.Read(ref producingDone) == 0 || queue.QueueCount > 0)
                        {
                            var package = queue.GetPackage();
                            Interlocked.Add(ref consumed, package.Count);
                        }
                    });
                    consumerThread.Start();

                    foreach (var thread in producerThreads)
                        thread.Join();

                    Volatile.Write(ref producingDone, 1);
                    consumerThread.Join();

                    Assert.Equal(producers * (long)itemsPerProducer, Interlocked.Read(ref consumed));
                    Assert.Equal(0, queue.QueueCount);

                    var orphans = GetMirrorCount(queue);
                    Assert.True(orphans == 0,
                        $"Round {round}: the BuildDate mirror holds {orphans} orphan tick(s) after full drain — " +
                        "mirror and channel updates are not atomic, the stale-retry filter is permanently skewed.");
                }
            }
            finally
            {
                queue.Dispose();
            }
        }

        private static void OpenWritesGate(object queue)
        {
            // Enqueue is gated on _acceptingWritesFlag, normally set by Start(); the test drives the
            // consumer manually instead of the processing loop.
            typeof(QueueProcessorBase<SensorValueBase>)
                .GetField("_acceptingWritesFlag", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(queue, 1);
        }

        private static int GetMirrorCount(object queue)
        {
            var mirror = typeof(QueueProcessorBase<SensorValueBase>)
                .GetField("_buildDateMirror", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(queue);

            return ((ICollection)mirror).Count;
        }


        private sealed class NullSender : IDataSender
        {
            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() =>
                new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<HSMSensorDataObjects.CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;
        }
    }
}
