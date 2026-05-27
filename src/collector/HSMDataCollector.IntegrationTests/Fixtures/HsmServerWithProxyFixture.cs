using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Options;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Fixtures
{
    public class HsmServerWithProxyFixture : IAsyncLifetime
    {
        private const string HsmImage = "hsmonitoring/hierarchical_sensor_monitoring:latest";
        private const string ToxiproxyImage = "ghcr.io/shopify/toxiproxy:latest";
        private const int SensorPort = 44330;
        private const int SitePort = 44333;
        private const int ToxiproxyApiPort = 8474;
        private const string ProxyName = "hsm_sensor";

        private INetwork _network;
        private IContainer _hsmContainer;
        private IContainer _toxiproxyContainer;
        private HttpClient _adminClient;
        private HttpClient _toxiproxyClient;
        private string _accessKey;
        private string _tempConfigDir;

        public string ServerAddress => "localhost";
        public ushort ProxyPort { get; private set; }
        public ushort MappedSitePort { get; private set; }
        public string AccessKey => _accessKey;

        public async Task InitializeAsync()
        {
            _tempConfigDir = PrepareConfigDirectory();

            var testImage = new ImageFromDockerfileBuilder()
                .WithName("hsm-integration-test-proxy:latest")
                .WithDockerfileDirectory(_tempConfigDir)
                .WithDockerfile("Dockerfile")
                .WithImageBuildPolicy(_ => true)
                .Build();
            await testImage.CreateAsync();

            _network = new NetworkBuilder()
                .WithName($"hsm-test-{Guid.NewGuid():N}")
                .Build();

            _hsmContainer = new ContainerBuilder()
                .WithImage(testImage)
                .WithPortBinding(SensorPort, true)
                .WithPortBinding(SitePort, true)
                .WithNetwork(_network)
                .WithNetworkAliases("hsm-server")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(SensorPort))
                .Build();

            _toxiproxyContainer = new ContainerBuilder()
                .WithImage(ToxiproxyImage)
                .WithPortBinding(ToxiproxyApiPort, true)
                .WithPortBinding(SensorPort, true)
                .WithNetwork(_network)
                .WithNetworkAliases("toxiproxy")
                .Build();

            await _hsmContainer.StartAsync();
            await _toxiproxyContainer.StartAsync();

            ProxyPort = _toxiproxyContainer.GetMappedPublicPort(SensorPort);
            MappedSitePort = _hsmContainer.GetMappedPublicPort(SitePort);

            var toxiproxyApiPort = _toxiproxyContainer.GetMappedPublicPort(ToxiproxyApiPort);
            _toxiproxyClient = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{toxiproxyApiPort}"),
            };

            await CreateProxyAsync();

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            _adminClient = new HttpClient(handler);

            await AuthenticateAsync();
            await CreateProductAndGetAccessKeyAsync();
        }

        public async Task DisposeAsync()
        {
            _adminClient?.Dispose();
            _toxiproxyClient?.Dispose();
            if (_toxiproxyContainer != null)
                await _toxiproxyContainer.DisposeAsync();
            if (_hsmContainer != null)
                await _hsmContainer.DisposeAsync();
            if (_network != null)
                await _network.DisposeAsync();
            if (_tempConfigDir != null && Directory.Exists(_tempConfigDir))
                Directory.Delete(_tempConfigDir, recursive: true);
        }

        public CollectorOptions CreateCollectorOptions(string clientName = null)
        {
            return new CollectorOptions
            {
                ServerAddress = ServerAddress,
                Port = ProxyPort,
                AccessKey = _accessKey,
                PackageCollectPeriod = TimeSpan.FromSeconds(2),
                MaxQueueSize = 1000,
                MaxValuesInPackage = 100,
                ClientName = clientName ?? $"TestClient_{Guid.NewGuid():N}",
                ComputerName = "TestMachine",
                Module = "IntegrationTests",
            };
        }

        public async Task DisableConnectionAsync()
        {
            await _toxiproxyClient.DeleteAsync($"/proxies/{ProxyName}");
        }

        public async Task EnableConnectionAsync()
        {
            await CreateProxyAsync();
        }

        public async Task AddLatencyAsync(int latencyMs)
        {
            var toxic = new
            {
                name = "latency",
                type = "latency",
                attributes = new { latency = latencyMs },
                stream = "downstream",
            };
            var json = JsonSerializer.Serialize(toxic);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _toxiproxyClient.PostAsync($"/proxies/{ProxyName}/toxics", content);
        }

        public async Task AddTimeoutAsync()
        {
            var toxic = new
            {
                name = "timeout",
                type = "timeout",
                attributes = new { timeout = 0 },
                stream = "downstream",
            };
            var json = JsonSerializer.Serialize(toxic);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _toxiproxyClient.PostAsync($"/proxies/{ProxyName}/toxics", content);
        }

        public async Task RemoveToxicAsync(string toxicName)
        {
            await _toxiproxyClient.DeleteAsync($"/proxies/{ProxyName}/toxics/{toxicName}");
        }

        public async Task AddConnectionResetAsync()
        {
            await DisableConnectionAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            await EnableConnectionAsync();
        }

        public async Task SlowDownAsync(int kbps)
        {
            var toxic = new
            {
                name = "bandwidth",
                type = "bandwidth",
                attributes = new { rate = kbps },
                stream = "downstream",
            };
            var json = JsonSerializer.Serialize(toxic);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _toxiproxyClient.PostAsync($"/proxies/{ProxyName}/toxics", content);
        }

        private async Task CreateProxyAsync()
        {
            var proxy = new
            {
                name = ProxyName,
                upstream = $"hsm-server:{SensorPort}",
                enabled = true,
            };
            var json = JsonSerializer.Serialize(proxy);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _toxiproxyClient.PostAsync("/proxies", content);
        }

        private async Task AuthenticateAsync()
        {
            var siteUrl = $"https://{ServerAddress}:{MappedSitePort}";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = "default",
                ["Password"] = "default",
                ["KeepLoggedIn"] = "true",
            });
            await _adminClient.PostAsync($"{siteUrl}/Account/Authenticate", content);
        }

        private async Task CreateProductAndGetAccessKeyAsync()
        {
            var siteUrl = $"https://{ServerAddress}:{MappedSitePort}";
            await _adminClient.PostAsync($"{siteUrl}/Product/CreateProduct",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "ProxyTestProduct" }));

            var productsHtml = await _adminClient.GetStringAsync($"{siteUrl}/Product/Index");
            var guidMatch = Regex.Match(productsHtml,
                @"[0-9a-fA-F]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");
            var productId = guidMatch.Success ? guidMatch.Value : null;
            if (productId == null) throw new InvalidOperationException("Product not found");

            var keysHtml = await _adminClient.GetStringAsync($"{siteUrl}/AccessKeys/AccessKeysForProduct?productId={productId}");
            var keyIdx = keysHtml.IndexOf("DefaultKey", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) throw new InvalidOperationException("DefaultKey not found in keys page");
            var keyMatch = Regex.Match(keysHtml.Substring(keyIdx),
                @"[0-9a-fA-F]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");
            _accessKey = keyMatch.Success ? keyMatch.Value.ToLowerInvariant() : throw new InvalidOperationException("Access key not found");
        }

        private static string PrepareConfigDirectory()
        {
            var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var sourceConfigDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
                "server", "HSMServer", "Config"));
            var tempDir = Path.Combine(Path.GetTempPath(), $"hsm-config-{Guid.NewGuid():N}");

            Directory.CreateDirectory(tempDir);

            foreach (var file in Directory.GetFiles(sourceConfigDir))
                File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)));

            var targetSettings = Path.Combine(tempDir, "appsettings.json");
            if (!File.Exists(targetSettings))
                File.Copy(Path.Combine(tempDir, "appsettings.Development.json"), targetSettings);

            File.WriteAllText(Path.Combine(tempDir, "Dockerfile"),
                "FROM hsmonitoring/hierarchical_sensor_monitoring:latest\n" +
                "USER root\n" +
                "COPY appsettings*.json /app/Config/\n");

            return tempDir;
        }
    }
}
