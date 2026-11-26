using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.LargeFiles;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.SensorApi;
using HSMServer.ServerConfiguration;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    public sealed class LargeFileHistoryReadFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(LargeFileHistoryReadTests);
    }


    /// <summary>
    /// An access key reads the history of a File sensor that holds a large file value (#1885): through the Sensor API's
    /// history and historyFile routes, the large value is described (file id, name, extension, size, secure, state from
    /// the store's record) and its content is never part of the answer; an ordinary file value keeps its content.
    /// </summary>
    public class LargeFileHistoryReadTests : MonitoringCoreTestsBase<LargeFileHistoryReadFixture>
    {
        private const int Piece = 65_536;
        private const string ClientName = "large-file-history";

        private static readonly DateTime Modified = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _root = $"lh{Guid.NewGuid():N}"[..12];
        private readonly LargeFileUploadService _uploads;
        private readonly LargeFileStoreArchiveAdapter _largeFiles;


        public LargeFileHistoryReadTests(LargeFileHistoryReadFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _uploads = new LargeFileUploadService(_databaseCoreManager.DatabaseCore, _valuesCache, new LargeFileOptions { MinFreeDiskMb = 0 });
            _largeFiles = new LargeFileStoreArchiveAdapter(_databaseCoreManager.DatabaseCore.LargeFiles);
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TheHistory_DescribesALargeValueWithoutItsContent_AndKeepsTheContentOfAnOrdinaryOne(bool secure)
        {
            var sensor = await CreateFileSensorAsync("dumps");
            var large = RandomBytes(Piece + 10);
            var fileId = await UploadAsync(sensor, large, secure);
            var ordinary = await AddOrdinaryValueAsync(sensor, "notes", "txt");

            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), largeFiles: _largeFiles);
            var (status, body) = await host.PostAsync("history", HistoryBody(sensor.Path), _key, ClientName);

            Assert.Equal(HttpStatusCode.OK, status);
            using var json = JsonDocument.Parse(body);
            var values = json.RootElement.EnumerateArray().ToList();
            Assert.Equal(2, values.Count);

            var largeValue = values.Single(v => v.TryGetProperty("LargeFile", out _));
            Assert.Equal(string.Empty, largeValue.GetProperty("Value").GetString());
            // The name without its extension, as for every file value (#1886).
            Assert.Equal("core", largeValue.GetProperty("FileName").GetString());

            var description = largeValue.GetProperty("LargeFile");
            Assert.Equal(fileId, description.GetProperty("FileId").GetGuid());
            Assert.Equal("core", description.GetProperty("Name").GetString());
            Assert.Equal("dmp", description.GetProperty("Extension").GetString());
            Assert.Equal(large.Length, description.GetProperty("Size").GetInt64());
            Assert.Equal(secure, description.GetProperty("Secure").GetBoolean());
            Assert.Equal(LargeFileValueStates.Verified, description.GetProperty("State").GetString());

            // The ordinary value keeps its shape and its content, with no LargeFile member.
            var ordinaryValue = values.Single(v => !v.TryGetProperty("LargeFile", out _));
            Assert.Equal(ordinary, ordinaryValue.GetProperty("Value").GetBytesFromBase64());
            Assert.Equal("notes", ordinaryValue.GetProperty("FileName").GetString());
            Assert.Equal(new[] { "Comment", "Extension", "FileName", "Status", "Time", "Value" },
                ordinaryValue.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

            Assert.DoesNotContain(Convert.ToBase64String(large[..64]), body);
        }

        [Fact]
        public async Task TheStateComesFromTheStore_APackedFile_AndOneTheStoreNoLongerHolds()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            var packed = await UploadAsync(sensor, RandomBytes(Piece), secure: true);
            var removed = await UploadAsync(sensor, RandomBytes(Piece + 1, seed: 2), secure: false, name: "other.dmp");

            var target = _largeFiles.BeginArchive(packed).Target;
            System.IO.File.WriteAllBytes(target.TempPath, [1, 2, 3]);
            Assert.Equal(HSMServer.Core.LargeFiles.Archives.ArchiveCommitOutcome.Committed, _largeFiles.CommitArchive(target, 2, 3, SHA256.HashData(new byte[] { 1, 2, 3 })));
            Assert.True(_databaseCoreManager.DatabaseCore.LargeFiles.Remove(removed, "test"));

            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), largeFiles: _largeFiles);
            var (status, body) = await host.PostAsync("history", HistoryBody(sensor.Path), _key, ClientName);

            Assert.Equal(HttpStatusCode.OK, status);
            using var json = JsonDocument.Parse(body);
            var descriptions = json.RootElement.EnumerateArray().Select(v => v.GetProperty("LargeFile")).ToDictionary(d => d.GetProperty("FileId").GetGuid());

            Assert.Equal(LargeFileValueStates.Packed, descriptions[packed].GetProperty("State").GetString());
            Assert.True(descriptions[packed].GetProperty("Secure").GetBoolean());

            Assert.Equal(LargeFileValueStates.Removed, descriptions[removed].GetProperty("State").GetString());
            Assert.Equal(JsonValueKind.Null, descriptions[removed].GetProperty("Secure").ValueKind);
        }

        [Fact]
        public async Task TheCsvHistory_HasAnEmptyContentCell_AndTheDescriptionInTheLastColumn()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            var large = RandomBytes(Piece + 3);
            var fileId = await UploadAsync(sensor, large, secure: false);
            await AddOrdinaryValueAsync(sensor, "notes", "txt");

            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), largeFiles: _largeFiles);
            var (status, csv) = await host.PostAsync("historyFile",
                $"{{\"Path\":\"{sensor.Path}\",\"From\":\"2000-01-01T00:00:00Z\",\"To\":\"2100-01-01T00:00:00Z\",\"FileName\":\"h\",\"Extension\":\"csv\"}}", _key, ClientName);

            Assert.Equal(HttpStatusCode.OK, status);

            var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            var separator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
            Assert.EndsWith($"{separator}Large file", lines[0]);

            var largeRow = lines.Single(line => line.Contains(fileId.ToString()));
            Assert.Contains($"{separator}\"\"{separator}\"core\"{separator}\"dmp\"", largeRow);
            Assert.EndsWith($"{separator}\"Large file {fileId}; {large.Length} bytes; not secure; verified\"", largeRow);
            Assert.DoesNotContain(Convert.ToBase64String(large[..64]), csv);

            // The ordinary row has an empty last cell.
            var ordinaryRow = lines.Single(line => line != lines[0] && line != largeRow);
            Assert.EndsWith($"{separator}\"\"", ordinaryRow);
        }

        [Fact]
        public async Task TheCsvHistoryOfOrdinaryValuesOnly_KeepsItsColumns()
        {
            var sensor = await CreateFileSensorAsync("notes");
            await AddOrdinaryValueAsync(sensor, "notes", "txt");

            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), largeFiles: _largeFiles);
            var (status, csv) = await host.PostAsync("historyFile",
                $"{{\"Path\":\"{sensor.Path}\",\"From\":\"2000-01-01T00:00:00Z\",\"To\":\"2100-01-01T00:00:00Z\",\"FileName\":\"h\",\"Extension\":\"csv\"}}", _key, ClientName);

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.DoesNotContain("Large file", csv);
        }


        private static string HistoryBody(string path) =>
            $"{{\"Path\":\"{path}\",\"From\":\"2000-01-01T00:00:00Z\",\"To\":\"2100-01-01T00:00:00Z\"}}";

        private async Task<Guid> UploadAsync(BaseSensorModel sensor, byte[] content, bool secure, string name = "core.dmp")
        {
            var begin = await _uploads.BeginAsync(new LargeFileBeginRequest(_product, sensor.Path, name, content.Length, Modified, null, Piece, Piece, Secure: secure), default);
            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);

            for (var i = 0; i * Piece < content.Length; i++)
            {
                var piece = content.AsMemory(i * Piece, Math.Min(Piece, content.Length - i * Piece));
                Assert.Equal(LargeFileAppendOutcome.Stored, (await _uploads.AppendAsync(begin.Lease, i, piece, SHA256.HashData(piece.Span), default)).Outcome);
            }

            var finish = await _uploads.FinishAsync(begin.Lease, SHA256.HashData(content), new StoredBytesHashChecker(), default);
            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);
            _uploads.Release(begin.Lease);

            return finish.FileId;
        }

        private async Task<byte[]> AddOrdinaryValueAsync(BaseSensorModel sensor, string name, string extension)
        {
            var content = RandomBytes(300, seed: 5);
            var value = new FileSensorValue
            {
                Path = sensor.Path,
                Name = name,
                Extension = extension,
                Value = [.. content],
                Time = DateTime.UtcNow.AddSeconds(1),
            };

            var result = await _valuesCache.AddSensorValueAsync(Guid.Parse(_key), _product, value);
            Assert.True(result.IsOk, result.Error);

            return content;
        }

        private async Task<BaseSensorModel> CreateFileSensorAsync(string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = HSMCommon.Model.SensorType.File,
                TypeIsExplicit = true,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));
            return sensor;
        }

        private static byte[] RandomBytes(int length, int seed = 1)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }
    }
}
