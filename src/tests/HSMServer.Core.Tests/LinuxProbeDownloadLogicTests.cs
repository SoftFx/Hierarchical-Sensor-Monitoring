using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.Agent;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace HSMServer.Core.Tests
{
    // Deterministic decisions behind the Linux probe download (#1424): which staged .deb is served,
    // which addresses the probe can use, whether server-ca.pem ships and what it may contain, and the
    // endpoint's guard / 503 path.
    public class LinuxProbeDownloadLogicTests
    {
        [Fact]
        public void SelectStagedPackage_PicksTheSingleDeb()
        {
            var files = new[] { "/w/probe/README.md", "/w/probe/.gitignore", "/w/probe/hsm-linux-probe_0.1.0_amd64.deb" };

            Assert.Equal("hsm-linux-probe_0.1.0_amd64.deb", LinuxProbeInstallerBundle.SelectStagedPackage(files));
        }

        [Theory]
        [InlineData()]
        [InlineData("README.md", "hsm-linux-probe_0.1.0_amd64.deb.sha256")]
        [InlineData("hsm-linux-probe_0.1.0_amd64.deb", "hsm-linux-probe_0.2.0_amd64.deb")]
        [InlineData("other_0.1.0_amd64.deb")]
        public void SelectStagedPackage_ReturnsNull_WhenNoneOrAmbiguous(params string[] files)
        {
            Assert.Null(LinuxProbeInstallerBundle.SelectStagedPackage(files));
        }

        [Theory]
        [InlineData("https://hsm.example.com", true)]
        [InlineData("HTTPS://hsm.example.com", true)]
        [InlineData("http://hsm.example.com", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ValidateServerAddress_AcceptsHttpsOnly(string address, bool valid)
        {
            Assert.Equal(valid, LinuxProbeInstallerBundle.ValidateServerAddress(address) is null);
        }

        [Theory]
        [InlineData(true, true, true)]   // Kestrel serves its own certificate: ship it
        [InlineData(false, true, false)] // behind a TLS-terminating proxy: the proxy's cert is what clients see
        [InlineData(true, false, false)] // nothing exportable
        [InlineData(false, false, false)]
        public void ServerCa_IsIncludedOnlyWhenTheServerTerminatesTlsWithACertificate(bool serverTerminatesTls, bool hasCertificate, bool expected)
        {
            Assert.Equal(expected, LinuxProbeServerCa.ShouldInclude(serverTerminatesTls, hasCertificate));
        }

        [Fact]
        public void ServerCa_ExportsThePublicCertificateOnly()
        {
            var path = WriteSelfSignedPfx("secret", out var thumbprint);
            try
            {
                var pem = LinuxProbeServerCa.Resolve(true, () => (path, "secret"));

                Assert.NotNull(pem);
                Assert.StartsWith("-----BEGIN CERTIFICATE-----", pem);
                Assert.DoesNotContain("PRIVATE KEY", pem);

                using var exported = X509Certificate2.CreateFromPem(pem);
                Assert.Equal(thumbprint, exported.Thumbprint);
                Assert.False(exported.HasPrivateKey);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ServerCa_IsOmitted_BehindAProxy_WithoutTouchingTheCertificate()
        {
            Assert.Null(LinuxProbeServerCa.Resolve(false, () => throw new InvalidOperationException("must not be read")));
        }

        [Fact]
        public void ServerCa_IsOmitted_WhenTheCertificateCannotBeRead()
        {
            Assert.Null(LinuxProbeServerCa.Resolve(true, () => (Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pfx"), null)));
        }

        [Fact]
        public void LinuxInstaller_IsAdminOnly()
        {
            var method = typeof(AgentController).GetMethod(nameof(AgentController.LinuxInstaller));

            Assert.NotNull(method.GetCustomAttributes(typeof(AuthorizeIsAdminAttribute), inherit: true).SingleOrDefault());
            var route = (HttpGetAttribute)method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Single();
            Assert.Equal("linux-installer", route.Template);
        }

        [Fact]
        public void LinuxInstaller_Returns503_WhenNoPackageIsStaged()
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllText(Path.Combine(webRoot, "probe", "README.md"), "drop-point");

                var result = Assert.IsType<ObjectResult>(CreateController(webRoot, out var productId).LinuxInstaller(productId));

                Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
                Assert.Equal(LinuxProbeInstallerBundle.NotStagedMessage, result.Value);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }

        [Fact]
        public void LinuxInstaller_Returns503_WhenTheStagingFolderIsMissing()
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                var result = Assert.IsType<ObjectResult>(CreateController(webRoot, out var productId).LinuxInstaller(productId));

                Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }

        [Fact]
        public void LinuxInstaller_Returns503_WithoutAWebRoot()
        {
            var result = Assert.IsType<ObjectResult>(CreateController(webRoot: null, out var productId).LinuxInstaller(productId));

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        }

        [Fact]
        public void LinuxInstaller_StreamsTheBundle_WhenAPackageIsStaged()
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                var package = new byte[] { 0x21, 0x3C, 0x61, 0x72, 0x63, 0x68, 0x3E, 0x0A, 0x00, 0xFF };
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllBytes(Path.Combine(webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"), package);

                var result = Assert.IsType<FileContentResult>(CreateController(webRoot, out var productId).LinuxInstaller(productId));

                Assert.Equal("application/gzip", result.ContentType);
                Assert.Equal("hsm-linux-probe-Garage_server.tar.gz", result.FileDownloadName);

                using var gzip = new GZipStream(new MemoryStream(result.FileContents), CompressionMode.Decompress);
                using var reader = new TarReader(gzip);
                var names = new System.Collections.Generic.List<string>();
                while (reader.GetNextEntry() is { } entry)
                    names.Add(entry.Name);

                Assert.Contains("hsm-linux-probe_0.1.0_amd64.deb", names);
                Assert.Contains("access-key", names);
                Assert.DoesNotContain("server-ca.pem", names); // no TLS handshake on this (test) connection
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }

        [Fact]
        public void LinuxInstaller_RejectsAPlaintextAddress()
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllBytes(Path.Combine(webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"), new byte[] { 1 });

                var controller = CreateController(webRoot, out var productId, externalUrl: "http://hsm.example.com:44330");

                Assert.IsType<BadRequestObjectResult>(controller.LinuxInstaller(productId));
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }


        private static AgentController CreateController(string webRoot, out Guid productId, string externalUrl = "https://hsm.example.com")
        {
            var product = new ProductModel("Garage server");
            var key = AccessKeyModel.BuildDefault(product);
            product.AccessKeys.TryAdd(key.Id, key);
            productId = product.Id;

            var cache = new Mock<ITreeValuesCache>();
            cache.Setup(c => c.TryGetProduct(product.Id, out product)).Returns(true);

            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Agent).Returns(new AgentConfig { ExternalConnectionUrl = externalUrl });
            config.Setup(c => c.Kestrel).Returns(new KestrelConfig());

            var environment = new Mock<IWebHostEnvironment>();
            environment.Setup(e => e.WebRootPath).Returns(webRoot);

            var context = new DefaultHttpContext { User = new User() };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("hsm.example.com");

            return new AgentController(new Mock<IUserManager>().Object, cache.Object, config.Object, environment.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };
        }

        private static string WriteSelfSignedPfx(string password, out string thumbprint)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=hsm.example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            thumbprint = certificate.Thumbprint;
            var path = Path.Combine(Path.GetTempPath(), $"hsm-probe-test-{Guid.NewGuid():N}.pfx");
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
            return path;
        }
    }
}
