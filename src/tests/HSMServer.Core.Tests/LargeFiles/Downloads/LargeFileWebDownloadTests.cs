using System;
using System.Net;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.LargeFiles;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The web download of a large file's archive (#1885): who may download, and that everything which may not disclose
    // the file answers the SAME 404 (status, body and headers), through the real routing and authorization.
    public sealed class LargeFileWebDownloadTests : IDisposable
    {
        private readonly LargeFileWorld _world = new();
        private readonly byte[] _archive = LargeFileWorld.ArchiveBytes();
        private readonly LargeArchiveFile _file;


        public LargeFileWebDownloadTests()
        {
            _file = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive);
        }

        public void Dispose() => _world.Dispose();


        private static string Url(Guid fileId, Guid archiveId) => LargeFileDownloadService.WebDownloadPath(fileId, archiveId);


        [Theory]
        [InlineData("admin")]
        [InlineData("managerA")]
        [InlineData("viewerA")]
        public async Task EveryoneWhoSeesTheProduct_DownloadsTheArchive(string userName)
        {
            using var host = _world.BuildHost();
            using var client = await LargeFileWorld.WebClient(host, UserNamed(userName));

            using var response = await client.GetAsync(Url(_file.FileId, _file.ArchiveId.Value));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_archive, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            _world.Log.Verify(l => l.Record(It.Is<DownloadCaller>(c => c.Channel == DownloadChannel.Web && c.UserName == userName),
                It.Is<DownloadTarget>(t => t.FileId == _file.FileId && t.ArchiveId == _file.ArchiveId), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task EveryHiddenCase_AnswersTheSame404()
        {
            var otherProduct = _world.Store.AddPacked(_world.ProductB.Id, _world.SensorB.Id, _archive);
            var uploading = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Uploading);

            // A record whose sensor is gone, and one whose sensor moved to another product than the file's.
            var orphan = _world.Store.AddPacked(_world.ProductA.Id, Guid.NewGuid(), _archive);
            var moved = _world.Store.AddPacked(_world.ProductB.Id, _world.SensorA.Id, _archive);

            using var host = _world.BuildHost();
            using var manager = await LargeFileWorld.WebClient(host, _world.ManagerA);
            using var stranger = await LargeFileWorld.WebClient(host, _world.ManagerB);

            var answers = new[]
            {
                await Get(stranger, Url(_file.FileId, _file.ArchiveId.Value)),            // a user without the product
                await Get(manager, Url(Guid.NewGuid(), _file.ArchiveId.Value)),           // an unknown file
                await Get(manager, Url(otherProduct.FileId, otherProduct.ArchiveId.Value)), // a file of another product
                await Get(manager, Url(_file.FileId, Guid.NewGuid())),                    // a stale archive id
                await Get(manager, Url(uploading.FileId, Guid.NewGuid())),                // a file in upload
                await Get(manager, Url(orphan.FileId, orphan.ArchiveId.Value)),           // its sensor is gone
                await Get(manager, Url(moved.FileId, moved.ArchiveId.Value)),             // its sensor is in another product
            };

            Assert.Equal(404, answers[0].Status);
            Assert.Equal("The file was not found.", answers[0].Body);
            Assert.DoesNotContain(LargeFileDownloadResult.ArchiveHeader, answers[0].Headers);
            Assert.All(answers, answer => Assert.Equal(answers[0], answer));

            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task AVerifiedFile_Answers409WithTheReason()
        {
            var verified = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified);
            _world.Passwords.Setup(p => p.HasPassword(_world.ProductA.Id)).Returns(false);

            using var host = _world.BuildHost();
            using var client = await LargeFileWorld.WebClient(host, _world.ViewerA);

            var answer = await Get(client, Url(verified.FileId, Guid.NewGuid()));

            Assert.Equal(409, answer.Status);
            Assert.Equal(LargeFileDownloadService.WaitingForPasswordReason, answer.Body);
            Assert.DoesNotContain(LargeFileDownloadResult.ArchiveHeader, answer.Headers);
        }

        [Fact]
        public async Task NotSignedIn_IsRedirectedToTheLoginPage()
        {
            using var host = _world.BuildHost();
            using var client = await LargeFileWorld.WebClient(host, null);

            using var response = await client.GetAsync(Url(_file.FileId, _file.ArchiveId.Value));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/Index", response.Headers.Location?.PathAndQuery);
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task AnApiToken_DoesNotOpenTheWebRoute()
        {
            // The web route is cookie-only: a bearer token is not a signed-in user.
            var credential = _world.AddToken(_world.Admin);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, credential);

            using var response = await client.GetAsync(Url(_file.FileId, _file.ArchiveId.Value));

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);
        }


        private User UserNamed(string name) => name switch
        {
            "admin" => _world.Admin,
            "managerA" => _world.ManagerA,
            "viewerA" => _world.ViewerA,
            _ => _world.ManagerB,
        };

        private static async Task<(int Status, string Body, string Headers)> Get(System.Net.Http.HttpClient client, string url)
        {
            using var response = await client.GetAsync(url);

            return await LargeFileWorld.Snapshot(response);
        }
    }
}
