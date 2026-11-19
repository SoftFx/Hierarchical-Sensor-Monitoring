using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The wire-level pin of the 201 Location header (#1786 reopen): the
    // controller's CreatedAtAction crosses the REAL routing table, so the
    // unit-test pin of ControllerName alone let a wrong controller name
    // ("Nodes" instead of "NodesApi") slip through — it generated the web
    // UI's cookie-authenticated /Nodes/GetNode URL through the conventional
    // {controller}/{action} route instead of /api/v1/nodes/{id}, and the
    // reviewer's manual GET on it came back 401. This test assembles the
    // production routing surface in a minimal in-memory host — the app's
    // own controllers via MapControllers PLUS the {action}-bearing
    // conventional routes Program.cs registers, because the phantom URL is
    // only reachable through those — and then does what a client does: POST
    // /api/v1/products,
    // read the Location of the 201, and GET it, expecting the management
    // API's node endpoint to answer 200. No Kestrel or LevelDB (the same
    // trade HsmMcpWireTests made); only routing, auth and the two real
    // controllers are under test.
    public class ProductsApiCreateLocationWireTests
    {
        private delegate void OutProductCallback(Guid id, out Core.Model.ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IApiTokenSecurityEventSink> _securityEvents = new();

        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly string _credential = ApiTokenMaterial.FormatToken(new string('A', 22), new string('B', 43));

        // The created product lands here from the AddProductAsync callback,
        // which is what TryGetProduct then serves — the create and the
        // Location GET must agree on one product.
        private readonly List<Core.Model.ProductModel> _products = [];


        public ProductsApiCreateLocationWireTests()
        {
            _cache.Setup(c => c.GetProducts()).Returns(() => [.. _products]);

            _cache.Setup(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, Guid _, Core.TableOfChanges.InitiatorInfo _, CancellationToken _) =>
                {
                    var product = new Core.Model.ProductModel(
                        EntitiesFactory.BuildProductEntity(name: name) with { Id = Guid.NewGuid().ToString() });

                    _products.Add(product);

                    return product;
                });

            // The codebase's out-param pattern (NodesApiControllerTests): the
            // callback serves the product (null for an unknown id, which the
            // reader itself maps to NotFound), the return is constant.
            _cache.Setup(c => c.TryGetProduct(It.IsAny<Guid>(), out It.Ref<Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid id, out Core.Model.ProductModel product) =>
                    product = _products.Find(p => p.Id == id)))
                .Returns(true);

            // The single authentication decision the real handler delegates to
            // the manager (HsmMcpWireTests' pattern): the credential
            // authenticates as the owner, everything else fails closed.
            _tokens.Setup(m => m.TryAuthenticate(_credential, out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new TryAuthenticateCallback((string _, out ApiTokenInfo token) =>
                    token = new ApiTokenInfo { OwnerUserId = _ownerId }))
                .Returns(true);

            _users.Setup(u => u[_ownerId]).Returns(new HSMServer.Model.Authentication.User(EntitiesFactory.BuildUser()));
            _users.Setup(u => u.UpdateUser(It.IsAny<HSMServer.Model.Authentication.User>())).ReturnsAsync(true);

            _authorization.Setup(a => a.AuthorizeProductCreate(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.IsVisible(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }

        private delegate void TryAuthenticateCallback(string presented, out ApiTokenInfo token);


        [Fact]
        public async Task CreateProduct_LocationResolvesToTheNodeEndpoint_OverTheRealRoutingTable()
        {
            using var host = BuildHost();
            host.Start();

            using var client = host.GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _credential);

            using var created = await client.PostAsync("/api/v1/products",
                new StringContent(JsonSerializer.Serialize(new { name = "Alpha" }), Encoding.UTF8, "application/json"));

            Assert.True(created.IsSuccessStatusCode, $"create failed: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);

            // The body carries the created product; its id is the node id the
            // Location must address.
            using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var productId = body.RootElement.GetProperty("id").GetString();

            Assert.NotNull(created.Headers.Location);

            // The load-bearing assertion: the Location addresses the
            // management API's node endpoint, not a conventional-route URL of
            // the web UI (/Nodes/GetNode?id=... — a GET there answers 401 to a
            // bearer client, the reopened #1786's symptom).
            var location = created.Headers.Location;
            var locationPath = location.IsAbsoluteUri ? location.PathAndQuery : location.ToString();

            Assert.Equal($"/api/v1/nodes/{productId}", locationPath);

            // Resolve it like a client would: the same bearer GET must return
            // the created node, proving the header is a live endpoint and not
            // a well-formed string.
            using var fetched = await client.GetAsync(location);

            Assert.True(fetched.IsSuccessStatusCode, $"Location GET failed: {(int)fetched.StatusCode} {await fetched.Content.ReadAsStringAsync()}");
            Assert.Equal(System.Net.HttpStatusCode.OK, fetched.StatusCode);
        }


        // The production routing surface and nothing more: the app's own
        // controllers through MapControllers, plus the {action}-bearing
        // conventional routes Program.cs registers (Account/Home — the
        // patterns link generation resolves the pre-fix controller name
        // into, so leaving them out would test a routing table the real
        // server never runs; the Dashboard pattern has no {action} slot and
        // cannot produce a phantom action URL). The write path runs over the
        // real administration service with mocked collaborators
        // (HsmMcpWireTests' trade).
        private IHost BuildHost()
        {
            return Host.CreateDefaultBuilder()
                .UseEnvironment(Environments.Development)
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddRouting();

                        services.AddControllers()
                            .AddApplicationPart(typeof(ProductsApiController).Assembly);

                        services.AddAuthentication()
                            .AddHsmApiTokenScheme();
                        services.AddHsmApiTokenAuthorization();

                        services.AddScoped<SensorTreeReadService>();
                        services.AddScoped<ProductAdministrationService>();

                        services.AddSingleton(_cache.Object);
                        services.AddSingleton(_authorization.Object);
                        services.AddSingleton(_users.Object);
                        services.AddSingleton(_tokens.Object);
                        services.AddSingleton(_securityEvents.Object);
                        services.AddSingleton(new ApiTokensConfig { Enabled = true });
                        services.AddSingleton<ApiTokenInvalidAttemptLimiter>();
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();

                        app.UseAuthentication();
                        app.UseAuthorization();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();

                            endpoints.MapControllerRoute(
                                name: "Account",
                                pattern: "{controller=Account}/{action}",
                                defaults: new { controller = "Account" });

                            endpoints.MapControllerRoute(
                                name: "Home",
                                pattern: "{controller=Home}/{action=Index}");
                        });
                    });
                })
                .Build();
        }
    }
}
