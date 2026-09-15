using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Mcp;
using HSMServer.Middleware;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The wire-level smoke test of the /mcp endpoint (#1392 review, round 3):
    // a minimal in-memory host assembles the REAL pieces — MapMcp with the
    // RequireAuthorization policy, McpSitePortOnlyMiddleware, the real
    // HsmApiToken scheme (over mocked token/user managers) and AddHsmMcpServer's
    // registrations — and a real SDK McpClient drives initialize → tools/list →
    // tools/call through the full ASP.NET pipeline. No Kestrel, LevelDB or the
    // dual fixed-port listeners; that is exactly what made a WebApplicationFactory
    // test infeasible for the whole server.
    //
    // This closes the three load-bearing integration assumptions at once:
    //   1. the ambient principal flows (IHttpContextAccessor) — the tools/call
    //      body executes inline in its POST, so the stubbed cache is reached and
    //      the answer is data, not the McpToolContext backstop error;
    //   2. the SDK constructs tool instances in the REQUEST scope — the host runs
    //      in Development (ValidateScopes + ValidateOnBuild), so a root-scope
    //      resolution of the scoped SensorTreeReadService fails loudly;
    //   3. the guard reads the metadata MapMcp actually emits — a mismatch
    //      fail-closes the endpoint to a uniform 404 and the client cannot even
    //      initialize.
    public class HsmMcpWireTests
    {
        private const int SitePort = 44333;

        private static readonly string[] NineTools =
        [
            "find_sensors",
            "get_alert_schedule",
            "get_alert_template",
            "get_node",
            "get_sensor",
            "get_sensor_history",
            "list_alert_schedules",
            "list_alert_templates",
            "list_products",
        ];


        private delegate void TryAuthenticateCallback(string presented, out ApiTokenInfo token);


        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IApiTokenSecurityEventSink> _securityEvents = new();

        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly string _credential = ApiTokenMaterial.FormatToken(new string('A', 22), new string('B', 43));

        private readonly Core.Model.ProductModel _productA =
            new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });


        public HsmMcpWireTests()
        {
            _cache.Setup(c => c.GetProducts()).Returns(new List<Core.Model.ProductModel> { _productA });
            _cache.Setup(c => c.GetSensors()).Returns(new List<Core.Model.BaseSensorModel>());
            _cache.Setup(c => c.GetAlertTemplateModels()).Returns(new List<Core.Model.AlertTemplateModel>());
            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>());
            _cache.Setup(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>()))
                .Returns(new List<Core.Model.BaseSensorModel>());

            _schedules.Setup(s => s.GetAllSchedules())
                .Returns(new List<Core.Model.Policies.AlertSchedule>());

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.IsVisible(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(true);

            // The single authentication decision the real handler delegates to
            // the manager: a credential of the right shape authenticates as the
            // owner, everything else fails closed (the loose mock's default).
            _tokens.Setup(m => m.TryAuthenticate(_credential, out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new TryAuthenticateCallback((string _, out ApiTokenInfo token) =>
                    token = new ApiTokenInfo { OwnerUserId = _ownerId }))
                .Returns(true);

            _users.Setup(u => u[_ownerId]).Returns(new HSMServer.Model.Authentication.User(EntitiesFactory.BuildUser()));
        }


        [Fact]
        public async Task Wire_InitializeListsTools_AndCallsListProductsWithTheAmbientPrincipal()
        {
            using var host = BuildHost();
            host.Start();

            await using var client = await ConnectAsync(host);

            var tools = await client.ListToolsAsync();

            Assert.Equal(
                NineTools.Order(StringComparer.Ordinal),
                tools.Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal));

            var result = await client.CallToolAsync("list_products", new Dictionary<string, object> { ["limit"] = 1 });

            // The wire envelope omits isError on success (null), sets it only on
            // a tool error — the backstop McpException would have set it true.
            Assert.NotEqual(true, result.IsError);

            // The answer came through the REAL pipeline — the stubbed cache fed
            // the tool, meaning the principal flowed and the request-scope
            // resolution worked; the backstop error would have been an isError.
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains("alpha", text);
            Assert.Contains("totalFound", text);

            _cache.Verify(c => c.GetProducts(), Times.AtLeastOnce);
        }


        [Fact]
        public async Task Wire_WithoutCredential_TheTransportIsRejectedBeforeAnyTool()
        {
            using var host = BuildHost();
            host.Start();

            // No Authorization header: the RequireAuthorization policy challenges
            // with the plain 401 before the SDK handler, so even initialize fails.
            var client = host.GetTestClient();
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(client.BaseAddress, HsmMcp.EndpointPath),
                TransportMode = HttpTransportMode.StreamableHttp,
            }, client, NullLoggerFactory.Instance, ownsHttpClient: false);

            await Assert.ThrowsAnyAsync<Exception>(() => McpClient.CreateAsync(transport));

            _cache.Verify(c => c.GetProducts(), Times.Never);
        }


        [Fact]
        public async Task Wire_ToolsCallOnTheSensorPort_IsUniform404()
        {
            // The SitePort-only rule through the REAL endpoint metadata: on the
            // other listener the guard short-circuits before authentication, so
            // a perfectly valid credential gets the uniform 404 — the endpoint
            // is dead there, confirming nothing.
            using var host = BuildHost(localPort: 44330);
            host.Start();

            var client = host.GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _credential);

            var response = await client.GetAsync(HsmMcp.EndpointPath);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("not found", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }


        private IHost BuildHost(int localPort = SitePort)
        {
            return Host.CreateDefaultBuilder()
                // Development turns on ValidateScopes + ValidateOnBuild: a
                // root-scope resolution of the scoped SensorTreeReadService (or
                // any missing registration) fails the host instead of silently
                // handing out a wrong-lifetime instance.
                .UseEnvironment(Environments.Development)
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddRouting();

                        services.AddAuthentication()
                            .AddHsmApiTokenScheme();
                        services.AddHsmApiTokenAuthorization();

                        services.AddHsmMcpServer();
                        services.AddScoped<SensorTreeReadService>();

                        services.AddSingleton(_cache.Object);
                        services.AddSingleton(_authorization.Object);
                        services.AddSingleton(_schedules.Object);
                        services.AddSingleton(_tokens.Object);
                        services.AddSingleton(_users.Object);
                        services.AddSingleton(_securityEvents.Object);
                        services.AddSingleton(new ApiTokensConfig { Enabled = true });
                        services.AddSingleton<ApiTokenInvalidAttemptLimiter>();
                        services.AddSingleton(new HsmListenerBindings(44333, 44330));
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();

                        // The in-memory server has no real listener, so the
                        // SitePort guard's registry is driven through the
                        // connection feature — the same seam the guard tests
                        // use, here through the real pipeline.
                        app.Use((context, next) =>
                        {
                            context.Features.Set<IHttpConnectionFeature>(new FixedPortConnectionFeature(localPort));
                            return next(context);
                        });

                        app.UseMiddleware<McpSitePortOnlyMiddleware>();
                        app.UseAuthentication();
                        app.UseAuthorization();

                        app.UseEndpoints(endpoints => endpoints
                            .MapMcp(HsmMcp.EndpointPath)
                            .RequireAuthorization(HsmApiTokenDefaults.ManagementPolicy));
                    });
                })
                .Build();
        }


        private async Task<McpClient> ConnectAsync(IHost host)
        {
            var httpClient = host.GetTestClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _credential);

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress, HsmMcp.EndpointPath),
                TransportMode = HttpTransportMode.StreamableHttp,
            }, httpClient, NullLoggerFactory.Instance, ownsHttpClient: false);

            return await McpClient.CreateAsync(transport);
        }


        private sealed class FixedPortConnectionFeature : IHttpConnectionFeature
        {
            public FixedPortConnectionFeature(int localPort) => LocalPort = localPort;

            public string ConnectionId { get; set; }
            public IPAddress LocalIpAddress { get; set; }
            public int LocalPort { get; set; }
            public IPAddress RemoteIpAddress { get; set; }
            public int RemotePort { get; set; }
        }
    }
}
