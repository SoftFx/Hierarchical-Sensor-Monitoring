using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.Diagnostic;
using HSMDataCollector.Options;
using HSMDataCollector.Tables;
using Xunit;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Tables
{
    /// <summary>
    /// The last bar of the <c>Queue overflow</c> sensor against a periodic send in flight (#1846, third review): a loss
    /// the table ledger handed over after that send took its snapshot must be in a bar the stop publishes, because the
    /// ledger has given it away. The order of events is forced through the sensor's send stages; nothing is timed.
    /// </summary>
    public sealed class QueueOverflowFinalBarTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task A_loss_taken_after_the_snapshot_of_a_send_in_flight_is_in_the_bar_the_stop_publishes()
        {
            var sender = new RecordingValueSender();

            using (var collector = new DataCollector(new CollectorOptions { AccessKey = "overflow-final-bar", DataSender = sender }))
            {
                // A bar that never closes during the test and a send loop that comes round quickly: the only sends
                // are the periodic ones of the open bar and the final one of the stop.
                var bar = new BarSensorOptions
                {
                    PostDataPeriod = TimeSpan.FromMilliseconds(50),
                    BarPeriod = TimeSpan.FromHours(1),
                    BarTickPeriod = TimeSpan.FromHours(1),
                };

                if (DataCollector.IsWindowsOS)
                    collector.Windows.AddQueueOverflow(bar);
                else
                    collector.Unix.AddQueueOverflow(bar);

                await collector.Start();

                var overflow = DataCollector.IsWindowsOS
                    ? ((DefaultSensorsCollection)collector.Windows).QueueOverflowSensor
                    : ((DefaultSensorsCollection)collector.Unix).QueueOverflowSensor;

                // Something for the periodic send to publish.
                overflow.AddValue("Seed", 1);

                var snapshotTaken = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new ManualResetEventSlim();
                var pausedOnce = 0;

                overflow.SendStageForTests = stage =>
                {
                    if (stage == MonitoringSendStage.ValueBuilt)
                    {
                        // The first periodic send from now on stops with its snapshot taken and not published.
                        if (Interlocked.Exchange(ref pausedOnce, 1) == 0)
                        {
                            snapshotTaken.TrySetResult(true);
                            release.Wait(Patience);
                        }
                    }
                    else if (stage == MonitoringSendStage.LoopStopping)
                    {
                        // The stop is about to wait for the send in flight: that send goes on and ends before the
                        // stop takes its next step, so the test does not depend on the length of that wait.
                        release.Set();
                        SpinWait.SpinUntil(() => !overflow.IsSendInProgress, Patience);
                    }
                };

                Assert.True(await Task.WhenAny(snapshotTaken.Task, Task.Delay(Patience)) == snapshotTaken.Task, "no periodic send came");

                // What the table tick does with the losses of its ledger: the sensor takes them, so the ledger keeps
                // nothing for the stop line.
                Assert.True(overflow.TryAddValue(TablePipeline.DroppedRowsQueueName, 5));

                await collector.Stop();

                var last = sender.OverflowBars.Last();

                Assert.True(last.Comment != null && last.Comment.Contains($"{TablePipeline.DroppedRowsQueueName}: 5"),
                            $"the loss the sensor took is in no published bar; the last bar says: '{last.Comment}'");
                Assert.Equal(2, last.Count);
            }
        }
    }
}
