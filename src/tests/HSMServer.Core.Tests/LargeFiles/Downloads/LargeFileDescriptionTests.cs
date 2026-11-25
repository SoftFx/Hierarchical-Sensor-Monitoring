using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.LargeFiles;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The description of a large file (#1885): GET /api/v1/sensors/{sensorId}/large-files/{fileId} and its MCP twin
    // get_large_file. Same visibility as the download; the dump password value is never part of any answer.
    public sealed class LargeFileDescriptionTests : IDisposable
    {
        private readonly LargeFileWorld _world = new();
        private readonly byte[] _archive = LargeFileWorld.ArchiveBytes();
        private readonly LargeArchiveFile _file;


        public LargeFileDescriptionTests()
        {
            _file = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, _archive, passwordVersion: 3);
        }

        public void Dispose() => _world.Dispose();


        private static string Url(Guid sensorId, Guid fileId) => $"/api/v1/sensors/{sensorId}/large-files/{fileId}";


        [Fact]
        public async Task APackedFile_IsDescribedWithItsArchive_AndNoPassword()
        {
            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ViewerA, readOnly: true));

            using var response = await client.GetAsync(Url(_world.SensorA.Id, _file.FileId));
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(LargeFileWorld.Password, text);

            using var body = JsonDocument.Parse(text);
            var root = body.RootElement;
            Assert.Equal(_file.FileId, root.GetProperty("fileId").GetGuid());
            Assert.Equal(_world.SensorA.Id, root.GetProperty("sensorId").GetGuid());
            Assert.Equal("core", root.GetProperty("name").GetString());
            Assert.Equal("dmp", root.GetProperty("extension").GetString());
            Assert.Equal("packed", root.GetProperty("state").GetString());
            Assert.Equal("queued", root.GetProperty("buildState").GetString());

            var archive = root.GetProperty("archive");
            Assert.Equal(_file.ArchiveId.Value, archive.GetProperty("id").GetGuid());
            Assert.Equal(_archive.Length, archive.GetProperty("size").GetInt64());
            Assert.Equal(Convert.ToHexString(_file.ArchiveSha256).ToLowerInvariant(), archive.GetProperty("sha256").GetString());
            Assert.Equal(3, archive.GetProperty("passwordVersion").GetInt32());
            Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), archive.GetProperty("passwordSetUtc").GetDateTime());

            // The link works with the same token.
            var link = new Uri(archive.GetProperty("downloadUrl").GetString());
            Assert.Equal(LargeFileDownloadService.ApiDownloadPath(_file.FileId, _file.ArchiveId.Value), link.AbsolutePath);

            using var download = await client.GetAsync(link.PathAndQuery);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(_archive, await download.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task AVerifiedFile_IsDescribedWithTheReason_AndNoArchive()
        {
            var verified = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified);
            _world.Passwords.Setup(p => p.HasPassword(_world.ProductA.Id)).Returns(false);
            _world.Builder.Setup(b => b.GetStatus(verified.FileId)).Returns(new ArchiveBuildStatus(ArchiveBuildState.WaitingForPassword, 0, null, null));

            using var host = _world.BuildHost();
            using var client = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            using var response = await client.GetAsync(Url(_world.SensorA.Id, verified.FileId));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("verified", body.RootElement.GetProperty("state").GetString());
            Assert.Equal("waitingForPassword", body.RootElement.GetProperty("buildState").GetString());
            Assert.Equal(LargeFileDownloadService.WaitingForPasswordReason, body.RootElement.GetProperty("notReadyReason").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("archive").ValueKind);
        }

        [Fact]
        public async Task UnknownInvisibleAndOtherSensorFiles_AnswerTheSame404()
        {
            var uploading = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Uploading);

            using var host = _world.BuildHost();
            using var stranger = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerB));
            using var manager = LargeFileWorld.ApiClient(host, _world.AddToken(_world.ManagerA));

            var answers = new[]
            {
                await Snapshot(stranger, Url(_world.SensorA.Id, _file.FileId)),
                await Snapshot(manager, Url(_world.SensorA.Id, Guid.NewGuid())),
                await Snapshot(manager, Url(_world.SensorB.Id, _file.FileId)),
                await Snapshot(manager, Url(Guid.NewGuid(), _file.FileId)),
                await Snapshot(manager, Url(_world.SensorA.Id, uploading.FileId)),
            };

            Assert.Equal(404, answers[0].Status);
            Assert.Contains(ManagementApiErrors.NotFoundMessage, answers[0].Body);
            Assert.All(answers, answer => Assert.Equal(answers[0], answer));
        }


        [Fact]
        public void TheMcpTool_ReturnsTheSameDescription_AndItsErrorIsTheAreaNotFound()
        {
            var credential = _world.AddToken(_world.ManagerA);
            var principal = TokenPrincipal(credential);

            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
            http.HttpContext.Request.Scheme = "https";
            http.HttpContext.Request.Host = new HostString("hsm.example", 443);

            var tool = new LargeFilesMcpTools(_world.CreateService(), http);

            var dto = tool.GetLargeFile(_world.SensorA.Id, _file.FileId);

            Assert.Equal("packed", dto.State);
            Assert.Equal($"https://hsm.example:443{LargeFileDownloadService.ApiDownloadPath(_file.FileId, _file.ArchiveId.Value)}", dto.Archive.DownloadUrl);
            Assert.DoesNotContain(LargeFileWorld.Password, JsonSerializer.Serialize(dto));

            var error = Assert.Throws<McpException>(() => tool.GetLargeFile(_world.SensorB.Id, _file.FileId));
            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }

        [Fact]
        public void TheMcpTool_IsReadOnlyAndIdempotent_AndSaysTheBytesNeverGoThroughMcp()
        {
            var method = typeof(LargeFilesMcpTools).GetMethod(nameof(LargeFilesMcpTools.GetLargeFile));
            var attribute = (McpServerToolAttribute)Attribute.GetCustomAttribute(method, typeof(McpServerToolAttribute));
            var description = ((System.ComponentModel.DescriptionAttribute)Attribute.GetCustomAttribute(method, typeof(System.ComponentModel.DescriptionAttribute))).Description;

            Assert.Equal("get_large_file", attribute.Name);
            Assert.True(attribute.ReadOnly);
            Assert.True(attribute.Idempotent);
            Assert.Contains("never go through MCP", description);
            Assert.Contains("same 'Authorization: Bearer' token", description);
            Assert.Contains("ask the operator", description);
        }


        private System.Security.Claims.ClaimsPrincipal TokenPrincipal(string credential) =>
            new(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(HSMServer.Authentication.HsmApiTokenClaims.OwnerUserId, _world.ManagerA.Id.ToString()),
                new System.Security.Claims.Claim(HSMServer.Authentication.HsmApiTokenClaims.TokenId, HSMServer.Authentication.ApiTokenMaterial.TokenIdOf(credential)),
            ], HSMServer.Authentication.HsmApiTokenDefaults.AuthenticationScheme));

        private static async Task<(int Status, string Body, string Headers)> Snapshot(System.Net.Http.HttpClient client, string url)
        {
            using var response = await client.GetAsync(url);

            return await LargeFileWorld.Snapshot(response);
        }
    }
}
