using HSMDataCollector.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class ScheduledTaskHandleTests
    {
        [Fact]
        public void Null_scheduler_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ScheduledTaskHandle(null));
        }

        [Fact]
        public void Not_scheduled_initially()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);
                Assert.False(handle.IsScheduled);
            }
        }

        [Fact]
        public async Task Start_schedules_and_action_runs()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);
                var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                handle.Start(() => signal.TrySetResult(true), TimeSpan.FromMilliseconds(20), Timeout.InfiniteTimeSpan, null);

                Assert.True(handle.IsScheduled);

                var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                Assert.Same(signal.Task, completed);
            }
        }

        [Fact]
        public void Start_is_idempotent_keeps_first_schedule()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);
                var first = 0;
                var second = 0;

                handle.Start(() => Interlocked.Increment(ref first), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50), null);
                // Second Start while already scheduled must be a no-op — the second action never runs.
                handle.Start(() => Interlocked.Increment(ref second), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50), null);

                Assert.True(handle.IsScheduled);
                Assert.Equal(0, Volatile.Read(ref second));
            }
        }

        [Fact]
        public async Task StopAsync_stops_the_action()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);
                var count = 0;

                handle.Start(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(40), null);
                await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

                await handle.StopAsync().ConfigureAwait(false);
                Assert.False(handle.IsScheduled);

                // Allow any in-flight dispatch to drain, then confirm no further runs.
                await Task.Delay(TimeSpan.FromMilliseconds(80)).ConfigureAwait(false);
                var snapshot = Volatile.Read(ref count);
                await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

                Assert.Equal(snapshot, Volatile.Read(ref count));
            }
        }

        [Fact]
        public async Task StopAsync_is_idempotent_and_safe_when_not_started()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);

                var ex = await Record.ExceptionAsync(async () =>
                {
                    await handle.StopAsync().ConfigureAwait(false);
                    await handle.StopAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                Assert.Null(ex);
            }
        }

        [Fact]
        public async Task StopAsync_without_waiting_for_current_run_completes_promptly()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);
                handle.Start(() => { }, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), null);
                await Task.Delay(TimeSpan.FromMilliseconds(80)).ConfigureAwait(false);

                var stopTask = handle.StopAsync(waitForCurrentRun: false).AsTask();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.Same(stopTask, completed);
                Assert.False(handle.IsScheduled);
            }
        }

        [Fact]
        public async Task Restart_after_stop_schedules_again()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var handle = new ScheduledTaskHandle(scheduler);
                var runs = 0;

                handle.Start(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), null);
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                await handle.StopAsync().ConfigureAwait(false);

                var afterStop = Volatile.Read(ref runs);

                handle.Start(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), null);
                Assert.True(handle.IsScheduled);

                await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
                await handle.StopAsync().ConfigureAwait(false);

                Assert.True(Volatile.Read(ref runs) > afterStop, "Handle should schedule again after a stop.");
            }
        }
    }
}
