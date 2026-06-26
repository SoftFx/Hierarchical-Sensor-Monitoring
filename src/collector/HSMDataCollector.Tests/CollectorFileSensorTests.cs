using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorFileSensorTests
    {
        [Fact]
        public async Task SendFileAsync_uses_single_system_path_prefix()
        {
            var sender = new RecordingFileSender();
            var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(filePath, "hello");

            try
            {
                using (var collector = CreateCollector(sender))
                {
                    await collector.Start().ConfigureAwait(false);

                    Assert.True(await collector.SendFileAsync("contract/file/single-prefix", filePath).ConfigureAwait(false));
                    Assert.True(await sender.WaitForFileCountAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));
                }

                Assert.Equal("conformance-host/conformance-module/contract/file/single-prefix", sender.Files[0].Path);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        public async Task SendFileAsync_rejects_invalid_status_without_sending_file()
        {
            var sender = new RecordingFileSender();
            var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(filePath, "hello");

            try
            {
                using (var collector = CreateCollector(sender))
                {
                    await collector.Start().ConfigureAwait(false);

                    var sent = await collector.SendFileAsync(
                        "contract/file/invalid-status",
                        filePath,
                        (SensorStatus)99,
                        "bad-status").ConfigureAwait(false);

                    Assert.False(sent);
                    Assert.False(await sender.WaitForFileCountAsync(1, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false));
                }
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        public async Task SendFileAsync_before_start_returns_false_without_sending_file()
        {
            var sender = new RecordingFileSender();
            var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(filePath, "hello");

            try
            {
                using (var collector = CreateCollector(sender))
                {
                    var sent = await collector.SendFileAsync("contract/file/before-start", filePath).ConfigureAwait(false);

                    Assert.False(sent);
                    Assert.False(await sender.WaitForFileCountAsync(1, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false));
                }
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        public async Task File_sensor_string_value_rejects_null_without_throwing_or_sending()
        {
            var sender = new RecordingFileSender();

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateFileSensor("contract/file/null-string", "payload", "txt");
                await collector.Start().ConfigureAwait(false);

                var exception = Record.Exception(() => sensor.AddValue(null, SensorStatus.Ok, "null-file"));

                Assert.Null(exception);
                Assert.False(await sender.WaitForFileCountAsync(1, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false));
            }
        }

        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "conformance-key",
                ServerAddress = "https://localhost",
                ClientName = "conformance-client",
                ComputerName = "conformance-host",
                Module = "conformance-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 10,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
            });
        }

        private sealed class RecordingFileSender : IDataSender
        {
            private readonly List<FileSensorValue> _files = new List<FileSensorValue>();
            private readonly object _lock = new object();

            public IReadOnlyList<FileSensorValue> Files
            {
                get
                {
                    lock (_lock)
                        return _files.ToArray();
                }
            }

            public void Dispose()
            {
            }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                default;

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                default;

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
                default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
            {
                lock (_lock)
                    _files.Add(file);

                return default;
            }

            public async Task<bool> WaitForFileCountAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    lock (_lock)
                    {
                        if (_files.Count >= count)
                            return true;
                    }

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return false;
            }
        }
    }
}
