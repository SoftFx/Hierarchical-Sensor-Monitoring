#if NET5_0_OR_GREATER
#nullable disable
#endif

using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
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
    /// A failed sensor initialization in <c>Start()</c> or in the obsolete <c>Initialize()</c> and
    /// the start rollback it triggers. The processor is stopped exactly once:
    /// <list type="bullet">
    /// <item>when a Stop or Dispose took the lifecycle while the initialization ran, that caller
    /// owns the stop and the failed start leaves it alone, so the value the caller is draining is
    /// delivered and not cleared by a start-rollback stop beside it;</item>
    /// <item>when a Stop or Dispose comes while the rollback itself runs, it joins the rollback
    /// instead of starting a second stop beside it, and <c>ToStopped</c> fires once, before
    /// Dispose returns.</item>
    /// </list>
    /// Nothing is timed: the initialization and the sensor stop wait on gates the test opens once
    /// the racing call has been made. Compiled into both collector test projects (net472 and net6.0).
    /// </summary>
    public sealed class FailedStartRollbackTests
    {
        private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

        private const string ValuePath = "failed-start/value";

        [Fact]
        public async Task Start_whose_init_fails_after_a_Stop_took_over_leaves_the_stop_to_Stop()
        {
            var sender = new CountingSender();

            using (var collector = CreateCollector(sender))
            {
                var value = collector.CreateDoubleSensor(ValuePath);
                var gated = Register(collector, new GatedSensor(Options(collector, "failed-start/start-gated"), throwAfterInit: true));

                var start = collector.Start();
                await WithGuard(gated.InitEntered);

                // Accepted while Starting: the owner of the stop has to drain it.
                value.AddValue(42);

                // Stop takes the lifecycle and waits for the initialization before it stops.
                var stop = collector.Stop();

                gated.ReleaseInit();

                await WithGuard(start);
                await WithGuard(stop);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.Equal(1, gated.StopCalls);
                Assert.Equal(1, sender.ValuesSentFor(ValuePath));
            }
        }

        [Fact]
        public async Task Obsolete_Initialize_whose_init_fails_after_a_Stop_took_over_leaves_the_stop_to_Stop()
        {
            var sender = new CountingSender();

            using (var collector = CreateCollector(sender))
            {
                var value = collector.CreateDoubleSensor(ValuePath);
                var gated = Register(collector, new GatedSensor(Options(collector, "failed-start/initialize-gated"), throwAfterInit: true));

#pragma warning disable CS0618 // the obsolete entry point is what this test covers
                var initialize = Task.Run(() => collector.Initialize(false));
#pragma warning restore CS0618

                await WithGuard(gated.InitEntered);

                value.AddValue(42);

                var stop = collector.Stop();

                gated.ReleaseInit();

                await WithGuard(initialize);
                await WithGuard(stop);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.Equal(1, gated.StopCalls);
                Assert.Equal(1, sender.ValuesSentFor(ValuePath));
            }
        }

        [Fact]
        public async Task Stop_during_the_rollback_of_a_failed_Start_joins_the_rollback()
        {
            var sender = new CountingSender();

            using (var collector = CreateCollector(sender))
            {
                var gated = Register(collector, new GatedSensor(Options(collector, "failed-start/rollback-stop"), throwAfterInit: true, gateStop: true));
                var stopping = 0;
                var stopped = 0;

                collector.ToStopping += () => Interlocked.Increment(ref stopping);
                collector.ToStopped += () => Interlocked.Increment(ref stopped);

                var start = collector.Start();
                await WithGuard(gated.InitEntered);

                // The initialization fails and the start rollback stops the sensors; it is held
                // there until the racing Stop has been made.
                gated.ReleaseInit();
                await WithGuard(gated.StopEntered);

                var stop = collector.Stop();

                gated.ReleaseStop();

                await WithGuard(start);
                await WithGuard(stop);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);
                Assert.Equal(1, gated.StopCalls);
                Assert.Equal(1, Volatile.Read(ref stopping));
                Assert.Equal(1, Volatile.Read(ref stopped));
            }
        }

        [Fact]
        public async Task Dispose_during_the_rollback_of_a_failed_obsolete_Initialize_joins_the_rollback()
        {
            var releaseStop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Dispose reaches the cancel of the pending requests only after it has decided how to
            // stop, so the rollback is held until then.
            var sender = new CountingSender { OnCancelPendingRequests = () => releaseStop.TrySetResult(true) };
            var collector = CreateCollector(sender);
            var gated = Register(collector, new GatedSensor(Options(collector, "failed-start/rollback-dispose"), throwAfterInit: true, stopGate: releaseStop.Task));
            var stopped = 0;

            collector.ToStopped += () => Interlocked.Increment(ref stopped);

#pragma warning disable CS0618 // the obsolete entry point is what this test covers
            var initialize = Task.Run(() => collector.Initialize(false));
#pragma warning restore CS0618

            await WithGuard(gated.InitEntered);

            gated.ReleaseInit();
            await WithGuard(gated.StopEntered);

            var dispose = Task.Run(() => collector.Dispose());
            await WithGuard(dispose);

            // ToStopped fires once, before Dispose returns: not later, from the rollback, on a
            // collector whose components are already disposed.
            Assert.Equal(1, Volatile.Read(ref stopped));

            await WithGuard(initialize);

            Assert.Equal(CollectorStatus.Disposed, collector.Status);
            Assert.Equal(1, gated.StopCalls);
            Assert.Equal(1, Volatile.Read(ref stopped));
        }

        private static async Task WithGuard(Task task)
        {
            // A guard against a hang, not a synchronisation: the awaited task is what the test waits for.
            var completed = await Task.WhenAny(task, Task.Delay(HangGuard));
            Assert.True(ReferenceEquals(completed, task), "The test hung waiting for a collector operation.");
            await task;
        }

        private static DataCollector CreateCollector(IDataSender sender) =>
            new DataCollector(new CollectorOptions
            {
                AccessKey = "failed-start-key",
                ClientName = "failed-start-client",
                ComputerName = "failed-start-host",
                Module = "failed-start-module",
                DataSender = sender,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
            });

        private static InstantSensorOptions Options(DataCollector collector, string path) =>
            new InstantSensorOptions
            {
                ComputerName = collector.ComputerName,
                Module = collector.Module,
                Path = path,
                DataProcessor = (DataProcessor)typeof(DataCollector)
                    .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(collector),
            };

        private static GatedSensor Register(DataCollector collector, GatedSensor sensor)
        {
            var storage = (SensorsStorage)typeof(DataCollector)
                .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

            storage.Register(sensor);
            return sensor;
        }

        // A sensor whose InitAsync waits on a gate and may then throw, and whose StopAsync may
        // wait on a gate too. Every processor stop reaches StopAsync once, so StopCalls counts them.
        private sealed class GatedSensor : SensorBase<NoDisplayUnit>
        {
            private readonly TaskCompletionSource<bool> _initEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _initRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _stopEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _stopRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly bool _throwAfterInit;
            private readonly Task _stopGate;

            private int _stopCalls;

            internal GatedSensor(InstantSensorOptions options, bool throwAfterInit, bool gateStop = false, Task stopGate = null) : base(options)
            {
                _throwAfterInit = throwAfterInit;
                _stopGate = stopGate ?? (gateStop ? _stopRelease.Task : null);
            }

            internal Task InitEntered => _initEntered.Task;

            internal Task StopEntered => _stopEntered.Task;

            internal int StopCalls => Volatile.Read(ref _stopCalls);

            internal void ReleaseInit() => _initRelease.TrySetResult(true);

            internal void ReleaseStop() => _stopRelease.TrySetResult(true);

            public override async ValueTask<bool> InitAsync()
            {
                _initEntered.TrySetResult(true);

                await _initRelease.Task.ConfigureAwait(false);

                if (_throwAfterInit)
                    throw new InvalidOperationException("Injected sensor initialization failure.");

                return true;
            }

            public override async ValueTask StopAsync()
            {
                Interlocked.Increment(ref _stopCalls);
                _stopEntered.TrySetResult(true);

                if (_stopGate != null)
                    await _stopGate.ConfigureAwait(false);
            }

            // Disposal is not a processor stop: it is not counted.
            protected override ValueTask DisposeAsyncCore() => default;
        }

        private sealed class CountingSender : IDataSender, ICancelableDataSender
        {
            private readonly object _lock = new object();
            private readonly List<string> _paths = new List<string>();

            internal Action OnCancelPendingRequests { get; set; }

            internal int ValuesSentFor(string pathSuffix)
            {
                lock (_lock)
                    return _paths.Count(path => path != null && path.EndsWith(pathSuffix, StringComparison.Ordinal));
            }

            public void CancelPendingRequests() => OnCancelPendingRequests?.Invoke();

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Record(items);

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Record(items);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;

            private ValueTask<PackageSendingInfo> Record(IEnumerable<SensorValueBase> items)
            {
                lock (_lock)
                    _paths.AddRange(items.Select(item => item.Path));

                return default;
            }
        }
    }
}
