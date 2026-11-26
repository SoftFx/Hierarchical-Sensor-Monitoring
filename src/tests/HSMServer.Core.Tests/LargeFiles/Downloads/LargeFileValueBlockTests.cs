using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.Model;
using HSMServer.Model.ManagementApi.LargeFiles;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The large file block of a File sensor value in the sensor reads of /api/v1 (#1885): GET /api/v1/sensors/{id} and its
    // history give a large file value its file id, kind, state and, once packed, its archive with the download link; an
    // ordinary file value has none; no password is ever part of it.
    public sealed class LargeFileValueBlockTests : IDisposable
    {
        private readonly LargeFileWorld _world = new();
        private readonly byte[] _archive = LargeFileWorld.ArchiveBytes();


        public void Dispose() => _world.Dispose();


        [Fact]
        public async Task ThePackedLargeValueOfASensor_CarriesItsArchiveAndLink_AndNoPassword()
        {
            var file = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, passwordVersion: 3, name: "core.dmp");
            Assert.True(_world.SensorA.TryAddValue(LargeValue(file.FileId)));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ViewerA, readOnly: true));

            using var response = await client.GetAsync($"/api/v1/sensors/{_world.SensorA.Id}");
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(LargeFileWorld.Password, text);

            using var body = JsonDocument.Parse(text);
            var value = body.RootElement.GetProperty("lastValue").GetProperty("value");
            Assert.Equal("core.dmp", value.GetProperty("name").GetString());

            var block = value.GetProperty("largeFile");
            Assert.Equal(file.FileId, block.GetProperty("fileId").GetGuid());
            Assert.True(block.GetProperty("secure").GetBoolean());
            Assert.Equal(LargeFileValueStates.Packed, block.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, block.GetProperty("notReadyReason").ValueKind);

            var archive = block.GetProperty("archive");
            Assert.Equal(file.ArchiveId.Value, archive.GetProperty("id").GetGuid());
            Assert.Equal(_archive.Length, archive.GetProperty("size").GetInt64());
            Assert.Equal(Convert.ToHexString(file.ArchiveSha256).ToLowerInvariant(), archive.GetProperty("sha256").GetString());
            Assert.Equal(3, archive.GetProperty("passwordVersion").GetInt32());

            // The link is absolute on this host and downloads with the same token.
            var link = new Uri(archive.GetProperty("downloadUrl").GetString());
            Assert.Equal(LargeFileDownloadService.ApiDownloadPath(file.FileId, file.ArchiveId.Value), link.AbsolutePath);

            using var download = await client.GetAsync(link.PathAndQuery);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(_archive, await download.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task ANotSecureValue_HasNoPasswordVersion_AVerifiedOneItsReason_AndOneTheStoreNoLongerHoldsIsRemoved()
        {
            var notSecure = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, secure: false);
            var verified = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified);
            var gone = Guid.NewGuid();
            var t0 = DateTime.UtcNow.AddMinutes(-10);

            List<BaseValue> page =
            [
                LargeValue(gone, t0.AddMinutes(3)),
                Ordinary(t0.AddMinutes(2)),
                LargeValue(verified.FileId, t0.AddMinutes(1)),
                LargeValue(notSecure.FileId, t0),
            ];
            _world.Cache.Setup(c => c.GetSensorValuesPage(_world.SensorA.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<HSMSensorDataObjects.HistoryRequests.RequestOptions>()))
                .Returns(Pages(page));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var response = await client.GetAsync($"/api/v1/sensors/{_world.SensorA.Id}/history?from={t0.AddHours(-1):O}&to={t0.AddHours(1):O}");
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(text);
            var values = body.RootElement.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value")).ToList();
            Assert.Equal(4, values.Count);

            // Oldest first.
            var notSecureBlock = values[0].GetProperty("largeFile");
            Assert.False(notSecureBlock.GetProperty("secure").GetBoolean());
            Assert.Equal(LargeFileValueStates.Packed, notSecureBlock.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, notSecureBlock.GetProperty("archive").GetProperty("passwordVersion").ValueKind);

            var verifiedBlock = values[1].GetProperty("largeFile");
            Assert.Equal(LargeFileValueStates.Verified, verifiedBlock.GetProperty("state").GetString());
            Assert.Equal(LargeFileDownloadService.BeingPackedReason, verifiedBlock.GetProperty("notReadyReason").GetString());
            Assert.Equal(JsonValueKind.Null, verifiedBlock.GetProperty("archive").ValueKind);

            Assert.Equal(JsonValueKind.Null, values[2].GetProperty("largeFile").ValueKind);

            var goneBlock = values[3].GetProperty("largeFile");
            Assert.Equal(gone, goneBlock.GetProperty("fileId").GetGuid());
            Assert.Equal(LargeFileValueStates.Removed, goneBlock.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, goneBlock.GetProperty("secure").ValueKind);
            Assert.Equal(JsonValueKind.Null, goneBlock.GetProperty("archive").ValueKind);
        }

        [Fact]
        public async Task AFileOfAnotherSensor_IsNotDescribedThroughThisSensorsValue()
        {
            var other = _world.Store.AddPacked(_world.ProductA.Id, Guid.NewGuid(), _archive);
            Assert.True(_world.SensorA.TryAddValue(LargeValue(other.FileId)));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/sensors/{_world.SensorA.Id}"));
            var block = body.RootElement.GetProperty("lastValue").GetProperty("value").GetProperty("largeFile");

            Assert.Equal(LargeFileValueStates.Removed, block.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, block.GetProperty("archive").ValueKind);
        }

        [Fact]
        public void AStoreThatCannotBeRead_DescribesTheValueAsUnavailable()
        {
            var store = new Mock<ILargeFileArchiveStore>();
            store.Setup(s => s.TryGetFile(It.IsAny<Guid>())).Throws(new System.IO.IOException("the store cannot open"));

            var service = new LargeFileDownloadService(store.Object, _world.Cache.Object, _world.CreateEvaluator(), _world.Tokens.Object, _world.Users.Object,
                _world.Builder.Object, _world.Passwords.Object, _world.Log.Object, _world.Monitor.Object, TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LargeFileDownloadService>.Instance);

            var fileId = Guid.NewGuid();
            var block = service.DescribeValue(_world.SensorA.Id, fileId, "https://hsm");

            Assert.Equal(fileId, block.FileId);
            Assert.Equal(LargeFileValueStates.Unavailable, block.State);
            Assert.Null(block.Secure);
            Assert.Null(block.Archive);
        }


        private static LargeFileValue LargeValue(Guid fileId, DateTime? time = null) => new()
        {
            Time = time ?? DateTime.UtcNow,
            Value = [],
            Name = "core.dmp",
            Extension = "dmp",
            OriginalSize = 3L << 30,
            FileId = fileId,
            Hash = new byte[32],
            Status = SensorStatus.Ok,
        };

        private static FileValue Ordinary(DateTime time) => new()
        {
            Time = time,
            Value = [1, 2, 3],
            Name = "notes",
            Extension = "txt",
            OriginalSize = 3,
            Status = SensorStatus.Ok,
        };

        private static async IAsyncEnumerable<List<BaseValue>> Pages(List<BaseValue> newestFirst)
        {
            await Task.Yield();
            yield return newestFirst;
        }
    }
}
