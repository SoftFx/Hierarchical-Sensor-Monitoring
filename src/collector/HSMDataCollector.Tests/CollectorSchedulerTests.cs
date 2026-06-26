using HSMDataCollector.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorSchedulerTests
    {
        [Fact]
        public async Task Schedule_invokes_action_after_delay()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                scheduler.Schedule(() => signal.TrySetResult(true), TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);

                var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                Assert.Same(signal.Task, completed);
            }
        }

        [Fact]
        public async Task Schedule_periodic_invokes_action_repeatedly()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var count = 0;
                var period = TimeSpan.FromMilliseconds(50);

                var task = scheduler.Schedule(() => Interlocked.Increment(ref count), TimeSpan.Zero, period);

                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

                task.Dispose();
                var observed = Volatile.Read(ref count);

                // With 50ms period over 300ms we expect ~5-6 invocations; assert at least 3 to allow scheduler jitter.
                Assert.True(observed >= 3, $"Expected at least 3 invocations, got {observed}.");
            }
        }

        [Fact]
        public async Task Schedule_async_action_awaits_completion_before_next_invocation()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var inFlight = 0;
                var maxConcurrent = 0;
                var period = TimeSpan.FromMilliseconds(20);

                var task = scheduler.Schedule(async () =>
                {
                    var current = Interlocked.Increment(ref inFlight);
                    var oldMax = Volatile.Read(ref maxConcurrent);
                    while (current > oldMax && Interlocked.CompareExchange(ref maxConcurrent, current, oldMax) != oldMax)
                        oldMax = Volatile.Read(ref maxConcurrent);

                    await Task.Delay(100).ConfigureAwait(false);

                    Interlocked.Decrement(ref inFlight);
                }, TimeSpan.Zero, period);

                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                task.Dispose();

                // A ScheduledTask must not start a new run while its previous run is still in flight.
                Assert.Equal(1, Volatile.Read(ref maxConcurrent));
            }
        }

        [Fact]
        public async Task Remove_stops_scheduled_action()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var count = 0;
                var task = scheduler.Schedule(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));

                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
                scheduler.Remove(task);

                // Allow any in-flight invocation that the worker already dispatched to finish before snapshotting.
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                var afterRemoveCount = Volatile.Read(ref count);
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

                Assert.Equal(afterRemoveCount, Volatile.Read(ref count));
            }
        }

        [Fact]
        public async Task Dispose_stops_worker_and_blocks_new_schedules()
        {
            var scheduler = new CollectorScheduler();
            var fired = 0;
            scheduler.Schedule(() => Interlocked.Increment(ref fired), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

            scheduler.Dispose();

            // Drain any in-flight invocation already dispatched to the threadpool before snapshotting.
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            var afterDisposeCount = Volatile.Read(ref fired);
            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

            Assert.Equal(afterDisposeCount, Volatile.Read(ref fired));
            Assert.Throws<ObjectDisposedException>(() => scheduler.Schedule(() => { }, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void Schedule_with_negative_delay_throws()
        {
            using (var scheduler = new CollectorScheduler())
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => scheduler.Schedule(() => { }, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1)));
            }
        }

        [Fact]
        public void Schedule_with_zero_period_throws()
        {
            using (var scheduler = new CollectorScheduler())
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => scheduler.Schedule(() => { }, TimeSpan.Zero, TimeSpan.Zero));
            }
        }

        [Fact]
        public void Schedule_with_null_action_throws()
        {
            using (var scheduler = new CollectorScheduler())
            {
                Assert.Throws<ArgumentNullException>(
                    () => scheduler.Schedule((Action)null, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
                Assert.Throws<ArgumentNullException>(
                    () => scheduler.Schedule((Func<Task>)null, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            }
        }

        [Fact]
        public async Task Multiple_schedulers_are_independent()
        {
            // Per-collector schedulers should not share state — confirms removal of static global state.
            using (var schedulerA = new CollectorScheduler())
            using (var schedulerB = new CollectorScheduler())
            {
                var countA = 0;
                var countB = 0;

                var taskA = schedulerA.Schedule(() => Interlocked.Increment(ref countA), TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
                var taskB = schedulerB.Schedule(() => Interlocked.Increment(ref countB), TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

                schedulerA.Remove(taskA);

                // Drain any in-flight invocation already dispatched to the threadpool before snapshotting.
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                var snapshotA = Volatile.Read(ref countA);
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

                // schedulerB must keep firing after schedulerA's task was removed.
                Assert.True(Volatile.Read(ref countB) > snapshotA / 2,
                    $"schedulerB should keep firing independently. countA(after-remove)={snapshotA}, countB={Volatile.Read(ref countB)}");
                Assert.Equal(snapshotA, Volatile.Read(ref countA));

                taskB.Dispose();
            }
        }

        [Fact]
        public async Task Onerror_callback_receives_exception_from_action()
        {
            using (var scheduler = new CollectorScheduler())
            {
                Exception caught = null;
                var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                scheduler.Schedule(
                    () => throw new InvalidOperationException("boom"),
                    TimeSpan.FromMilliseconds(20),
                    Timeout.InfiniteTimeSpan,
                    ex => { caught = ex; signal.TrySetResult(true); });

                var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

                Assert.Same(signal.Task, completed);
                Assert.IsType<InvalidOperationException>(caught);
            }
        }

        [Fact]
        public async Task ScheduledTask_dispose_removes_from_owning_scheduler()
        {
            using (var scheduler = new CollectorScheduler())
            {
                var count = 0;
                var task = scheduler.Schedule(() => Interlocked.Increment(ref count), TimeSpan.Zero, TimeSpan.FromMilliseconds(40));

                await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
                task.Dispose();

                // Drain any in-flight invocation already dispatched to the threadpool before snapshotting.
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                var snapshot = Volatile.Read(ref count);
                await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

                Assert.Equal(snapshot, Volatile.Read(ref count));
            }
        }
    }
}
