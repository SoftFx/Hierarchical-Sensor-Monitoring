using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Agent;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Hosting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests
{
    // #1550: the admin Downloads page. The builder must offer exactly what the download endpoints will
    // serve (same staged files, same "not staged" rules), and list the root products the admin sees.
    public class DownloadsPageTests
    {
        private const string Address = "https://hsm.example.com";
        private const int Port = 44330;


        [Fact]
        public void Build_OrdersProductsByName_CaseInsensitive_ThenFolder_AndDropsDuplicates()
        {
            var alpha = new DownloadProductItem(Guid.NewGuid(), "alpha", null);
            var betaInZ = new DownloadProductItem(Guid.NewGuid(), "Beta", "Z folder");
            var betaInA = new DownloadProductItem(Guid.NewGuid(), "beta", "A folder");
            var gamma = new DownloadProductItem(Guid.NewGuid(), "Gamma", null);

            var model = DownloadsPageBuilder.Build([gamma, betaInZ, alpha, betaInA, gamma, null], null, Address, Port);

            Assert.Equal([alpha.Id, betaInA.Id, betaInZ.Id, gamma.Id], model.Products.Select(p => p.Id));
        }

        [Fact]
        public void DisplayName_ShowsTheFolder_OnlyWhenThereIsOne()
        {
            Assert.Equal("Garage (Home)", new DownloadProductItem(Guid.NewGuid(), "Garage", "Home").DisplayName);
            Assert.Equal("Garage", new DownloadProductItem(Guid.NewGuid(), "Garage", null).DisplayName);
        }

        [Fact]
        public void Build_KeepsTheSelectedProduct_OnlyWhenItIsListed()
        {
            var product = new DownloadProductItem(Guid.NewGuid(), "Garage", null);

            Assert.Equal(product.Id, DownloadsPageBuilder.Build([product], null, Address, Port, product.Id).SelectedProductId);
            Assert.Null(DownloadsPageBuilder.Build([product], null, Address, Port, Guid.NewGuid()).SelectedProductId);
            Assert.Null(DownloadsPageBuilder.Build([product], null, Address, Port).SelectedProductId);
        }

        [Fact]
        public void Build_WithoutAWebRoot_ReportsNothingStaged()
        {
            var model = DownloadsPageBuilder.Build([], null, Address, Port);

            Assert.False(model.AgentStaged);
            Assert.Null(model.AgentVersion);
            Assert.False(model.ProbeStaged);
            Assert.Null(model.ProbePackage);
            Assert.Null(model.ProbeVersion);
            Assert.Equal(AgentInstallerBundle.NotStagedMessage, model.AgentNotStagedMessage);
            Assert.Equal(LinuxProbeInstallerBundle.NotStagedMessage, model.ProbeNotStagedMessage);
        }

        [Fact]
        public void Build_WithAnEmptyWebRoot_ReportsNothingStaged()
        {
            WithWebRoot(webRoot =>
            {
                var model = DownloadsPageBuilder.Build([], webRoot, Address, Port);

                Assert.False(model.AgentStaged);
                Assert.Null(model.AgentVersion);
                Assert.False(model.ProbeStaged);
            });
        }

        [Fact]
        public void Build_ReadsTheStagedAgentAndItsVersion()
        {
            WithWebRoot(webRoot =>
            {
                StageAgent(webRoot, exe: true, version: "0.5.28\r\n");

                var model = DownloadsPageBuilder.Build([], webRoot, Address, Port);

                Assert.True(model.AgentStaged);
                Assert.Equal("0.5.28", model.AgentVersion);
            });
        }

        [Theory]
        [InlineData(true, null, true)]     // exe without version.txt: downloadable, version unknown
        [InlineData(true, "  \n", true)]   // empty version.txt reads as unknown, not as a blank version
        [InlineData(false, "0.5.28", false)] // version.txt alone: the endpoint answers 503, so no button and no version
        public void Build_AgentStagedFollowsTheExe_VersionIsNullWhenUnknown(bool exe, string version, bool expectedStaged)
        {
            WithWebRoot(webRoot =>
            {
                StageAgent(webRoot, exe, version);

                var model = DownloadsPageBuilder.Build([], webRoot, Address, Port);

                Assert.Equal(expectedStaged, model.AgentStaged);
                Assert.Equal(expectedStaged && !string.IsNullOrWhiteSpace(version) ? version.Trim() : null, model.AgentVersion);
            });
        }

        [Fact]
        public void Build_ReadsTheStagedProbePackageAndItsVersion()
        {
            WithWebRoot(webRoot =>
            {
                StageProbe(webRoot, "hsm-linux-probe_0.8.4_amd64.deb", "README.md");

                var model = DownloadsPageBuilder.Build([], webRoot, Address, Port);

                Assert.True(model.ProbeStaged);
                Assert.Equal("hsm-linux-probe_0.8.4_amd64.deb", model.ProbePackage);
                Assert.Equal("0.8.4", model.ProbeVersion);
            });
        }

        [Fact]
        public void Build_TwoStagedProbePackages_AreNotStaged_LikeTheEndpoint()
        {
            WithWebRoot(webRoot =>
            {
                StageProbe(webRoot, "hsm-linux-probe_0.8.3_amd64.deb", "hsm-linux-probe_0.8.4_amd64.deb");

                var model = DownloadsPageBuilder.Build([], webRoot, Address, Port);

                Assert.False(model.ProbeStaged);
                Assert.Null(model.ProbeVersion);
            });
        }

        [Fact]
        public void Build_ProbeFolderWithOnlyTheReadme_IsNotStaged()
        {
            WithWebRoot(webRoot =>
            {
                StageProbe(webRoot, "README.md");

                Assert.False(DownloadsPageBuilder.Build([], webRoot, Address, Port).ProbeStaged);
            });
        }

        [Theory]
        [InlineData("hsm-linux-probe_0.8.4_amd64.deb", "0.8.4")]
        [InlineData("hsm-linux-probe_1.0.0-rc1_arm64.deb", "1.0.0-rc1")]
        [InlineData("hsm-linux-probe_0.9.0~rc1_amd64.deb", "0.9.0~rc1")]
        [InlineData("hsm-linux-probe_0.8.4.deb", null)]
        [InlineData("hsm-linux-probe__amd64.deb", null)]
        [InlineData("other_0.8.4_amd64.deb", null)]
        [InlineData("hsm-linux-probe_0.8.4_amd64.tar.gz", null)]
        [InlineData(null, null)]
        public void PackageVersion_ReadsTheVersionFromTheReleaseFileName(string packageName, string expected)
        {
            Assert.Equal(expected, LinuxProbeInstallerBundle.PackageVersion(packageName));
        }

        [Fact]
        public void Build_ReportsTheAddress_AndWarnsWhenTheProbeWouldRefuseIt()
        {
            var https = DownloadsPageBuilder.Build([], null, Address, Port);
            Assert.Equal(Address, https.ServerAddress);
            Assert.Equal(Port, https.ServerPort);
            Assert.Null(https.ProbeAddressWarning);

            var plain = DownloadsPageBuilder.Build([], null, "http://hsm.local", Port);
            Assert.Equal(LinuxProbeInstallerBundle.ValidateServerAddress("http://hsm.local"), plain.ProbeAddressWarning);
            Assert.NotNull(plain.ProbeAddressWarning);
        }

        [Theory]
        [InlineData(true, Address, true)]
        [InlineData(true, "http://hsm.local", false)] // every click would be a 400: the card shows the warning instead
        [InlineData(false, Address, false)]           // every click would be a 503: the card shows the not-staged notice
        [InlineData(false, "http://hsm.local", false)]
        public void ProbeDownloadable_RequiresAStagedPackage_AndAnAddressTheProbeAccepts(bool staged, string address, bool expected)
        {
            WithWebRoot(webRoot =>
            {
                if (staged)
                    StageProbe(webRoot, "hsm-linux-probe_0.8.4_amd64.deb");

                Assert.Equal(expected, DownloadsPageBuilder.Build([], webRoot, address, Port).ProbeDownloadable);
            });
        }

        [Fact]
        public void Service_ListsRootProductsWithTheirFolder_AndResolvesTheConfiguredAddress()
        {
            var folder = new FolderModel(EntitiesFactory.BuildFolderEntity() with { DisplayName = "Home" });
            var inFolder = new ProductModel(EntitiesFactory.BuildProductEntity(name: "Garage", parent: null) with { FolderId = folder.Id.ToString() });
            var loose = new ProductModel(EntitiesFactory.BuildProductEntity(name: "Attic", parent: null));
            var nested = new ProductModel(EntitiesFactory.BuildProductEntity(name: "Nested", parent: null));
            loose.AddSubProduct(nested);

            var service = CreateService([inFolder, loose], folder, externalUrl: "https://agents.example.com:8443", out _);

            var model = service.Build(new User("admin") { IsAdmin = true }, "https", "page.example.com");

            Assert.Equal(
                [("Attic", (string)null), ("Garage", "Home")],
                model.Products.Select(p => (p.Name, p.FolderName)));
            Assert.Equal("https://agents.example.com", model.ServerAddress);
            Assert.Equal(8443, model.ServerPort);
        }

        [Fact]
        public void Service_FallsBackToTheRequestHost_WhenNoConnectionUrlIsSet()
        {
            var service = CreateService([], null, externalUrl: "", out var sensorPort);

            var model = service.Build(new User("admin") { IsAdmin = true }, "https", "page.example.com");

            Assert.Equal("https://page.example.com", model.ServerAddress);
            Assert.Equal(sensorPort, model.ServerPort);
        }

        [Fact]
        public void Service_ListsOnlyTheProductsTheUserSees()
        {
            var product = new ProductModel(EntitiesFactory.BuildProductEntity(name: "Garage", parent: null));

            var service = CreateService([product], null, externalUrl: Address, out _);

            Assert.Empty(service.Build(new User("viewer"), "https", "page.example.com").Products);
        }

        [Fact]
        public void Controller_IsAdminOnly()
        {
            Assert.NotNull(typeof(DownloadsController).GetCustomAttributes(typeof(AuthorizeIsAdminAttribute), inherit: true).SingleOrDefault());
        }


        private static DownloadsPageService CreateService(List<ProductModel> products, FolderModel folder, string externalUrl, out int sensorPort)
        {
            var cache = new Mock<ITreeValuesCache>();
            cache.Setup(c => c.GetProducts()).Returns(products);

            var folders = new Mock<IFolderManager>();
            if (folder is not null)
                folders.Setup(f => f.TryGetValueById(folder.Id, out folder)).Returns(true);

            var users = new Mock<IUserManager>();
            users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());

            var kestrel = new KestrelConfig();
            sensorPort = kestrel.SensorPort;

            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Agent).Returns(new AgentConfig { ExternalConnectionUrl = externalUrl });
            config.Setup(c => c.Kestrel).Returns(kestrel);

            var environment = new Mock<IWebHostEnvironment>();
            environment.Setup(e => e.WebRootPath).Returns((string)null);

            return new DownloadsPageService(new TreeViewModel(cache.Object, folders.Object, users.Object), config.Object, environment.Object);
        }

        private static void StageAgent(string webRoot, bool exe, string version)
        {
            var dir = Directory.CreateDirectory(Path.Combine(webRoot, AgentInstallerBundle.StagingFolder)).FullName;
            if (exe)
                File.WriteAllBytes(Path.Combine(dir, AgentInstallerBundle.ExeName), [0x4D, 0x5A]);
            if (version is not null)
                File.WriteAllText(Path.Combine(dir, AgentInstallerBundle.VersionFileName), version);
        }

        private static void StageProbe(string webRoot, params string[] fileNames)
        {
            var dir = Directory.CreateDirectory(Path.Combine(webRoot, LinuxProbeInstallerBundle.StagingFolder)).FullName;
            foreach (var name in fileNames)
                File.WriteAllBytes(Path.Combine(dir, name), [1]);
        }

        private static void WithWebRoot(Action<string> test)
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-downloads-test-").FullName;
            try
            {
                test(webRoot);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }
    }
}
