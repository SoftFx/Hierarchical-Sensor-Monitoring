using System;
using System.IO;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.Agent;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using HSMServer.Model.ManagementApi.AgentInstaller;
using HSMServer.Mcp;

namespace HSMServer.Core.Tests.Mcp
{
    // The get_agent_installer tool (#1788): a thin rendering of the same
    // AgentInstallerAdministrationService the REST controller runs on — the
    // link envelope (url/fileName/size/sha256) and the failure classes
    // rendered as tool errors with the REST arm's strings.
    public class AgentInstallerMcpToolsTests : IDisposable
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly AgentInstallerDownloadLinks _links = new();

        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly string _webRoot = Directory.CreateTempSubdirectory("hsm-installer-mcp-").FullName;


        public AgentInstallerMcpToolsTests()
        {
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[_ownerId]).Returns(new User("the-manager") { IsAdmin = true });
        }

        public void Dispose()
        {
            _links.Dispose();
            Directory.Delete(_webRoot, recursive: true);
        }


        private AgentInstallerMcpTools CreateTools(ProductModel product, bool stageExe = true)
        {
            var key = AccessKeyModel.BuildDefault(product);
            product.AccessKeys.TryAdd(key.Id, key);

            _cache.Setup(c => c.TryGetProduct(It.Is<Guid>(id => id == product.Id),
                    out It.Ref<ProductModel>.IsAny))
                .Callback(new TryProductCallback((Guid _, out ProductModel p) => p = product))
                .Returns(true);

            if (stageExe)
            {
                Directory.CreateDirectory(Path.Combine(_webRoot, "agent"));
                File.WriteAllBytes(Path.Combine(_webRoot, "agent", AgentInstallerBundle.ExeName),
                    new byte[] { 0x4D, 0x5A });
            }

            var environment = new Mock<IWebHostEnvironment>();
            environment.Setup(e => e.WebRootPath).Returns(_webRoot);

            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Agent).Returns(new AgentConfig());
            config.Setup(c => c.Kestrel).Returns(new KestrelConfig { TrustedProxies = [] });
            config.Setup(c => c.ServerCertificate).Returns(new ServerCertificateConfig());

            var installer = new AgentInstallerAdministrationService(_cache.Object, _authorization.Object,
                _users.Object, config.Object, environment.Object, _links);

            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(HsmApiTokenClaims.OwnerUserId, _ownerId.ToString()),
                        new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
                    ], HsmApiTokenDefaults.AuthenticationScheme)),
                },
            };
            http.HttpContext.Request.Scheme = "https";
            http.HttpContext.Request.Host = new HostString("api.example.com", 8443);

            return new AgentInstallerMcpTools(installer, http);
        }

        private delegate void TryProductCallback(Guid id, out ProductModel product);


        [Fact]
        public void GetAgentInstaller_ReturnsTheOneTimeLinkEnvelope()
        {
            var product = new ProductModel("Garage server");
            var tools = CreateTools(product);

            var link = tools.GetAgentInstaller(product.Id, "windows");

            // Host-absolute from the tool call's OWN request — port included.
            Assert.StartsWith("https://api.example.com:8443/api/agent/installer-download?token=", link.Url);
            Assert.EndsWith(".zip", link.FileName);
            Assert.Equal(64, link.Sha256.Length);
        }


        [Fact]
        public void GetAgentInstaller_OwnerWithoutThePmRole_IsToolError()
        {
            _users.Setup(u => u[_ownerId]).Returns(new User("viewer"));

            var product = new ProductModel("Garage server");
            var tools = CreateTools(product);

            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => tools.GetAgentInstaller(product.Id, "windows"));

            Assert.Equal("The token's owner must hold the ProductManager role at this product.", error.Message);
        }
    }
}
