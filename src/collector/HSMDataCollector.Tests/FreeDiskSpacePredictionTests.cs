using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class FreeDiskSpacePredictionTests
    {
        [Fact]
        public void FreeDiskSpace_reports_disk_read_failure_as_error_status()
        {
            using (var collector = CreateCollector())
            {
                var sensor = new TestFreeDiskSpace(
                    CreateOptions(collector, new ThrowingDiskInfo(new IOException("statvfs failed"))));
                Exception observedException = null;

                sensor.ExceptionThrowing += (_, ex) => observedException = ex;

                var value = sensor.ReadValue();

                Assert.Equal(default, value);
                Assert.Equal(SensorStatus.Error, sensor.ReadStatus());
                Assert.Equal("statvfs failed", sensor.ReadComment());
                Assert.IsType<IOException>(observedException);
            }
        }

        [Fact]
        public async Task StartAsync_reports_initial_disk_read_failure_without_throwing()
        {
            using (var collector = CreateCollector())
            {
                var sensor = new TestFreeDiskSpacePrediction(
                    CreateOptions(collector),
                    new ThrowingDiskInfo(new IOException("statvfs failed")));
                Exception observedException = null;

                sensor.ExceptionThrowing += (_, ex) => observedException = ex;

                var exception = await Record.ExceptionAsync(async () => await sensor.StartAsync().AsTask()).ConfigureAwait(false);

                await sensor.StopAsync().ConfigureAwait(false);

                Assert.Null(exception);
                Assert.IsType<IOException>(observedException);
            }
        }

        [Fact]
        public async Task Disk_speed_sampler_reports_disk_read_failure_without_throwing()
        {
            using (var collector = CreateCollector())
            {
                var diskInfo = new ThrowingDiskInfo(new IOException("statvfs failed"));
                var sensor = new TestFreeDiskSpacePrediction(CreateOptions(collector), diskInfo);
                var exceptions = new List<Exception>();

                sensor.ExceptionThrowing += (_, ex) => exceptions.Add(ex);

                await sensor.StartAsync().ConfigureAwait(false);

                var sampler = typeof(FreeDiskSpacePredictionBase)
                    .GetMethod("UpdateDiskSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

                var exception = Record.Exception(() => sampler.Invoke(sensor, null));

                await sensor.StopAsync().ConfigureAwait(false);

                Assert.Null(exception);
                Assert.All(exceptions, ex => Assert.IsType<IOException>(ex));
                Assert.True(exceptions.Count >= 2);
            }
        }

        private static DataCollector CreateCollector()
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "disk-prediction-key",
                ClientName = "disk-prediction-client",
                ComputerName = "disk-prediction-host",
                Module = "disk-prediction-module",
                DataSender = new NoopSender(),
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static DiskSensorOptions CreateOptions(DataCollector collector)
        {
            return new DiskSensorOptions
            {
                Path = "disk/prediction",
                DataProcessor = GetDataProcessor(collector),
                PostDataPeriod = TimeSpan.FromSeconds(1),
                CalibrationRequests = 1,
            };
        }

        private static DiskSensorOptions CreateOptions(DataCollector collector, IDiskInfo diskInfo) =>
            CreateOptions(collector).SetInfo(diskInfo);

        private static DataProcessor GetDataProcessor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private sealed class TestFreeDiskSpacePrediction : FreeDiskSpacePredictionBase
        {
            internal TestFreeDiskSpacePrediction(DiskSensorOptions options, IDiskInfo diskInfo)
                : base(options, diskInfo) { }
        }

        private sealed class TestFreeDiskSpace : FreeDiskSpaceBase
        {
            internal TestFreeDiskSpace(DiskSensorOptions options) : base(options) { }

            public double ReadValue() => GetValue();

            public string ReadComment() => GetComment();

            public SensorStatus ReadStatus() => GetStatus();
        }

        private sealed class ThrowingDiskInfo : IDiskInfo
        {
            private readonly Exception _exception;

            public ThrowingDiskInfo(Exception exception) => _exception = exception;

            public long FreeSpaceMb => throw _exception;

            public long FreeSpace => throw _exception;

            public string DiskLetter => "/";
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
