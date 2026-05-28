using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Options;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Fixtures
{
    public class HsmServerFixture : IAsyncLifetime
    {
        private const string DockerImage = "hsmonitoring/hierarchical_sensor_monitoring:latest";
        private const int SensorPort = 44330;
        private const int SitePort = 44333;
        private const string DefaultUsername = "default";
        private const string DefaultPassword = "default";
        private const string TestProductName = "IntegrationTests";

        private IContainer _container;
        private HttpClient _adminClient;
        private string _accessKey;
        private string _tempConfigDir;

        public string ServerAddress => "localhost";
        public ushort MappedSensorPort { get; private set; }
        public ushort MappedSitePort { get; private set; }
        public string AccessKey => _accessKey;

        public async Task InitializeAsync()
        {
            _tempConfigDir = PrepareConfigDirectory();

            var testImage = new ImageFromDockerfileBuilder()
                .WithName("hsm-integration-test:latest")
                .WithDockerfileDirectory(_tempConfigDir)
                .WithDockerfile("Dockerfile")
                .WithImageBuildPolicy(_ => true)
                .Build();
            await testImage.CreateAsync();

            _container = new ContainerBuilder()
                .WithImage(testImage)
                .WithPortBinding(SensorPort, true)
                .WithPortBinding(SitePort, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(SensorPort))
                .Build();

            await _container.StartAsync();

            MappedSensorPort = _container.GetMappedPublicPort(SensorPort);
            MappedSitePort = _container.GetMappedPublicPort(SitePort);

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
            if (_container != null)
                await _container.DisposeAsync();
            if (_tempConfigDir != null && Directory.Exists(_tempConfigDir))
                Directory.Delete(_tempConfigDir, recursive: true);
        }

        public CollectorOptions CreateCollectorOptions(string clientName = null)
        {
            return new CollectorOptions
            {
                ServerAddress = ServerAddress,
                Port = MappedSensorPort,
                AccessKey = _accessKey,
                PackageCollectPeriod = TimeSpan.FromSeconds(2),
                MaxQueueSize = 1000,
                MaxValuesInPackage = 100,
                ClientName = clientName ?? $"TestClient_{Guid.NewGuid():N}",
                ComputerName = "TestMachine",
                Module = "IntegrationTests",
            };
        }

        public HttpClient CreateSensorApiClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Key", _accessKey);
            client.BaseAddress = new Uri($"https://{ServerAddress}:{MappedSensorPort}");
            return client;
        }

        public async Task StopContainerAsync()
        {
            if (_container != null)
                await _container.StopAsync();
        }

        public async Task StartContainerAsync()
        {
            if (_container != null)
                await _container.StartAsync();
        }

        private async Task AuthenticateAsync()
        {
            var siteUrl = $"https://{ServerAddress}:{MappedSitePort}";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = DefaultUsername,
                ["Password"] = DefaultPassword,
                ["KeepLoggedIn"] = "true",
            });

            var response = await _adminClient.PostAsync($"{siteUrl}/Account/Authenticate", content);
            if ((int)response.StatusCode != 302)
                response.EnsureSuccessStatusCode();
        }

        private async Task CreateProductAndGetAccessKeyAsync()
        {
            var siteUrl = $"https://{ServerAddress}:{MappedSitePort}";

            var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = TestProductName,
            });
            await _adminClient.PostAsync($"{siteUrl}/Product/CreateProduct", createContent);

            var productsHtml = await _adminClient.GetStringAsync($"{siteUrl}/Product/Index");
            var productId = ExtractGuidFromText(productsHtml, TestProductName);
            if (productId == null)
                throw new InvalidOperationException($"Product '{TestProductName}' not found in products page");

            var keysHtml = await _adminClient.GetStringAsync($"{siteUrl}/AccessKeys/AccessKeysForProduct?productId={productId}");
            var accessKey = ExtractGuidFromText(keysHtml, "DefaultKey");
            if (accessKey == null)
                throw new InvalidOperationException("Access key not found in keys page");

            _accessKey = accessKey;
        }

        private static string ExtractGuidFromText(string text, string nearText = null)
        {
            var guidPattern = "[0-9a-fA-F]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";

            if (nearText != null)
            {
                var nearIdx = text.IndexOf(nearText, StringComparison.OrdinalIgnoreCase);
                if (nearIdx < 0) return null;
                var searchFrom = nearIdx;
                var match = Regex.Match(text.Substring(searchFrom), guidPattern, RegexOptions.IgnoreCase);
                return match.Success ? match.Value.ToLowerInvariant() : null;
            }

            var allMatches = Regex.Matches(text, guidPattern, RegexOptions.IgnoreCase);
            return allMatches.Count > 0 ? allMatches[0].Value.ToLowerInvariant() : null;
        }

        private static string PrepareConfigDirectory()
        {
            var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var sourceConfigDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
                "server", "HSMServer", "Config"));
            var tempDir = Path.Combine(Path.GetTempPath(), $"hsm-config-{Guid.NewGuid():N}");

            Directory.CreateDirectory(tempDir);

            if (Directory.Exists(sourceConfigDir))
            {
                foreach (var file in Directory.GetFiles(sourceConfigDir))
                    File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)));
            }

            // Release builds expect appsettings.json — copy from Development config if missing
            var targetSettings = Path.Combine(tempDir, "appsettings.json");
            var developmentSettings = Path.Combine(tempDir, "appsettings.Development.json");
            if (!File.Exists(targetSettings) && File.Exists(developmentSettings))
                File.Copy(Path.Combine(tempDir, "appsettings.Development.json"), targetSettings);

            File.WriteAllText(Path.Combine(tempDir, "Dockerfile"),
                "FROM hsmonitoring/hierarchical_sensor_monitoring:latest\n" +
                "USER root\n" +
                (File.Exists(targetSettings) || File.Exists(developmentSettings)
                    ? "COPY appsettings*.json /app/Config/\n"
                    : string.Empty));

            return tempDir;
        }
    }
}
