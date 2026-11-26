using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.LargeFiles;
using Microsoft.AspNetCore.HttpOverrides;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The management API download of a large file's archive (#1885) through the real routing, the real HsmApiToken scheme
    // and the real owner-mirroring evaluator: the access matrix, the download log, byte ranges and resumption, the hand-over
    // to the bundled Caddy, and the archive replaced in the middle of a download.
    public sealed class LargeFileApiDownloadTests : IDisposable
    {
        private readonly LargeFileWorld _world = new();
        private readonly byte[] _archive = LargeFileWorld.ArchiveBytes();
        private readonly LargeArchiveFile _file;


        public LargeFileApiDownloadTests()
        {
            _file = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, name: "Дамп сервера \"prod\"", extension: "dmp");
        }

        public void Dispose() => _world.Dispose();


        private string Url(LargeArchiveFile file) => LargeFileDownloadService.ApiDownloadPath(file.FileId, file.ArchiveId.Value);


        [Theory]
        [InlineData("admin", false)]
        [InlineData("managerA", false)]
        [InlineData("viewerA", false)]
        [InlineData("managerA", true)]
        public async Task ATokenOfAnOwnerWhoSeesTheProduct_Downloads(string owner, bool readOnly)
        {
            var credential = _world.AddToken(owner switch { "admin" => _world.Admin, "viewerA" => _world.ViewerA, _ => _world.ManagerA }, readOnly);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, credential);
            using var response = await client.GetAsync(Url(_file));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_archive, await response.Content.ReadAsByteArrayAsync());

            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(_archive))}:", Header(response, LargeFileDownloadResult.ReprDigestHeader));
            Assert.Equal("1", Header(response, LargeFileDownloadResult.PasswordVersionHeader));
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true }, Header(response, HeaderNames.CacheControl));
            Assert.Equal($"\"{_file.ArchiveId:N}\"", response.Headers.ETag?.ToString());
            Assert.Equal("bytes", Header(response, HeaderNames.AcceptRanges));
            Assert.Null(Header(response, LargeFileDownloadResult.ArchiveHeader));

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.Equal("attachment", disposition?.DispositionType);
            Assert.Equal("Дамп сервера _prod_.dmp.zip", disposition?.FileNameStar);
            Assert.True(disposition?.FileName?.Trim('"').All(symbol => symbol is >= ' ' and <= '~'), disposition?.FileName);

            _world.Log.Verify(l => l.Record(
                It.Is<DownloadCaller>(c => c.Channel == DownloadChannel.Api && c.TokenId != null && c.TokenName == $"token of {owner}"),
                It.Is<DownloadTarget>(t => t.FileId == _file.FileId && t.ArchiveId == _file.ArchiveId && t.PasswordVersion == 1 && t.ProductId == _world.ProductA.Id),
                It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task AnOwnerWhoCannotSeeTheProduct_AndAnUnknownId_GetTheSame404()
        {
            var stranger = _world.AddToken(_world.ManagerB);
            var manager = _world.AddToken(_world.ManagerA);
            var uploading = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Uploading);

            using var host = _world.BuildHost();
            using var strangerClient = LargeFileWorld.ApiClient(host, stranger);
            using var managerClient = LargeFileWorld.ApiClient(host, manager);

            var answers = new[]
            {
                await Snapshot(strangerClient.GetAsync(Url(_file))),
                await Snapshot(managerClient.GetAsync(LargeFileDownloadService.ApiDownloadPath(Guid.NewGuid(), _file.ArchiveId.Value))),
                await Snapshot(managerClient.GetAsync(LargeFileDownloadService.ApiDownloadPath(_file.FileId, Guid.NewGuid()))),
                await Snapshot(managerClient.GetAsync(LargeFileDownloadService.ApiDownloadPath(uploading.FileId, Guid.NewGuid()))),
            };

            Assert.Equal(404, answers[0].Status);

            using (var body = JsonDocument.Parse(answers[0].Body))
            {
                Assert.Equal(ManagementApiErrors.NotFoundCode, body.RootElement.GetProperty("error").GetString());
                Assert.Equal(ManagementApiErrors.NotFoundMessage, body.RootElement.GetProperty("message").GetString());
            }

            Assert.All(answers, answer => Assert.Equal(answers[0], answer));
            Assert.All(answers, answer => Assert.DoesNotContain(LargeFileDownloadResult.ArchiveHeader, answer.Headers));
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task NoToken_AndAnAccessKey_Answer401()
        {
            using var host = _world.BuildHost();

            using var anonymous = LargeFileWorld.ApiClient(host, null);
            using var withoutToken = await anonymous.GetAsync(Url(_file));

            using var keyClient = LargeFileWorld.ApiClient(host, null);
            keyClient.DefaultRequestHeaders.Add("Key", Guid.NewGuid().ToString());
            using var withKey = await keyClient.GetAsync(Url(_file));

            Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, withKey.StatusCode);
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task AVerifiedFile_Answers409WithTheReason()
        {
            var verified = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified);
            _world.Builder.Setup(b => b.GetStatus(verified.FileId)).Returns(new ArchiveBuildStatus(ArchiveBuildState.WaitingForRoom, 1, null, null));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ViewerA));

            var answer = await Snapshot(client.GetAsync(LargeFileDownloadService.ApiDownloadPath(verified.FileId, Guid.NewGuid())));

            Assert.Equal(409, answer.Status);
            using var body = JsonDocument.Parse(answer.Body);
            Assert.Equal(ManagementApiErrors.ConflictCode, body.RootElement.GetProperty("error").GetString());
            Assert.Equal(LargeFileDownloadService.WaitingForRoomReason, body.RootElement.GetProperty("message").GetString());
            Assert.DoesNotContain(LargeFileDownloadResult.ArchiveHeader, answer.Headers);
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ANotSecureArchive_Downloads_WithoutThePasswordVersionHeader_WhetherOrNotTheProductHasAPassword(bool productHasPassword)
        {
            _world.Passwords.Setup(p => p.HasPassword(_world.ProductA.Id)).Returns(productHasPassword);
            var open = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, name: "trace", extension: "log", secure: false);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ViewerA));
            using var response = await client.GetAsync(Url(open));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_archive, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(_archive))}:", Header(response, LargeFileDownloadResult.ReprDigestHeader));
            Assert.False(response.Headers.Contains(LargeFileDownloadResult.PasswordVersionHeader));
            Assert.False(response.Content.Headers.Contains(LargeFileDownloadResult.PasswordVersionHeader));
            Assert.Equal("trace.log.zip", response.Content.Headers.ContentDisposition?.FileNameStar);

            // Recorded like a secure download, with no password version.
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(),
                It.Is<DownloadTarget>(t => t.FileId == open.FileId && t.ArchiveId == open.ArchiveId && t.PasswordVersion == 0),
                It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task ANotSecureFileNotPackedYet_Answers409BeingPacked_NeverWaitingForAPassword()
        {
            _world.Passwords.Setup(p => p.HasPassword(_world.ProductA.Id)).Returns(false);
            var verified = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified, secure: false);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ViewerA));

            var answer = await Snapshot(client.GetAsync(LargeFileDownloadService.ApiDownloadPath(verified.FileId, Guid.NewGuid())));

            Assert.Equal(409, answer.Status);
            using var body = JsonDocument.Parse(answer.Body);
            Assert.Equal(LargeFileDownloadService.BeingPackedReason, body.RootElement.GetProperty("message").GetString());
        }

        [Fact]
        public async Task NotSecureAndSecureFiles_HideBehindTheSame404()
        {
            var open = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, secure: false);
            var stranger = _world.AddToken(_world.ManagerB);
            var manager = _world.AddToken(_world.ManagerA);

            using var host = _world.BuildHost();
            using var strangerClient = LargeFileWorld.ApiClient(host, stranger);
            using var managerClient = LargeFileWorld.ApiClient(host, manager);

            var answers = new[]
            {
                await Snapshot(strangerClient.GetAsync(Url(_file))),
                await Snapshot(strangerClient.GetAsync(Url(open))),
                await Snapshot(managerClient.GetAsync(LargeFileDownloadService.ApiDownloadPath(open.FileId, Guid.NewGuid()))),
            };

            Assert.Equal(404, answers[0].Status);
            Assert.All(answers, answer => Assert.Equal(answers[0], answer));
        }

        [Fact]
        public async Task TheHandOverOfANotSecureArchive_CarriesNoPasswordVersion()
        {
            _world.TrustProxies();
            var open = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, secure: false);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(open));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            LargeFileWorld.FromPeer(request, LargeFileWorld.ProxyPeer);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal($"{_world.ProductA.Id:N}/{open.ArchiveId:N}.zip", Header(response, LargeFileDownloadResult.ArchiveHeader));
            Assert.NotNull(Header(response, LargeFileDownloadResult.ReprDigestHeader));
            Assert.Null(Header(response, LargeFileDownloadResult.PasswordVersionHeader));
        }

        [Fact]
        public void ASecureRecordWithoutAPasswordVersion_IsNotServed()
        {
            // A packed secure record always names its version; one that names none (or 0) is a store defect.
            foreach (var version in new int?[] { null, 0 })
            {
                var broken = _world.Store.Add(_file with { FileId = Guid.NewGuid(), PasswordVersion = version });
                var decision = _world.CreateService().Decide(broken.FileId, broken.ArchiveId.Value, LargeFileCaller.Web(_world.ManagerA, null), record: false);

                Assert.Equal(LargeFileDownloadOutcome.NotFound, decision.Outcome);
            }
        }

        [Fact]
        public async Task EveryGetIsRecorded_AHeadIsNot()
        {
            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using (var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url(_file))))
            {
                Assert.Equal(HttpStatusCode.OK, head.StatusCode);
                Assert.Equal(_archive.Length, head.Content.Headers.ContentLength);
            }

            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);

            using (await client.GetAsync(Url(_file))) { }
            using (await client.GetAsync(Url(_file))) { }

            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Exactly(2));
            _world.Monitor.Verify(m => m.ReportDownloadLog(true, null), Times.Exactly(2));
        }

        [Fact]
        public async Task AFailingDownloadLog_Answers503_WithoutTheFile()
        {
            _world.Log.Setup(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()))
                .Throws(new DownloadLogException("the log database is read-only"));
            _world.TrustProxies();

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            // Even a request the bundled Caddy forwarded gets no instruction when the record failed.
            using var request = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            LargeFileWorld.FromPeer(request, LargeFileWorld.ProxyPeer);

            var answer = await Snapshot(client.SendAsync(request));

            Assert.Equal(503, answer.Status);
            using var body = JsonDocument.Parse(answer.Body);
            Assert.Equal(ManagementApiErrors.ServiceUnavailableCode, body.RootElement.GetProperty("error").GetString());
            Assert.DoesNotContain(LargeFileDownloadResult.ArchiveHeader, answer.Headers);
            Assert.DoesNotContain("Repr-Digest", answer.Headers);
            Assert.DoesNotContain("Content-Disposition", answer.Headers);
            _world.Monitor.Verify(m => m.ReportDownloadLog(false, "the log database is read-only"), Times.Once);
        }


        [Fact]
        public async Task ARange_Answers206_AndAResumeWithTheETagContinues()
        {
            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var first = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            first.Headers.Range = new RangeHeaderValue(0, 999);
            using var part = await client.SendAsync(first);

            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
            Assert.Equal($"bytes 0-999/{_archive.Length}", part.Content.Headers.ContentRange?.ToString());
            Assert.Equal(_archive[..1000], await part.Content.ReadAsByteArrayAsync());

            using var resume = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            resume.Headers.Range = new RangeHeaderValue(1000, null);
            resume.Headers.IfRange = new RangeConditionHeaderValue(part.Headers.ETag);
            using var rest = await client.SendAsync(resume);

            Assert.Equal(HttpStatusCode.PartialContent, rest.StatusCode);
            Assert.Equal(_archive[1000..], await rest.Content.ReadAsByteArrayAsync());

            // A stale validator: the whole archive again, not a splice.
            using var stale = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            stale.Headers.Range = new RangeHeaderValue(1000, null);
            stale.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue($"\"{Guid.NewGuid():N}\""));
            using var whole = await client.SendAsync(stale);

            Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
            Assert.Equal(_archive, await whole.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task ANewPasswordInTheMiddleOfADownload_TheNextRangeOfTheOldArchiveIs404()
        {
            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var first = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            first.Headers.Range = new RangeHeaderValue(0, 999);
            using var part = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);

            var reencrypted = _world.Store.Reencrypt(_file.FileId, LargeFileWorld.ArchiveBytes(seed: 2), passwordVersion: 2);

            using var resume = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            resume.Headers.Range = new RangeHeaderValue(1000, null);
            resume.Headers.IfRange = new RangeConditionHeaderValue(part.Headers.ETag);
            using var rest = await client.SendAsync(resume);

            Assert.Equal(HttpStatusCode.NotFound, rest.StatusCode);

            using var fresh = await client.GetAsync(Url(reencrypted));
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            Assert.Equal("2", Header(fresh, LargeFileDownloadResult.PasswordVersionHeader));
        }

        [Fact]
        public void AnArchiveReplacedAfterTheDecision_IsNotServed()
        {
            // The race the re-read after the open closes: the decision saw the old archive, the store replaced it before
            // the open. The answer is the plain 404, not the new archive's bytes under the old ETag.
            var service = _world.CreateService();
            var decision = service.Decide(_file.FileId, _file.ArchiveId.Value, LargeFileCaller.Web(_world.ManagerA, null), record: false);
            Assert.Equal(LargeFileDownloadOutcome.Ok, decision.Outcome);

            _world.Store.Reencrypt(_file.FileId, LargeFileWorld.ArchiveBytes(seed: 3), passwordVersion: 2);

            Assert.Null(service.TryOpenArchive(decision.Download));
        }


        [Fact]
        public void AnArchiveTheStoreCannotOpen_IsNotServed_WhateverTheStoreThrows()
        {
            var service = _world.CreateService();
            var decision = service.Decide(_file.FileId, _file.ArchiveId.Value, LargeFileCaller.Web(_world.ManagerA, null), record: false);
            Assert.Equal(LargeFileDownloadOutcome.Ok, decision.Outcome);

            foreach (var fault in new Exception[] { new UnauthorizedAccessException("access denied"), new InvalidOperationException("store fault"), new IOException("io") })
            {
                _world.Store.OpenFault = _ => fault;

                Assert.Null(service.TryOpenArchive(decision.Download));
            }

            _world.Store.OpenFault = _ => new OperationCanceledException();
            Assert.Throws<OperationCanceledException>(() => service.TryOpenArchive(decision.Download));
        }

        [Fact]
        public async Task AnArchiveTheStoreIsDeniedAccessTo_AnswersTheSame404AsAnUnknownId()
        {
            var manager = _world.AddToken(_world.ManagerA);
            _world.Store.OpenFault = id => id == _file.FileId ? new UnauthorizedAccessException("access denied") : null;

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, manager);

            var denied = await Snapshot(client.GetAsync(Url(_file)));
            var unknown = await Snapshot(client.GetAsync(LargeFileDownloadService.ApiDownloadPath(Guid.NewGuid(), _file.ArchiveId.Value)));

            Assert.Equal(404, denied.Status);
            Assert.Equal(unknown, denied);
        }

        [Fact]
        public async Task TheMarkerFromTheTrustedProxy_HandsTheFileToCaddy()
        {
            _world.TrustProxies();

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            LargeFileWorld.FromPeer(request, LargeFileWorld.ProxyPeer);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("instruction for the bundled Caddy", await response.Content.ReadAsStringAsync());
            Assert.Equal($"{_world.ProductA.Id:N}/{_file.ArchiveId:N}.zip", Header(response, LargeFileDownloadResult.ArchiveHeader));
            Assert.NotNull(Header(response, LargeFileDownloadResult.ReprDigestHeader));
            Assert.Equal("1", Header(response, LargeFileDownloadResult.PasswordVersionHeader));
            Assert.NotNull(response.Content.Headers.ContentDisposition);

            // The instruction comes after the record.
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Once);
        }

        // The hand-over path is the store's own path of the archive under the folder Caddy mounts, accepted only as the two
        // GUIDs: a store that places the archive elsewhere is never handed over, nor served, and answers the same 404.
        [Theory]
        [InlineData("elsewhere")]
        [InlineData("outside")]
        [InlineData("unreadable")]
        public async Task AnArchiveTheStoreDoesNotPlaceAtTheGuidPath_IsNeitherHandedOverNorServed(string where)
        {
            _world.TrustProxies();
            _world.Store.ArchivePathOverride = file => where switch
            {
                "elsewhere" => Path.Combine(_world.Store.Root, "other", $"{file.ArchiveId:N}.zip"),
                "outside" => Path.Combine(Path.GetTempPath(), $"{file.ProductId:N}", $"{file.ArchiveId:N}.zip"),
                _ => throw new IOException("the store cannot tell"),
            };

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            LargeFileWorld.FromPeer(request, LargeFileWorld.ProxyPeer);
            var handedOver = await Snapshot(client.SendAsync(request));
            var served = await Snapshot(client.GetAsync(Url(_file)));
            var unknown = await Snapshot(client.GetAsync(LargeFileDownloadService.ApiDownloadPath(Guid.NewGuid(), _file.ArchiveId.Value)));

            Assert.Equal(404, handedOver.Status);
            Assert.Equal(unknown, handedOver);
            Assert.Equal(unknown, served);
            Assert.DoesNotContain(LargeFileDownloadResult.ArchiveHeader, handedOver.Headers);
            _world.Log.Verify(l => l.Record(It.IsAny<DownloadCaller>(), It.IsAny<DownloadTarget>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Theory]
        [InlineData(false, LargeFileWorld.ProxyPeer)] // the marker without a configured trusted proxy
        [InlineData(true, null)]                      // trusted proxies configured, but no connection peer is known
        [InlineData(true, "198.51.100.20")]           // a direct client
        [InlineData(true, "10.0.0.2")]                // a neighbour of the trusted proxy
        [InlineData(true, "::ffff:198.51.100.20")]    // a direct client over IPv6
        public async Task TheMarkerWithoutTheTrustedProxy_GetsTheFileItself(bool trustProxies, string peer)
        {
            if (trustProxies)
                _world.TrustProxies();

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            if (peer is not null)
                LargeFileWorld.FromPeer(request, peer);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_archive, await response.Content.ReadAsByteArrayAsync());
            Assert.Null(Header(response, LargeFileDownloadResult.ArchiveHeader));
        }

        [Fact]
        public async Task TheMarkerFromTheTrustedProxyOverIPv4MappedIPv6_HandsTheFileToCaddy()
        {
            _world.TrustProxies();

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            LargeFileWorld.FromPeer(request, $"::ffff:{LargeFileWorld.ProxyPeer}");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal($"{_world.ProductA.Id:N}/{_file.ArchiveId:N}.zip", Header(response, LargeFileDownloadResult.ArchiveHeader));
        }

        [Fact]
        public async Task ADirectClientSendingTheForwardedHeaders_GetsTheFileItself()
        {
            // X-Original-For is what the forwarded-headers middleware sets on a trusted hop, but it leaves a copy the
            // client sent in place: only the connection's own peer tells a trusted proxy.
            _world.TrustProxies();

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(_file));
            request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
            request.Headers.Add(ForwardedHeadersDefaults.XOriginalForHeaderName, $"{LargeFileWorld.ProxyPeer}:5000");
            LargeFileWorld.FromPeer(request, "198.51.100.20");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_archive, await response.Content.ReadAsByteArrayAsync());
            Assert.Null(Header(response, LargeFileDownloadResult.ArchiveHeader));
        }

        [Fact]
        public async Task NoErrorAnswer_EverCarriesTheInstruction()
        {
            _world.TrustProxies();
            var verified = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified);

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            async Task<HttpResponseMessage> ViaCaddy(string url)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add(LargeFileDownloadResult.ServeArchivesHeader, "1");
                LargeFileWorld.FromPeer(request, LargeFileWorld.ProxyPeer);
                return await client.SendAsync(request);
            }

            using var notFound = await ViaCaddy(LargeFileDownloadService.ApiDownloadPath(_file.FileId, Guid.NewGuid()));
            using var notReady = await ViaCaddy(LargeFileDownloadService.ApiDownloadPath(verified.FileId, Guid.NewGuid()));

            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, notReady.StatusCode);
            Assert.Null(Header(notFound, LargeFileDownloadResult.ArchiveHeader));
            Assert.Null(Header(notReady, LargeFileDownloadResult.ArchiveHeader));
        }


        private static string Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
                ? string.Join(",", values)
                : null;

        private static async Task<(int Status, string Body, string Headers)> Snapshot(Task<HttpResponseMessage> request)
        {
            using var response = await request;

            return await LargeFileWorld.Snapshot(response);
        }
    }
}
