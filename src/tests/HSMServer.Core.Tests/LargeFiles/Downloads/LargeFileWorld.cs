using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Middleware;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.LargeFiles;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The world of the download tests (#1885): two root products with a sensor each, users of every role, API tokens
    // over the REAL owner-mirroring evaluator, a fake store over a temporary folder, and mocks of the contracts other
    // branches implement (builder, dump passwords, download log). BuildHost assembles the real routing, authentication
    // and authorization of both surfaces in a TestServer (the ProductsApiCreateLocationWireTests trade): the web area
    // through a test cookie stand-in plus the real UserProcessorMiddleware, the API through the real HsmApiToken scheme.
    internal sealed class LargeFileWorld : IDisposable
    {
        internal const string SignInPath = "/test-sign-in";
        internal const string Password = "Secret-Password-Value-0123456789";

        private delegate void TryAuthenticateCallback(string presented, out ApiTokenInfo token);
        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private readonly Dictionary<string, User> _usersByName = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, User> _usersById = [];
        private readonly Dictionary<string, ApiTokenInfo> _tokensById = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, BaseSensorModel> _sensors = [];
        private readonly Dictionary<Guid, ProductModel> _products = [];
        private char _nextTokenLetter = 'A';


        public LargeFileWorld()
        {
            ProductA = AddProduct("alpha");
            ProductB = AddProduct("beta");
            SensorA = AddSensor(ProductA, "dumps");
            SensorB = AddSensor(ProductB, "dumps");

            Admin = AddUser(new User("admin") { IsAdmin = true });
            ManagerA = AddUser(new User("managerA") { ProductsRoles = { (ProductA.Id, ProductRoleEnum.ProductManager) } });
            ViewerA = AddUser(new User("viewerA") { ProductsRoles = { (ProductA.Id, ProductRoleEnum.ProductViewer) } });
            ManagerB = AddUser(new User("managerB") { ProductsRoles = { (ProductB.Id, ProductRoleEnum.ProductManager) } });

            Cache.Setup(c => c.GetSensor(It.IsAny<Guid>())).Returns((Guid id) => _sensors.GetValueOrDefault(id));
            Cache.Setup(c => c.TryGetProduct(It.IsAny<Guid>(), out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid id, out ProductModel product) => product = _products.GetValueOrDefault(id)))
                .Returns(true);

            Users.Setup(u => u[It.IsAny<string>()]).Returns((string name) => name is null ? null : _usersByName.GetValueOrDefault(name));
            Users.Setup(u => u[It.IsAny<Guid>()]).Returns((Guid id) => _usersById.GetValueOrDefault(id));

            Tokens.Setup(t => t.IsTokenLive(It.IsAny<string>())).Returns((string id) => _tokensById.ContainsKey(id));
            Tokens.Setup(t => t.GetToken(It.IsAny<string>())).Returns((string id) => _tokensById.GetValueOrDefault(id));

            Passwords.Setup(p => p.HasPassword(It.IsAny<Guid>())).Returns(true);
            Passwords.Setup(p => p.GetVersion(It.IsAny<Guid>(), It.IsAny<int>()))
                .Returns((Guid _, int version) => new DumpPasswordVersion(version, Password, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "managerA", null));
            Builder.Setup(b => b.GetStatus(It.IsAny<Guid>())).Returns(new ArchiveBuildStatus(ArchiveBuildState.Queued, 0, null, null));

            Config.Setup(c => c.Kestrel).Returns(new KestrelConfig());
            Config.Setup(c => c.LargeFileArchives).Returns(new LargeFileArchivesConfig());
        }


        public FakeLargeFileArchiveStore Store { get; } = new();

        public Mock<ITreeValuesCache> Cache { get; } = new();
        public Mock<IUserManager> Users { get; } = new();
        public Mock<IApiTokenManager> Tokens { get; } = new();
        public Mock<IFolderManager> Folders { get; } = new();
        public Mock<IApiTokenSecurityEventSink> SecurityEvents { get; } = new();
        public Mock<IArchiveBuilder> Builder { get; } = new();
        public Mock<IDumpPasswordService> Passwords { get; } = new();
        public Mock<IDownloadLog> Log { get; } = new();
        public Mock<ILargeFilesMonitor> Monitor { get; } = new();
        public Mock<IServerConfig> Config { get; } = new();

        public ProductModel ProductA { get; }
        public ProductModel ProductB { get; }
        public BaseSensorModel SensorA { get; }
        public BaseSensorModel SensorB { get; }

        public User Admin { get; }
        public User ManagerA { get; }
        public User ViewerA { get; }
        public User ManagerB { get; }


        public void TrustProxies() => Config.Setup(c => c.Kestrel).Returns(new KestrelConfig { TrustedProxies = ["10.0.0.1"] });

        public void RemoveSensor(BaseSensorModel sensor) => _sensors.Remove(sensor.Id);

        public static byte[] ArchiveBytes(int length = 64 * 1024, int seed = 1)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        /// <summary>A bearer credential of a live token of the owner.</summary>
        public string AddToken(User owner, bool readOnly = false)
        {
            var tokenId = new string(_nextTokenLetter++, 22);
            var credential = ApiTokenMaterial.FormatToken(tokenId, new string('B', 43));
            var token = new ApiTokenInfo { EntityId = Guid.NewGuid(), OwnerUserId = owner.Id, Name = $"token of {owner.Name}", ReadOnly = readOnly };

            _tokensById[tokenId] = token;

            Tokens.Setup(m => m.TryAuthenticate(credential, out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new TryAuthenticateCallback((string _, out ApiTokenInfo info) => info = token))
                .Returns(true);

            return credential;
        }

        public LargeFileDownloadService CreateService() =>
            new(Store, Cache.Object, CreateEvaluator(), Tokens.Object, Users.Object, Builder.Object, Passwords.Object, Log.Object,
                Monitor.Object, TimeProvider.System, NullLogger<LargeFileDownloadService>.Instance);

        public ApiTokenAuthorizationService CreateEvaluator() =>
            new(Users.Object, Tokens.Object, Folders.Object, Cache.Object, SecurityEvents.Object);


        public IHost BuildHost()
        {
            var host = Host.CreateDefaultBuilder()
                .UseEnvironment(Environments.Development)
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddControllers().AddApplicationPart(typeof(LargeFilesApiController).Assembly);

                        // The web area: the real cookie scheme (the DefaultPolicy is pinned to it); SignInPath issues the
                        // cookie of a user, as the login page does.
                        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                            .AddCookie(options => options.LoginPath = new PathString("/Account/Index"))
                            .AddHsmApiTokenScheme();
                        services.AddHsmApiTokenAuthorization();

                        services.AddSingleton(Store);
                        services.AddSingleton<ILargeFileArchiveStore>(Store);
                        services.AddSingleton(Cache.Object);
                        services.AddSingleton(Users.Object);
                        services.AddSingleton(Tokens.Object);
                        services.AddSingleton(Folders.Object);
                        services.AddSingleton(SecurityEvents.Object);
                        services.AddSingleton(Builder.Object);
                        services.AddSingleton(Passwords.Object);
                        services.AddSingleton(Log.Object);
                        services.AddSingleton(Monitor.Object);
                        services.AddSingleton(Config.Object);
                        services.AddSingleton(TimeProvider.System);
                        services.AddSingleton<IApiTokenAuthorizationService, ApiTokenAuthorizationService>();
                        services.AddSingleton(new ApiTokensConfig { Enabled = true });
                        services.AddSingleton<ApiTokenInvalidAttemptLimiter>();
                        services.AddScoped<LargeFileDownloadService>();
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseMiddleware<UserProcessorMiddleware>();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                            endpoints.MapGet(SignInPath, async context =>
                            {
                                var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, context.Request.Query["name"].ToString())],
                                    CookieAuthenticationDefaults.AuthenticationScheme);

                                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                            });
                        });
                    });
                })
                .Build();

            host.Start();
            return host;
        }

        /// <summary>A client signed in as the user through the real cookie (none for null).</summary>
        public static async Task<HttpClient> WebClient(IHost host, User user)
        {
            var client = host.GetTestClient();

            if (user is null)
                return client;

            using var signIn = await client.GetAsync($"{SignInPath}?name={Uri.EscapeDataString(user.Name)}");
            var cookie = signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

            client.DefaultRequestHeaders.Add("Cookie", cookie);
            return client;
        }

        public static HttpClient ApiClient(IHost host, string credential)
        {
            var client = host.GetTestClient();

            if (credential is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);

            return client;
        }

        // Status, body and every header except the per-request Date: what "the same answer" means.
        public static async Task<(int Status, string Body, string Headers)> Snapshot(HttpResponseMessage response)
        {
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(header => header.Key != "Date")
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .Select(header => $"{header.Key}: {string.Join(",", header.Value)}");

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), string.Join("\n", headers));
        }

        public void Dispose() => Store.Dispose();


        private ProductModel AddProduct(string name)
        {
            var product = new ProductModel(EntitiesFactory.BuildProductEntity(name: name) with { Id = Guid.NewGuid().ToString() });
            _products[product.Id] = product;
            return product;
        }

        private BaseSensorModel AddSensor(ProductModel product, string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: name, type: (byte)HSMCommon.Model.SensorType.File) with
            {
                Id = Guid.NewGuid().ToString(),
            });

            product.AddSensor(sensor);
            _sensors[sensor.Id] = sensor;
            return sensor;
        }

        private User AddUser(User user)
        {
            _usersByName[user.Name] = user;
            _usersById[user.Id] = user;
            return user;
        }
    }
}
