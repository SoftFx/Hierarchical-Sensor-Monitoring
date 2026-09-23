using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.Agent;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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
        [InlineData(true, false, true, LinuxProbeCaDecision.Include)]              // Kestrel serves a configured certificate
        [InlineData(true, true, true, LinuxProbeCaDecision.RefuseBundledDefault)]  // the repo's default: its key is public
        [InlineData(true, false, false, LinuxProbeCaDecision.OmitUnreadable)]      // nothing exportable: reported
        [InlineData(false, false, true, LinuxProbeCaDecision.Omit)]                // behind a TLS-terminating proxy
        [InlineData(false, true, true, LinuxProbeCaDecision.Omit)]                 // proxy in front of a default-cert Kestrel
        public void ServerCa_Decision(bool serverTerminatesTls, bool isBundledDefault, bool hasCertificate, LinuxProbeCaDecision expected)
        {
            Assert.Equal(expected, LinuxProbeServerCa.Decide(serverTerminatesTls, isBundledDefault, hasCertificate));
        }

        [Fact]
        public void ServerCa_ExportsTheLeafPublicCertificateOnly()
        {
            using var served = CreateSelfSigned();
            Assert.True(served.HasPrivateKey);

            var (decision, pem) = LinuxProbeServerCa.Resolve(true, () => (served, false));

            Assert.Equal(LinuxProbeCaDecision.Include, decision);
            Assert.StartsWith("-----BEGIN CERTIFICATE-----", pem);
            Assert.DoesNotContain("PRIVATE KEY", pem);
            Assert.Equal(1, pem.Split("-----BEGIN CERTIFICATE-----").Length - 1);

            using var exported = X509Certificate2.CreateFromPem(pem);
            Assert.Equal(served.Thumbprint, exported.Thumbprint);
            Assert.False(exported.HasPrivateKey);
        }

        [Fact]
        public void ServerCa_IsOmitted_BehindAProxy_WithoutTouchingTheCertificate()
        {
            var (decision, pem) = LinuxProbeServerCa.Resolve(false, () => throw new InvalidOperationException("must not be read"));

            Assert.Equal(LinuxProbeCaDecision.Omit, decision);
            Assert.Null(pem);
        }

        [Fact]
        public void ServerCa_RefusesTheBundledDefault()
        {
            using var served = CreateSelfSigned();

            var (decision, pem) = LinuxProbeServerCa.Resolve(true, () => (served, true));

            Assert.Equal(LinuxProbeCaDecision.RefuseBundledDefault, decision);
            Assert.Null(pem);
        }

        [Fact]
        public void ServerCa_IsOmitted_WhenTheCertificateCannotBeRead()
        {
            var (decision, pem) = LinuxProbeServerCa.Resolve(true, () => throw new CryptographicException("unreadable certificate file"));

            Assert.Equal(LinuxProbeCaDecision.OmitUnreadable, decision);
            Assert.Null(pem);
        }

        [Theory]
        // The reference deployment (#1427): Caddy terminates the client's TLS with a public certificate
        // and re-encrypts to Kestrel, so the Kestrel handshake is not the one the probe will verify.
        [InlineData(true, true, true, false)]
        [InlineData(true, true, false, true)]    // trusted proxies configured, but this request came directly
        [InlineData(true, false, true, true)]    // no trusted proxy configured: an invented header changes nothing
        [InlineData(false, false, false, false)] // plaintext hop: nothing of ours terminated it
        public void ServerTerminatesClientTls_IsFalse_OnlyForATrustedProxyHop(bool kestrelHandshake, bool trustedProxyConfigured, bool forwarded, bool expected)
        {
            Assert.Equal(expected, LinuxProbeServerCa.ServerTerminatesClientTls(kestrelHandshake, trustedProxyConfigured, forwarded));
        }

        [Fact]
        public void LinuxInstaller_ShipsNoCa_BehindTheBundledReverseProxy()
        {
            // docker-compose.yml: Caddy holds the public certificate, HSM keeps the bundled default on the
            // proxy hop. Nothing of HSM's is worth trusting there, and nothing needs to be: no 400, no CA.
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllBytes(Path.Combine(webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"), new byte[] { 1 });

                var controller = CreateController(webRoot, out var productId, certificate: new ServerCertificateConfig(), tls: true, behindTrustedProxy: true);

                var result = Assert.IsType<FileContentResult>(controller.LinuxInstaller(productId));

                using var gzip = new GZipStream(new MemoryStream(result.FileContents), CompressionMode.Decompress);
                using var reader = new TarReader(gzip);
                var names = new System.Collections.Generic.List<string>();
                while (reader.GetNextEntry() is { } entry)
                    names.Add(entry.Name);

                Assert.DoesNotContain(names, n => n.EndsWith("/server-ca.pem"));
                Assert.Contains(names, n => n.EndsWith("/config.json"));
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
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

                Assert.Contains("hsm-linux-probe-Garage_server/hsm-linux-probe_0.1.0_amd64.deb", names);
                Assert.Contains("hsm-linux-probe-Garage_server/access-key", names);
                Assert.DoesNotContain("hsm-linux-probe-Garage_server/server-ca.pem", names); // no TLS handshake on this (test) connection
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }

        [Fact]
        public void LinuxInstaller_ShipsTheServerCertificate_WhenKestrelTerminatesTls()
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            var pfx = WriteSelfSignedPfx("secret", out var thumbprint);
            try
            {
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllBytes(Path.Combine(webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"), new byte[] { 1 });

                // An absolute Name wins over the Config folder in Path.Combine, so this is "a configured certificate".
                var controller = CreateController(webRoot, out var productId, certificate: new ServerCertificateConfig { Name = pfx, Key = "secret" }, tls: true);

                var result = Assert.IsType<FileContentResult>(controller.LinuxInstaller(productId));

                using var gzip = new GZipStream(new MemoryStream(result.FileContents), CompressionMode.Decompress);
                using var reader = new TarReader(gzip);
                string pem = null;
                UnixFileMode mode = default;
                while (reader.GetNextEntry(copyData: true) is { } entry)
                    if (entry.Name.EndsWith("/server-ca.pem"))
                    {
                        pem = new StreamReader(entry.DataStream).ReadToEnd();
                        mode = entry.Mode;
                    }

                Assert.NotNull(pem);
                Assert.Equal((UnixFileMode)0b110_100_100, mode); // 0644
                Assert.DoesNotContain("PRIVATE KEY", pem);
                using var exported = X509Certificate2.CreateFromPem(pem);
                Assert.Equal(thumbprint, exported.Thumbprint);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
                File.Delete(pfx);
            }
        }

        [Fact]
        public void LinuxInstaller_RefusesTheBundledDefaultCertificate()
        {
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllBytes(Path.Combine(webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"), new byte[] { 1 });

                // Name empty: Kestrel falls back to the bundled default.server.pfx, whose private key is public.
                var controller = CreateController(webRoot, out var productId, certificate: new ServerCertificateConfig(), tls: true);

                var result = Assert.IsType<BadRequestObjectResult>(controller.LinuxInstaller(productId));

                Assert.Equal(LinuxProbeServerCa.BundledDefaultMessage, result.Value);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }

        [Fact]
        public void LinuxInstaller_ReportsNotStaged_BeforeTheCertificate()
        {
            // An out-of-the-box server has both "no package" and "default certificate": the package is the
            // real blocker, so it is what the admin hears about first.
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            try
            {
                var controller = CreateController(webRoot, out var productId, certificate: new ServerCertificateConfig(), tls: true);

                var result = Assert.IsType<ObjectResult>(controller.LinuxInstaller(productId));

                Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }

        [Theory]
        [InlineData(nameof(AgentController.LinuxInstaller))]
        [InlineData(nameof(AgentController.Installer))]
        public void Installers_AreNeverCacheable(string action)
        {
            var attribute = (ResponseCacheAttribute)typeof(AgentController).GetMethod(action)
                .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true).Single();

            Assert.True(attribute.NoStore);
            Assert.Equal(ResponseCacheLocation.None, attribute.Location);
        }

        [Fact]
        public void LinuxInstaller_UsesTheCertificateKestrelLoaded_NotALaterSettingsChange()
        {
            // Settings saved but not yet applied (restart pending): Kestrel still serves what it loaded.
            var webRoot = Directory.CreateTempSubdirectory("hsm-probe-test-").FullName;
            var pfx = WriteSelfSignedPfx("secret", out var thumbprint);
            try
            {
                Directory.CreateDirectory(Path.Combine(webRoot, "probe"));
                File.WriteAllBytes(Path.Combine(webRoot, "probe", "hsm-linux-probe_0.1.0_amd64.deb"), new byte[] { 1 });

                var certificate = new ServerCertificateConfig { Name = pfx, Key = "secret" };
                _ = certificate.Certificate; // loaded at startup
                certificate.Name = string.Empty; // admin clears the setting; takes effect after a restart

                var result = CreateController(webRoot, out var productId, certificate: certificate, tls: true).LinuxInstaller(productId);

                Assert.IsType<FileContentResult>(result);
                Assert.False(certificate.IsBundledDefault);
            }
            finally
            {
                Directory.Delete(webRoot, recursive: true);
                File.Delete(pfx);
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


        private static AgentController CreateController(string webRoot, out Guid productId, string externalUrl = "https://hsm.example.com",
                                                        ServerCertificateConfig certificate = null, bool tls = false, bool behindTrustedProxy = false)
        {
            var product = new ProductModel("Garage server");
            var key = AccessKeyModel.BuildDefault(product);
            product.AccessKeys.TryAdd(key.Id, key);
            productId = product.Id;

            var cache = new Mock<ITreeValuesCache>();
            cache.Setup(c => c.TryGetProduct(product.Id, out product)).Returns(true);

            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Agent).Returns(new AgentConfig { ExternalConnectionUrl = externalUrl });
            config.Setup(c => c.Kestrel).Returns(new KestrelConfig
            {
                TrustedProxies = behindTrustedProxy ? ["attached-networks"] : [],
            });
            config.Setup(c => c.ServerCertificate).Returns(certificate ?? new ServerCertificateConfig());

            var environment = new Mock<IWebHostEnvironment>();
            environment.Setup(e => e.WebRootPath).Returns(webRoot);

            var context = new DefaultHttpContext { User = new User() };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("hsm.example.com");
            if (tls)
                context.Features.Set(new Mock<ITlsHandshakeFeature>().Object);

            // What the forwarded-headers middleware leaves behind after it consumed a trusted proxy's header.
            if (behindTrustedProxy)
                context.Request.Headers[ForwardedHeadersDefaults.XOriginalForHeaderName] = "203.0.113.7:51000";

            return new AgentController(new Mock<IUserManager>().Object, cache.Object, config.Object, environment.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };
        }

        private static X509Certificate2 CreateSelfSigned()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=hsm.example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
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
