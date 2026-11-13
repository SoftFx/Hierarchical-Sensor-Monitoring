#if NET5_0_OR_GREATER
#nullable disable
#endif

using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1061: the obsolete synchronous <c>Initialize()</c> has the init-race protection of
    /// <c>Start()</c>. A Stop or Dispose that races the sensor initialization waits for it before
    /// it stops and disposes the sensors and queues that initialization is still touching, and is
    /// the only stop of the processor: Initialize does not start a second, conflicting one.
    ///
    /// Nothing is timed. The race is opened by a sensor whose InitAsync waits on a gate, and the
    /// gate is opened from the one point of Dispose that both the old and the fixed code reach
    /// after Dispose has decided how to stop: the cancel of the sender's pending requests.
    /// Compiled into both collector test projects (net472 and net6.0).
    /// </summary>
    public sealed class ObsoleteInitializeLifecycleTests
    {
        private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task Dispose_racing_obsolete_Initialize_waits_for_sensor_init_and_stops_the_processor_once()
        {
            var releaseInit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sender = new HookSender { OnCancelPendingRequests = () => releaseInit.TrySetResult(true) };
            var collector = CreateCollector(sender);
            var sensor = Register(collector, new GatedInitSensor(Options(collector, "obsolete-initialize/dispose-race"), releaseInit.Task));

#pragma warning disable CS0618 // the obsolete entry point is what this test covers
            var initialize = Task.Run(() => collector.Initialize(false));
#pragma warning restore CS0618

            await WithGuard(sensor.InitEntered);

            var dispose = Task.Run(() => collector.Dispose());

            await WithGuard(dispose);
            await WithGuard(initialize);

            Assert.Equal(CollectorStatus.Disposed, collector.Status);
            Assert.Equal(0, sensor.StopsDuringInit);

            // One processor stop (Dispose's terminal stop) and one sensor dispose. A second stop
            // would be Initialize rolling back a start that Dispose already owns, with a mode that
            // clears the queues Dispose is draining.
            Assert.Equal(1, sensor.StopCalls);
            Assert.Equal(1, sensor.DisposeCalls);
        }

        [Fact]
        public async Task Stop_from_a_ToStarting_handler_during_obsolete_Initialize_leaves_no_queue_running()
        {
            var collector = CreateCollector(new HookSender());
            Task stopFromHandler = null;

            collector.ToStarting += () => stopFromHandler = collector.Stop();

            try
            {
#pragma warning disable CS0618 // the obsolete entry point is what this test covers
                collector.Initialize(false);
#pragma warning restore CS0618

                Assert.NotNull(stopFromHandler);
                await WithGuard(stopFromHandler);

                Assert.Equal(CollectorStatus.Stopped, collector.Status);

                foreach (var queue in GetQueues(collector))
                    Assert.Equal("Stopped", GetQueueState(queue));
            }
            finally
            {
                collector.Dispose();
            }
        }

        private static IEnumerable<IQueueProcessor> GetQueues(DataCollector collector)
        {
            var processor = GetDataProcessor(collector);

            foreach (var field in typeof(DataProcessor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (typeof(IQueueProcessor).IsAssignableFrom(field.FieldType))
                    yield return (IQueueProcessor)field.GetValue(processor);
        }

        private static string GetQueueState(IQueueProcessor queue)
        {
            for (var type = queue.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(queue).ToString();
            }

            throw new InvalidOperationException($"{queue.GetType().Name} has no _state field.");
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
                AccessKey = "obsolete-initialize-key",
                ClientName = "obsolete-initialize-client",
                ComputerName = "obsolete-initialize-host",
                Module = "obsolete-initialize-module",
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
                DataProcessor = GetDataProcessor(collector),
            };

        private static GatedInitSensor Register(DataCollector collector, GatedInitSensor sensor)
        {
            var storage = (SensorsStorage)typeof(DataCollector)
                .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

            storage.Register(sensor);
            return sensor;
        }

        private static DataProcessor GetDataProcessor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        // A sensor whose InitAsync waits on a gate, and which records a StopAsync that arrives
        // while that InitAsync is still running.
        private sealed class GatedInitSensor : SensorBase<NoDisplayUnit>
        {
            private readonly TaskCompletionSource<bool> _initEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Task _release;

            private int _initInFlight;
            private int _stopsDuringInit;
            private int _stopCalls;
            private int _disposeCalls;

            internal GatedInitSensor(InstantSensorOptions options, Task release) : base(options)
            {
                _release = release;
            }

            internal Task InitEntered => _initEntered.Task;

            internal int StopsDuringInit => Volatile.Read(ref _stopsDuringInit);

            internal int StopCalls => Volatile.Read(ref _stopCalls);

            internal int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public override async ValueTask<bool> InitAsync()
            {
                Volatile.Write(ref _initInFlight, 1);
                _initEntered.TrySetResult(true);

                await _release;

                Volatile.Write(ref _initInFlight, 0);
                return true;
            }

            public override ValueTask StopAsync()
            {
                if (Volatile.Read(ref _initInFlight) == 1)
                    Interlocked.Increment(ref _stopsDuringInit);

                Interlocked.Increment(ref _stopCalls);
                return default;
            }

            // Counted apart from StopAsync, so the test can tell a processor stop from the dispose.
            protected override ValueTask DisposeAsyncCore()
            {
                if (Volatile.Read(ref _initInFlight) == 1)
                    Interlocked.Increment(ref _stopsDuringInit);

                Interlocked.Increment(ref _disposeCalls);
                return default;
            }
        }

        private sealed class HookSender : IDataSender, ICancelableDataSender
        {
            internal Action OnCancelPendingRequests { get; set; }

            public void CancelPendingRequests() => OnCancelPendingRequests?.Invoke();

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;
        }
    }
}
