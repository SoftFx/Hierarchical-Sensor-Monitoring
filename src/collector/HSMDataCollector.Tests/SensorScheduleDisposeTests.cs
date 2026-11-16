#if NET5_0_OR_GREATER
#nullable disable
#endif

using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.Threading;
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
    /// #1066: a disposed sensor leaves nothing in the scheduler. Every class that owns a
    /// <see cref="ScheduledTaskHandle"/> (the monitoring send loop, the bar collect loop, the disk
    /// prediction sampler) stops it when the sensor is disposed, so the scheduler worker can never
    /// start a run against a disposed sensor.
    ///
    /// Nothing here waits for time to pass. A run is started by the scheduler worker only through
    /// <see cref="ScheduledTask.TryAttachRun"/>, and only while the task is in the timer wheel; the
    /// tests assert that every task of a disposed sensor is out of the wheel and refuses that call,
    /// which is what "cannot be invoked again" means for this scheduler. Compiled into both
    /// collector test projects (net472 and net6.0).
    /// </summary>
    public sealed class SensorScheduleDisposeTests
    {
        private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

        // The path the issue names: SensorsStorage.Dispose disposes each sensor without a
        // StopAsync first, while the collector's scheduler is still running.
        [Fact]
        public async Task Disposing_the_sensor_storage_stops_every_sensor_schedule_while_the_scheduler_runs()
        {
            using (var collector = CreateCollector())
            {
                var sensors = RegisterScheduledSensors(collector);

                await collector.Start();
                await WithGuard(sensors.FunctionCalled);

                var tasks = CaptureScheduledTasks(sensors.All);

                GetSensorsStorage(collector).Dispose();

                // The scheduler is still running here, so a task left in the wheel would be run
                // again against a disposed sensor.
                Assert.False(GetSchedulerWorker(collector).IsCompleted, "The scheduler must still be running for this test to mean anything.");

                await AssertNotDispatchableAsync(tasks, sensors.All);
            }
        }

        // The acceptance criterion: after DataCollector.Dispose returns, no sensor task remains
        // schedulable and the scheduler worker can no longer run one.
        [Fact]
        public async Task Collector_dispose_leaves_no_sensor_schedule_that_the_worker_can_run()
        {
            var collector = CreateCollector();
            var sensors = RegisterScheduledSensors(collector);

            await collector.Start();
            await WithGuard(sensors.FunctionCalled);

            var tasks = CaptureScheduledTasks(sensors.All);

            collector.Dispose();

            Assert.Equal(CollectorStatus.Disposed, collector.Status);
            Assert.True(GetSchedulerWorker(collector).IsCompleted, "Dispose must return with the scheduler worker exited.");

            await AssertNotDispatchableAsync(tasks, sensors.All);
        }

        private static async Task AssertNotDispatchableAsync(List<CapturedTask> tasks, IEnumerable<object> sensors)
        {
            foreach (var captured in tasks)
            {
                // A run the worker dispatched before the dispose has finished: the sensor's stop
                // awaited it (bounded), and it is drained here before anything is counted.
                await WithGuard(captured.Task.CurrentRun);

                Assert.True(captured.Task.IsDisposed, $"{captured.Name}: the scheduled task was left running after the sensor was disposed.");
                Assert.Null(captured.Task.BucketNode);
                Assert.False(captured.Task.TryAttachRun(new TaskCompletionSource<bool>().Task),
                    $"{captured.Name}: the scheduler could still start a run of a disposed sensor.");
            }

            foreach (var sensor in sensors)
                foreach (var handle in GetHandles(sensor))
                    Assert.False(handle.Value.IsScheduled, $"{sensor.GetType().Name}.{handle.Key} is still scheduled after dispose.");
        }

        private static ScheduledSensors RegisterScheduledSensors(DataCollector collector)
        {
            var functionCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Monitoring sensor: one handle, the send loop. A short period keeps it active.
            var function = collector.CreateFunctionSensor(
                "dispose-schedule/function",
                () =>
                {
                    functionCalled.TrySetResult(true);
                    return 1;
                },
                new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(20) });

            // Bar sensor: two handles, the send loop and the bar collect loop.
            var bar = collector.CreateIntBarSensor(
                "dispose-schedule/bar",
                new BarSensorOptions
                {
                    BarPeriod = TimeSpan.FromMinutes(1),
                    BarTickPeriod = TimeSpan.FromMilliseconds(20),
                    PostDataPeriod = TimeSpan.FromMilliseconds(20),
                });

            // Disk prediction sensor: the send loop plus its free-space sampler. Built on a fake
            // disk, so it runs on any OS.
            var diskOptions = new DiskSensorOptions
            {
                Path = "dispose-schedule/disk-prediction",
                DataProcessor = GetDataProcessor(collector),
                PostDataPeriod = TimeSpan.FromMilliseconds(20),
                SpaceCheckPeriod = TimeSpan.FromMilliseconds(20),
                CalibrationRequests = 1,
            }.SetInfo(new FixedDiskInfo());

            var disk = (ISensor)GetSensorsStorage(collector).Register(new DiskPrediction(diskOptions));

            return new ScheduledSensors(functionCalled.Task, function, bar, disk);
        }

        private static List<CapturedTask> CaptureScheduledTasks(IEnumerable<object> sensors)
        {
            var tasks = new List<CapturedTask>();

            foreach (var sensor in sensors)
            {
                var handles = GetHandles(sensor);
                Assert.NotEmpty(handles);

                foreach (var handle in handles)
                {
                    var task = (ScheduledTask)typeof(ScheduledTaskHandle)
                        .GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(handle.Value);

                    // Every handle is running before the dispose: the sensor is active.
                    Assert.True(task != null, $"{sensor.GetType().Name}.{handle.Key} was not scheduled after Start.");

                    tasks.Add(new CapturedTask($"{sensor.GetType().Name}.{handle.Key}", task));
                }
            }

            return tasks;
        }

        // Every ScheduledTaskHandle field declared anywhere in the sensor's class hierarchy.
        private static List<KeyValuePair<string, ScheduledTaskHandle>> GetHandles(object sensor)
        {
            var handles = new List<KeyValuePair<string, ScheduledTaskHandle>>();

            for (var type = sensor.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType == typeof(ScheduledTaskHandle))
                        handles.Add(new KeyValuePair<string, ScheduledTaskHandle>(field.Name, (ScheduledTaskHandle)field.GetValue(sensor)));
                }
            }

            return handles;
        }

        private static async Task WithGuard(Task task)
        {
            // A guard against a hang, not a synchronisation: the awaited task is what the test waits for.
            var completed = await Task.WhenAny(task, Task.Delay(HangGuard));
            Assert.True(ReferenceEquals(completed, task), "The test hung waiting for a collector operation.");
            await task;
        }

        private static DataCollector CreateCollector() =>
            new DataCollector(new CollectorOptions
            {
                AccessKey = "dispose-schedule-key",
                ClientName = "dispose-schedule-client",
                ComputerName = "dispose-schedule-host",
                Module = "dispose-schedule-module",
                DataSender = new NoopSender(),
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
            });

        private static DataProcessor GetDataProcessor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private static SensorsStorage GetSensorsStorage(DataCollector collector) =>
            (SensorsStorage)typeof(DataCollector)
                .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private static Task GetSchedulerWorker(DataCollector collector)
        {
            var scheduler = typeof(DataCollector)
                .GetField("_scheduler", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

            return (Task)typeof(CollectorScheduler)
                .GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(scheduler);
        }

        private sealed class ScheduledSensors
        {
            internal ScheduledSensors(Task functionCalled, params object[] all)
            {
                FunctionCalled = functionCalled;
                All = all;
            }

            internal Task FunctionCalled { get; }

            internal object[] All { get; }
        }

        private sealed class CapturedTask
        {
            internal CapturedTask(string name, ScheduledTask task)
            {
                Name = name;
                Task = task;
            }

            internal string Name { get; }

            internal ScheduledTask Task { get; }
        }

        private sealed class DiskPrediction : FreeDiskSpacePredictionBase
        {
            internal DiskPrediction(DiskSensorOptions options) : base(options, options.DiskInfo) { }
        }

        private sealed class FixedDiskInfo : IDiskInfo
        {
            public long FreeSpace => 100L * 1024 * 1024 * 1024;

            public long FreeSpaceMb => FreeSpace / (1024L * 1024L);

            public string DiskLetter => "C";
        }

        private sealed class NoopSender : IDataSender
        {
            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;
        }
    }
}
