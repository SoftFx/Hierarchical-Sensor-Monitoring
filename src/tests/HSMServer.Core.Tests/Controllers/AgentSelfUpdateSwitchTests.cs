using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Model.Agent;
using HSMServer.SensorApi;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1812: the server has no switch for agent self-update (the client turns it off with
    // update.enabled). The version manifest keeps "updateEnabled", always true, because deployed
    // agents require it; the update-available directive depends only on the staged version; and a
    // settings file that still carries the retired Agent.AutoUpdateEnabled key loads as before.
    public sealed class AgentSelfUpdateSwitchTests : IDisposable
    {
        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"hsm-agent-switch-{Guid.NewGuid():N}");

        public AgentSelfUpdateSwitchTests()
        {
            Directory.CreateDirectory(Path.Combine(_webRoot, "agent"));
            File.WriteAllBytes(Path.Combine(_webRoot, "agent", AgentInstallerBundle.ExeName), [1, 2, 3]);
            File.WriteAllText(Path.Combine(_webRoot, "agent", "version.txt"), "9.9.9");
        }

        public void Dispose() => Directory.Delete(_webRoot, recursive: true);


        [Fact]
        public void VersionManifest_AlwaysSaysUpdateEnabled()
        {
            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Agent).Returns(new AgentConfig());

            var controller = new AgentController(new Mock<IUserManager>().Object, new Mock<ITreeValuesCache>().Object, config.Object, Environment())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var ok = Assert.IsType<OkObjectResult>(controller.GetVersion());
            using var manifest = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));

            Assert.Equal("9.9.9", manifest.RootElement.GetProperty("version").GetString());
            Assert.True(manifest.RootElement.GetProperty("updateEnabled").GetBoolean());
        }

        [Fact]
        public void UpdateAvailableDirective_NeedsOnlyANewerStagedAgent()
        {
            var directives = new AgentDirectives(Environment(), TimeProvider.System);

            Assert.Equal(["update-available:9.9.9"], directives.For("0.6.0", product: null));
        }

        [Theory]
        [InlineData("9.9.9")]
        [InlineData("10.0.0")]
        public void UpdateAvailableDirective_NotSentToAnAgentThatIsNotOlder(string agentVersion)
        {
            var directives = new AgentDirectives(Environment(), TimeProvider.System);

            Assert.Empty(directives.For(agentVersion, product: null));
        }

        [Theory]
        [InlineData("true")]
        [InlineData("false")]
        public void SettingsWithTheRetiredAutoUpdateEnabledKey_StillLoad(string value)
        {
            var json = $$"""{ "Agent": { "ExternalConnectionUrl": "https://hsm.example.com", "AutoUpdateEnabled": {{value}}, "EnableTopCpuProcesses": true } }""";
            var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

            // The same call ServerConfig.Register makes for the section.
            var agent = configuration.GetSection(nameof(IServerConfig.Agent)).Get<AgentConfig>();

            Assert.NotNull(agent);
            Assert.Equal("https://hsm.example.com", agent.ExternalConnectionUrl);
            Assert.True(agent.EnableTopCpuProcesses);
        }


        private IWebHostEnvironment Environment()
        {
            var environment = new Mock<IWebHostEnvironment>();
            environment.Setup(e => e.WebRootPath).Returns(_webRoot);
            return environment.Object;
        }
    }
}
