using HSMDataCollector.Core;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorLifecycleTests
    {
        [Fact]
        public void Initial_status_is_stopped()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
            Assert.False(lifecycle.CanAcceptData);
            Assert.False(lifecycle.CanStartNewSensors);
        }

        [Fact]
        public void TryStart_from_stopped_succeeds()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.True(lifecycle.TryStart());
            Assert.Equal(CollectorStatus.Starting, lifecycle.Status);
            Assert.True(lifecycle.CanAcceptData);
            Assert.True(lifecycle.CanStartNewSensors);
        }

        [Fact]
        public void TryStart_from_starting_fails()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();

            Assert.False(lifecycle.TryStart());
            Assert.Equal(CollectorStatus.Starting, lifecycle.Status);
        }

        [Fact]
        public void TryStart_from_running_fails()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();

            Assert.False(lifecycle.TryStart());
            Assert.Equal(CollectorStatus.Running, lifecycle.Status);
        }

        [Fact]
        public void CompleteStart_from_starting_succeeds()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();

            Assert.True(lifecycle.CompleteStart());
            Assert.Equal(CollectorStatus.Running, lifecycle.Status);
            Assert.True(lifecycle.CanAcceptData);
            Assert.True(lifecycle.CanStartNewSensors);
        }

        [Fact]
        public void CompleteStart_from_stopped_fails()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.False(lifecycle.CompleteStart());
            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
        }

        [Fact]
        public void AbortStart_from_starting_returns_to_stopped()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();

            Assert.True(lifecycle.AbortStart());
            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
            Assert.False(lifecycle.CanAcceptData);
        }

        [Fact]
        public void AbortStart_from_running_fails()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();

            Assert.False(lifecycle.AbortStart());
            Assert.Equal(CollectorStatus.Running, lifecycle.Status);
        }

        [Fact]
        public void TryStop_from_running_succeeds()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();

            Assert.True(lifecycle.TryStop());
            Assert.Equal(CollectorStatus.Stopping, lifecycle.Status);
            Assert.True(lifecycle.CanAcceptData);
            Assert.False(lifecycle.CanStartNewSensors);
        }

        [Fact]
        public void TryStop_from_starting_succeeds()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();

            Assert.True(lifecycle.TryStop());
            Assert.Equal(CollectorStatus.Stopping, lifecycle.Status);
        }

        [Fact]
        public void TryStop_from_stopped_fails()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.False(lifecycle.TryStop());
            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
        }

        [Fact]
        public void TryStop_from_stopping_fails()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryStop();

            Assert.False(lifecycle.TryStop());
            Assert.Equal(CollectorStatus.Stopping, lifecycle.Status);
        }

        [Fact]
        public void CompleteStop_from_stopping_returns_to_stopped()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryStop();

            Assert.True(lifecycle.CompleteStop());
            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
            Assert.False(lifecycle.CanAcceptData);
        }

        [Fact]
        public void CompleteStop_from_stopped_is_noop()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.False(lifecycle.CompleteStop());
            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
        }

        [Fact]
        public void Full_lifecycle_stopped_to_running_to_stopped()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.True(lifecycle.TryStart());
            Assert.True(lifecycle.CompleteStart());
            Assert.True(lifecycle.TryStop());
            Assert.True(lifecycle.CompleteStop());

            Assert.Equal(CollectorStatus.Stopped, lifecycle.Status);
        }

        [Fact]
        public void Restart_after_stop_succeeds()
        {
            var lifecycle = new CollectorLifecycle();

            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryStop();
            lifecycle.CompleteStop();

            Assert.True(lifecycle.TryStart());
            Assert.Equal(CollectorStatus.Starting, lifecycle.Status);
        }

        // --- Dispose transitions ---

        [Fact]
        public void TryDispose_from_stopped_returns_stopped()
        {
            var lifecycle = new CollectorLifecycle();

            Assert.Equal(CollectorStatus.Stopped, lifecycle.TryDispose());
            Assert.Equal(CollectorStatus.Disposed, lifecycle.Status);
        }

        [Fact]
        public void TryDispose_from_running_returns_running()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();

            Assert.Equal(CollectorStatus.Running, lifecycle.TryDispose());
            Assert.Equal(CollectorStatus.Disposed, lifecycle.Status);
        }

        [Fact]
        public void TryDispose_from_starting_returns_starting()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();

            Assert.Equal(CollectorStatus.Starting, lifecycle.TryDispose());
            Assert.Equal(CollectorStatus.Disposed, lifecycle.Status);
        }

        [Fact]
        public void TryDispose_from_stopping_returns_stopping()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryStop();

            Assert.Equal(CollectorStatus.Stopping, lifecycle.TryDispose());
            Assert.Equal(CollectorStatus.Disposed, lifecycle.Status);
        }

        [Fact]
        public void Double_dispose_returns_disposed()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryDispose();

            Assert.Equal(CollectorStatus.Disposed, lifecycle.TryDispose());
        }

        [Fact]
        public void TryStart_after_dispose_fails()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryDispose();

            Assert.False(lifecycle.TryStart());
            Assert.Equal(CollectorStatus.Disposed, lifecycle.Status);
        }

        [Fact]
        public void CanStartNewSensors_false_after_dispose()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryDispose();

            Assert.False(lifecycle.CanStartNewSensors);
        }

        [Fact]
        public void CanAcceptData_true_during_stopping_after_dispose()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryDispose();
            lifecycle.TryStop();

            Assert.True(lifecycle.CanAcceptData);
        }

        [Fact]
        public void CanAcceptData_false_after_complete_stop_following_dispose()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();
            lifecycle.TryDispose();
            lifecycle.TryStop();
            lifecycle.CompleteStop();

            Assert.False(lifecycle.CanAcceptData);
        }

        // --- Concurrency ---

        [Fact]
        public void Concurrent_TryStart_only_one_wins()
        {
            var lifecycle = new CollectorLifecycle();
            var wins = 0;

            Parallel.For(0, 100, _ =>
            {
                if (lifecycle.TryStart())
                    Interlocked.Increment(ref wins);
            });

            Assert.Equal(1, wins);
            Assert.Equal(CollectorStatus.Starting, lifecycle.Status);
        }

        [Fact]
        public void Concurrent_TryDispose_only_one_returns_non_disposed()
        {
            var lifecycle = new CollectorLifecycle();
            lifecycle.TryStart();
            lifecycle.CompleteStart();

            var disposedCount = 0;

            Parallel.For(0, 100, _ =>
            {
                if (lifecycle.TryDispose() != CollectorStatus.Disposed)
                    Interlocked.Increment(ref disposedCount);
            });

            Assert.Equal(1, disposedCount);
            Assert.Equal(CollectorStatus.Disposed, lifecycle.Status);
        }

        [Fact]
        public void Concurrent_TryStart_and_TryStop_reach_valid_state()
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var lifecycle = new CollectorLifecycle();

                var started = false;
                var stopped = false;

                var startTask = Task.Run(() => { started = lifecycle.TryStart(); });
                var stopTask = Task.Run(() => { stopped = lifecycle.TryStop(); });

                Task.WaitAll(startTask, stopTask);

                var status = lifecycle.Status;

                Assert.True(
                    status == CollectorStatus.Stopped ||
                    status == CollectorStatus.Starting ||
                    status == CollectorStatus.Stopping,
                    $"Status should be Stopped, Starting, or Stopping but was {status}");
            }
        }
    }
}
