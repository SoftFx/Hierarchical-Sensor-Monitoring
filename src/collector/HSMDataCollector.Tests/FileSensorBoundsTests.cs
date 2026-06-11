using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Sensors;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-C1: the file sensor buffers the whole file into memory, so the user-settable
    /// MaxFileSizeBytes must be bounded by a hard ceiling (a 1 GB configured cap meant ~6 GB peak per
    /// send: byte[] + List copy + JSON numeric-array serialization). The extra List copy is removed
    /// via a zero-copy wrap with a safe fallback.
    /// </summary>
    public sealed class FileSensorBoundsTests
    {
        [Fact]
        public void Effective_cap_passes_through_values_within_the_ceiling()
        {
            Assert.Equal(10L * 1024 * 1024, FileSensorInstant.GetEffectiveMaxFileSizeBytes(10L * 1024 * 1024));
        }

        [Fact]
        public void Effective_cap_bounds_oversized_configured_values()
        {
            Assert.Equal(FileSensorOptions.MaxAllowedFileSizeBytes, FileSensorInstant.GetEffectiveMaxFileSizeBytes(long.MaxValue));
            Assert.Equal(FileSensorOptions.MaxAllowedFileSizeBytes, FileSensorInstant.GetEffectiveMaxFileSizeBytes(FileSensorOptions.MaxAllowedFileSizeBytes + 1));
        }

        [Fact]
        public void Effective_cap_allows_exactly_the_ceiling()
        {
            Assert.Equal(FileSensorOptions.MaxAllowedFileSizeBytes, FileSensorInstant.GetEffectiveMaxFileSizeBytes(FileSensorOptions.MaxAllowedFileSizeBytes));
        }

        [Fact]
        public void Default_file_size_cap_is_within_the_ceiling()
        {
            Assert.True(new FileSensorOptions().MaxFileSizeBytes <= FileSensorOptions.MaxAllowedFileSizeBytes);
        }

        [Fact]
        public void AsList_preserves_content_and_count()
        {
            var bytes = new byte[] { 1, 2, 3, 255, 0, 42 };

            var list = bytes.AsList();

            Assert.Equal(bytes.Length, list.Count);
            Assert.Equal(bytes, list);
        }

        [Fact]
        public void AsList_of_empty_array_is_empty()
        {
            Assert.Empty(new byte[0].AsList());
        }

        [Fact]
        public void AsList_does_not_copy_the_buffer_when_zero_copy_is_supported()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var list = bytes.AsList();

            if (!ByteCollectionExtensions.ZeroCopySupported)
                return; // Fallback path copies by design; content equality is covered above.

            bytes[1] = 99;

            Assert.Equal(new List<byte> { 1, 99, 3 }, list);
        }

        [Fact]
        public void AsList_serializes_identically_to_a_copied_list()
        {
            // The wrapped list goes straight into the wire serializer — its JSON must be
            // byte-for-byte what a regular List<byte> produces.
            var bytes = new byte[256];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)i;

            Assert.Equal(JsonSerializer.Serialize(new List<byte>(bytes)), JsonSerializer.Serialize(bytes.AsList()));
        }

        [Fact]
        public void AsList_supports_linq_and_enumeration_like_a_regular_list()
        {
            var bytes = new byte[] { 5, 10, 15 };
            var list = bytes.AsList();

            Assert.Equal(30, list.Sum(b => (int)b));
            Assert.Equal(new byte[] { 5, 10, 15 }, list.ToArray());
            Assert.Equal(3, list.Count());
        }

        [Fact]
        public async Task Send_file_over_configured_cap_is_rejected_without_sending()
        {
            var sender = new RecordingFileSender();
            var filePath = Path.GetTempFileName();

            try
            {
                File.WriteAllBytes(filePath, new byte[200]);

                using (var collector = CreateCollector(sender))
                {
                    collector.CreateFileSensor("bounds/over-cap", new FileSensorOptions { MaxFileSizeBytes = 100 });
                    await collector.Start().ConfigureAwait(false);

                    Assert.False(await collector.SendFileAsync("bounds/over-cap", filePath).ConfigureAwait(false),
                        "A file larger than the configured cap must be rejected.");

                    await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
                    Assert.Empty(sender.CapturedFiles);
                }
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        [Fact]
        public async Task Sent_file_bytes_survive_the_zero_copy_path_end_to_end()
        {
            var sender = new RecordingFileSender();
            var filePath = Path.GetTempFileName();
            var payload = new byte[1024];
            new Random(42).NextBytes(payload);

            try
            {
                File.WriteAllBytes(filePath, payload);

                using (var collector = CreateCollector(sender))
                {
                    collector.CreateFileSensor("bounds/roundtrip", new FileSensorOptions());
                    await collector.Start().ConfigureAwait(false);

                    Assert.True(await collector.SendFileAsync("bounds/roundtrip", filePath).ConfigureAwait(false));

                    var captured = await sender.WaitForFileAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                    Assert.NotNull(captured);
                    Assert.Equal(payload, captured.Value);
                }
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        private static DataCollector CreateCollector(RecordingFileSender sender) => new DataCollector(new CollectorOptions
        {
            AccessKey = "file-bounds-key",
            ClientName = "file-bounds-client",
            ComputerName = "file-bounds-host",
            Module = "file-bounds-module",
            DataSender = sender,
            PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(1),
        });


        private sealed class RecordingFileSender : IDataSender
        {
            private readonly List<FileSensorValue> _files = new List<FileSensorValue>();
            private readonly TaskCompletionSource<bool> _fileReceived = new TaskCompletionSource<bool>();

            internal IReadOnlyList<FileSensorValue> CapturedFiles
            {
                get
                {
                    lock (_files)
                        return _files.ToList();
                }
            }

            internal async Task<FileSensorValue> WaitForFileAsync(TimeSpan timeout)
            {
                await Task.WhenAny(_fileReceived.Task, Task.Delay(timeout)).ConfigureAwait(false);

                lock (_files)
                    return _files.Count > 0 ? _files[0] : null;
            }

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() =>
                new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendCommandAsync(IEnumerable<HSMSensorDataObjects.CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
            {
                lock (_files)
                    _files.Add(file);

                _fileReceived.TrySetResult(true);
                return default;
            }
        }
    }
}
