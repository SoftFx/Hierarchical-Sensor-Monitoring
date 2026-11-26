using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.Tests.LargeFiles.Archives;
using HSMServer.Model.ManagementApi.LargeFiles;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // A large file from the store to the client (#1883 + #1885), with nothing faked between them: a file made verified
    // through the store's own upload path, packed by the real ArchiveBuilder with the real SharpZipLib writer, described
    // and downloaded through the real LargeFileDownloadService and the API controller in a TestServer, and recorded in
    // the real download log. Only the dump passwords (in memory), the tree and the tokens are the world's.
    // One test opens the archive in 7-Zip, so the class runs alone (SevenZipAloneCollection).
    [Collection(HSMServer.Core.Tests.LargeFiles.Archives.SevenZipAloneCollection.Name)]
    public sealed class LargeFileEndToEndTests : IDisposable
    {
        private const string FirstPassword = "End-To-End-Pass-One-2026";
        private const string SecondPassword = "End-To-End-Pass-Two-2026";

        private readonly LargeFileWorld _world = new();
        private readonly RealLargeFileStore _real = new();
        private readonly FakeDumpPasswordService _passwords = new();
        private readonly InMemoryDownloadLogStore _logStore = new();
        private readonly ArchiveBuilder _builder;
        private readonly byte[] _content = RealLargeFileStore.Content(3 * RealLargeFileStore.Piece + 4321);


        public LargeFileEndToEndTests()
        {
            _builder = new ArchiveBuilder(_real.Adapter, new SharpZipArchiveWriter(), _passwords, () => DateTime.UtcNow);

            _world.ArchiveStore = _real.Adapter;
            _world.ArchiveBuilder = _builder;
            _world.PasswordService = _passwords;
            _world.DownloadLog = new DownloadLog(_logStore);
        }

        public void Dispose()
        {
            _builder.Dispose();
            _real.Dispose();
            _world.Dispose();
        }


        [Fact]
        public async Task ASecureFile_IsPackedUnderThePassword_DownloadedAndOpensWithIt_AndTheDownloadIsRecorded()
        {
            _passwords.Set(_world.ProductA.Id, FirstPassword);
            var fileId = _real.Verify(_world.ProductA.Id, _world.SensorA.Id, _content, "app.dmp", secure: true);

            var file = await PackAsync(fileId);
            Assert.Equal(1, file.PasswordVersion);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ViewerA, readOnly: true));
            using var response = await client.GetAsync(Url(file));
            var archive = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(file.ArchiveLength, archive.Length);
            Assert.Equal(file.ArchiveSha256, SHA256.HashData(archive));
            Assert.Equal("1", Header(response, LargeFileDownloadResult.PasswordVersionHeader));
            Assert.Equal("app.dmp.zip", response.Content.Headers.ContentDisposition?.FileNameStar);

            Assert.Equal(_content, Open(archive, FirstPassword));
            Assert.ThrowsAny<Exception>(() => Open(archive, SecondPassword));
            Assert.ThrowsAny<Exception>(() => Open(archive, null));

            var record = Assert.Single(_world.DownloadLog.ReadFile(_world.ProductA.Id, fileId, 10));
            Assert.Equal(file.ArchiveId, record.ArchiveId);
            Assert.Equal(1, record.PasswordVersion);
            Assert.Equal(DownloadChannel.Api, record.Channel);
            Assert.Equal(_world.ViewerA.Id, record.UserId);
            Assert.Equal("app.dmp.zip", record.FileName);
        }

        [Fact]
        public async Task AFileThatIsNotSecure_IsPackedWithoutAPassword_AndOpensWithoutOne()
        {
            // The product has no password: a file that is not secure never waits for one.
            var fileId = _real.Verify(_world.ProductA.Id, _world.SensorA.Id, _content, "trace.log", secure: false);

            var file = await PackAsync(fileId);
            Assert.Null(file.PasswordVersion);
            Assert.False(file.IsSecure);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));
            using var response = await client.GetAsync(Url(file));
            var archive = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(Header(response, LargeFileDownloadResult.PasswordVersionHeader));
            Assert.Equal(_content, Open(archive, null));
            Assert.ThrowsAny<Exception>(() => Open(archive, FirstPassword));

            var record = Assert.Single(_world.DownloadLog.ReadFile(_world.ProductA.Id, fileId, 10));
            Assert.Equal(0, record.PasswordVersion);
        }

        [Fact]
        public async Task ARangeAndAResume_GiveTheWholeArchive()
        {
            _passwords.Set(_world.ProductA.Id, FirstPassword);
            var file = await PackAsync(_real.Verify(_world.ProductA.Id, _world.SensorA.Id, _content));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var first = new HttpRequestMessage(HttpMethod.Get, Url(file));
            first.Headers.Range = new RangeHeaderValue(0, 999);
            using var part = await client.SendAsync(first);
            var head = await part.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
            Assert.Equal($"bytes 0-999/{file.ArchiveLength}", part.Content.Headers.ContentRange?.ToString());

            using var resume = new HttpRequestMessage(HttpMethod.Get, Url(file));
            resume.Headers.Range = new RangeHeaderValue(1000, null);
            resume.Headers.IfRange = new RangeConditionHeaderValue(part.Headers.ETag);
            using var rest = await client.SendAsync(resume);

            Assert.Equal(HttpStatusCode.PartialContent, rest.StatusCode);

            byte[] archive = [.. head, .. await rest.Content.ReadAsByteArrayAsync()];
            Assert.Equal(file.ArchiveSha256, SHA256.HashData(archive));
            Assert.Equal(_content, Open(archive, FirstPassword));

            // Both requests of one caller for one archive are one record.
            Assert.Single(_world.DownloadLog.ReadFile(_world.ProductA.Id, file.FileId, 10));
        }

        [Fact]
        public async Task AReencryption_MakesANewArchive_TheOldLinkAnswers404_AndTheNewOneOpensWithTheNewPassword()
        {
            _passwords.Set(_world.ProductA.Id, FirstPassword);
            var old = await PackAsync(_real.Verify(_world.ProductA.Id, _world.SensorA.Id, _content));
            var oldPath = _real.Adapter.ArchivePath(old);

            Assert.Equal(2, _passwords.Set(_world.ProductA.Id, SecondPassword));
            await _builder.RunDueAsync(CancellationToken.None);

            var current = _real.Adapter.TryGetFile(old.FileId);
            Assert.Equal(LargeArchiveFileState.Packed, current.State);
            Assert.NotEqual(old.ArchiveId, current.ArchiveId);
            Assert.Equal(2, current.PasswordVersion);
            Assert.False(File.Exists(oldPath));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using (var stale = await client.GetAsync(Url(old)))
                Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);

            // The description names the new archive and its link.
            using (var description = await client.GetAsync($"/api/v1/sensors/{_world.SensorA.Id}/large-files/{old.FileId}"))
            {
                var text = await description.Content.ReadAsStringAsync();
                Assert.Contains(current.ArchiveId.Value.ToString(), text);
                Assert.DoesNotContain(SecondPassword, text);
            }

            using var response = await client.GetAsync(Url(current));
            var archive = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("2", Header(response, LargeFileDownloadResult.PasswordVersionHeader));
            Assert.Equal(_content, Open(archive, SecondPassword));
            Assert.ThrowsAny<Exception>(() => Open(archive, FirstPassword));
        }

        // The hand-over to the bundled Caddy names a file that is where the store keeps the archive, under the folder
        // Caddy mounts.
        [Fact]
        public async Task TheHandOver_NamesTheArchiveTheStoreKeeps()
        {
            _world.TrustProxies();
            var file = await PackAsync(_real.Verify(_world.ProductA.Id, _world.SensorA.Id, _content, secure: false));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            LargeFileWorld.FromPeer(request, LargeFileWorld.ProxyPeer);
            using var response = await client.SendAsync(request);

            var relative = Header(response, LargeFileDownloadResult.ArchiveHeader);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(LargeArchivePaths.RelativeArchivePath(_world.ProductA.Id, file.ArchiveId.Value), relative);

            var served = Path.Combine(_real.Root, "archives", relative);
            Assert.Equal(_content, Open(File.ReadAllBytes(served), null));
        }

        [SevenZipFact]
        public async Task TheDownloadedSecureArchive_PassesSevenZipWithThePassword()
        {
            _passwords.Set(_world.ProductA.Id, FirstPassword);
            var file = await PackAsync(_real.Verify(_world.ProductA.Id, _world.SensorA.Id, _content, "app.dmp"));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));
            var archive = await client.GetByteArrayAsync(Url(file));

            var path = Path.Combine(_real.Root, "downloaded.zip");
            await File.WriteAllBytesAsync(path, archive);

            var (tested, output) = SevenZip.Run("t", $"-p{FirstPassword}", path);
            Assert.True(tested == 0, output);

            var (listed, listing) = SevenZip.Run("l", "-slt", path);
            Assert.True(listed == 0, listing);
            Assert.Contains("Path = app.dmp", listing);
            Assert.Contains("AES-256", listing);

            var (extracted, bytes) = SevenZip.RunToBytes("x", "-so", $"-p{FirstPassword}", path);
            Assert.Equal(0, extracted);
            Assert.Equal(SHA256.HashData(_content), SHA256.HashData(bytes));

            var (wrong, _) = SevenZip.Run("t", $"-p{SecondPassword}", path);
            Assert.NotEqual(0, wrong);
        }


        private async Task<LargeArchiveFile> PackAsync(Guid fileId)
        {
            await _builder.RunDueAsync(CancellationToken.None);

            var file = _real.Adapter.TryGetFile(fileId);
            Assert.True(file.State == LargeArchiveFileState.Packed, _builder.GetStatus(fileId)?.LastError ?? $"{file.State}");

            return file;
        }

        private static string Url(LargeArchiveFile file) => LargeFileDownloadService.ApiDownloadPath(file.FileId, file.ArchiveId.Value);

        private static string Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values) ? values.Single() : null;

        private static byte[] Open(byte[] archive, string password)
        {
            using var stream = new MemoryStream(archive);
            using var entry = new SharpZipArchiveWriter().OpenEntry(stream, password);

            return SharpZipArchiveWriterTests.ReadAll(entry.Content);
        }
    }
}
