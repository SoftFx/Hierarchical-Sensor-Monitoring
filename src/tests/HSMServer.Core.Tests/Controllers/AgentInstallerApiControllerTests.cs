using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Agent;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AgentInstaller;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The agent-installer surface (#1788): the PM admission with the
    // read-only-token allowance (the evaluator's read split + the owner-side
    // role check), the build's caller-actionable errors (400 no-key, 503
    // not-staged), the one-time link envelope (url/fileName/size/sha256 of
    // the EXACT bytes) and the single-use redemption.
    public class AgentInstallerApiControllerTests : IDisposable
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IWebHostEnvironment> _environment = new();
        private readonly AgentInstallerDownloadLinks _links = new();

        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly User _owner = new("the-manager") { IsAdmin = true };

        private readonly string _webRoot = Directory.CreateTempSubdirectory("hsm-installer-test-").FullName;


        public AgentInstallerApiControllerTests()
        {
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[_ownerId]).Returns(_owner);
        }

        public void Dispose()
        {
            _links.Dispose();
            Directory.Delete(_webRoot, recursive: true);
        }


        private ClaimsPrincipal Principal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, _ownerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private (AgentInstallerApiController Controller, Guid ProductId) CreateController(
            string webRoot = null, User owner = null, bool stageExe = true)
        {
            var product = new ProductModel("Garage server");
            var key = AccessKeyModel.BuildDefault(product);
            product.AccessKeys.TryAdd(key.Id, key);

            _cache.Setup(c => c.TryGetProduct(product.Id, out It.Ref<ProductModel>.IsAny))
                .Callback(new TryProductCallback((Guid _, out ProductModel p) => p = product))
                .Returns(true);

            if (stageExe)
            {
                Directory.CreateDirectory(Path.Combine(_webRoot, "agent"));
                File.WriteAllBytes(Path.Combine(_webRoot, "agent", AgentInstallerBundle.ExeName),
                    new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            }

            if (owner is not null)
                _users.Setup(u => u[_ownerId]).Returns(owner);

            _environment.Setup(e => e.WebRootPath).Returns(webRoot ?? _webRoot);

            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Agent).Returns(new AgentConfig { ExternalConnectionUrl = "https://hsm.example.com" });
            config.Setup(c => c.Kestrel).Returns(new KestrelConfig { TrustedProxies = [] });
            config.Setup(c => c.ServerCertificate).Returns(new ServerCertificateConfig());

            var installer = new AgentInstallerAdministrationService(_cache.Object, _authorization.Object,
                _users.Object, config.Object, _environment.Object, _links);

            var context = new DefaultHttpContext { User = Principal() };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("hsm.example.com");

            return (new AgentInstallerApiController(installer)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            }, product.Id);
        }

        private delegate void TryProductCallback(Guid id, out ProductModel product);

        private static ManagementApiErrorDto ErrorBodyOf(IActionResult result) =>
            Assert.IsType<ObjectResult>(result).Value as ManagementApiErrorDto;


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(AgentInstallerApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/products/{productId:guid}", type.GetCustomAttribute<RouteAttribute>()?.Template);
        }


        [Fact]
        public void GetInstaller_StreamsTheWindowsZip()
        {
            var (controller, productId) = CreateController();

            var result = Assert.IsType<FileContentResult>(controller.GetInstaller(productId, "windows"));

            Assert.Equal("application/zip", result.ContentType);
            Assert.Equal("hsm-agent-Garage_server.zip", result.FileDownloadName);
            Assert.True(result.FileContents.Length > 0);
        }


        [Fact]
        public void GetInstaller_StreamsTheLinuxBundle()
        {
            var (controller, productId) = CreateController();

            Directory.CreateDirectory(Path.Combine(_webRoot, "probe"));
            File.WriteAllBytes(Path.Combine(_webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"),
                new byte[] { 0x21, 0x3C });

            var result = Assert.IsType<FileContentResult>(controller.GetInstaller(productId, "linux"));

            Assert.Equal("application/gzip", result.ContentType);
            Assert.Equal("hsm-linux-probe-Garage_server.tar.gz", result.FileDownloadName);
        }


        [Fact]
        public void Controller_ResponsesAreNeverCacheable()
        {
            // The bundle carries a bearer key — the never-cacheable invariant
            // of the agent-download feature, on the API surface too.
            var cache = typeof(AgentInstallerApiController)
                .GetCustomAttribute<Microsoft.AspNetCore.Mvc.ResponseCacheAttribute>();

            Assert.NotNull(cache);
            Assert.True(cache.NoStore);
        }


        [Fact]
        public void GetInstaller_NoUsableKey_IsA400NamingTheFix()
        {
            // A product with no key collection entry: BuildDefault is skipped,
            // so admission passes but the build cannot embed a key.
            var (controller, productId) = CreateController();

            _cache.Setup(c => c.TryGetProduct(productId, out It.Ref<ProductModel>.IsAny))
                .Callback(new TryProductCallback((Guid _, out ProductModel p) =>
                {
                    p = new ProductModel("Garage server");

                    // No key: the fresh model's collection is empty.
                }))
                .Returns(true);

            var result = controller.GetInstaller(productId, "windows");

            var body = Assert.IsType<ObjectResult>(result);

            Assert.Equal(400, body.StatusCode);
            Assert.Contains(AgentInstallerAdministrationService.NoKeyMessage,
                Assert.IsType<string[]>(Assert.IsType<ManagementApiErrorDto>(body.Value).Details["accessKey"]));
        }


        [Fact]
        public void GetInstaller_NotStaged_IsThe503CapacityAbort()
        {
            var (controller, productId) = CreateController(stageExe: false);

            var result = controller.GetInstaller(productId, "windows");

            var body = Assert.IsType<ObjectResult>(result);

            Assert.Equal(503, body.StatusCode);
            Assert.Equal(ManagementApiErrors.ServiceUnavailableCode,
                Assert.IsType<ManagementApiErrorDto>(body.Value).Error);
            Assert.Equal(AgentInstallerBundle.NotStagedMessage,
                Assert.IsType<ManagementApiErrorDto>(body.Value).Message);
        }


        [Fact]
        public void GetInstaller_UnknownOrInvisibleProduct_IsTheSame404()
        {
            var (controller, _) = CreateController();

            var unknown = controller.GetInstaller(Guid.NewGuid(), "windows");

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var (_, productId) = CreateController(stageExe: false);
            var invisible = controller.GetInstaller(productId, "windows");

            Assert.Equal(404, Assert.IsType<ObjectResult>(unknown).StatusCode);
            Assert.Equal(404, Assert.IsType<ObjectResult>(invisible).StatusCode);
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(invisible));
        }


        [Fact]
        public void GetInstaller_OwnerWithoutThePmRole_Is403()
        {
            // Sight passes (any product role), but the owner is neither admin
            // nor ProductManager — the bundle's credential stays PM-only
            // (decision #5), while a READ-ONLY token of a PM still passes
            // (the composition never consults the token's flag).
            var (controller, productId) = CreateController(owner: new User("viewer"));

            var result = controller.GetInstaller(productId, "windows");

            var body = Assert.IsType<ObjectResult>(result);

            Assert.Equal(403, body.StatusCode);
            Assert.Equal(ManagementApiErrors.ForbiddenCode,
                Assert.IsType<ManagementApiErrorDto>(body.Value).Error);
        }


        [Fact]
        public void GetInstaller_BadOs_IsA400()
        {
            var (controller, productId) = CreateController();

            var result = controller.GetInstaller(productId, "mac");

            var body = Assert.IsType<ObjectResult>(result);

            Assert.Equal(400, body.StatusCode);
            Assert.Contains("The os must be 'windows' or 'linux' (got 'mac').",
                Assert.IsType<string[]>(Assert.IsType<ManagementApiErrorDto>(body.Value).Details["os"]));
        }


        [Fact]
        public void IssueInstallerLink_ReturnsTheEnvelope_AndTheUrlRedeemsExactlyOnce()
        {
            var (controller, productId) = CreateController();

            var link = Assert.IsType<AgentInstallerLinkDto>(
                Assert.IsType<OkObjectResult>(controller.IssueInstallerLink(productId, "windows")).Value);

            Assert.StartsWith("https://hsm.example.com/api/agent/installer-download?token=", link.Url);
            Assert.EndsWith(".zip", link.FileName);
            Assert.True(link.Size > 0);
            Assert.Equal(64, link.Sha256.Length);
            Assert.InRange((link.ExpiresAt - DateTime.UtcNow).TotalMinutes, 9, 10.01);

            // The redemption serves EXACTLY the advertised bytes, once.
            var token = Guid.Parse(link.Url.Split("token=")[1]);
            var download = new AgentInstallerDownloadController(_links);

            var served = Assert.IsType<FileContentResult>(download.Download(token));

            Assert.Equal(link.FileName, served.FileDownloadName);
            Assert.Equal(link.Size, served.FileContents.LongLength);
            Assert.Equal(link.Sha256, Sha256Hex(served.FileContents));

            Assert.Equal(404, Assert.IsType<NotFoundObjectResult>(download.Download(token)).StatusCode);
        }


        [Fact]
        public void Download_UnknownOrEmptyToken_Is404()
        {
            var download = new AgentInstallerDownloadController(_links);

            Assert.Equal(404, Assert.IsType<NotFoundObjectResult>(download.Download(Guid.NewGuid())).StatusCode);
            Assert.Equal(404, Assert.IsType<NotFoundObjectResult>(download.Download(Guid.Empty)).StatusCode);
        }


        [Fact]
        public void Links_AreSingleUse_AndDieAtExpiry()
        {
            var clock = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            _links.UtcNow = () => clock;

            var (token, expiresAt) = _links.Issue(new AgentInstallerDownloadLinks.Link(
                "b.zip", "application/zip", [1, 2, 3]));

            // Single use: the first redemption wins, the second is null.
            Assert.NotNull(_links.TryRedeem(token));
            Assert.Null(_links.TryRedeem(token));

            // Expiry: a fresh link dies with the clock.
            var (second, _) = _links.Issue(new AgentInstallerDownloadLinks.Link(
                "b.zip", "application/zip", [1, 2, 3]));

            clock += AgentInstallerDownloadLinks.LinkLifetime + TimeSpan.FromSeconds(1);

            Assert.Null(_links.TryRedeem(second));
            Assert.True(expiresAt <= clock);
        }


        private static string Sha256Hex(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes);

            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
