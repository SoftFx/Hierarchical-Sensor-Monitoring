using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.Agent;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Security.Cryptography;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Per-product HSM Agent download (epic #1167, W6/W7). The admin opens a product, clicks "Download
    /// Windows agent", and gets a zip with the byte-identical signed exe + a generated config.json
    /// (this server's address + the product's access key) + silent install scripts. The server ONLY
    /// generates + serves the zip — it is not an installer; the client install is the C++ exe's own
    /// <c>--install</c> (no .NET/MSI on the client).
    ///
    /// Epic #1174 adds two unauthenticated / key-authenticated endpoints for the agent self-update
    /// channel: <c>GET /api/agent/version</c> (version manifest) and <c>GET /api/agent/exe</c>
    /// (binary download, Key-header auth).
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    public class AgentController : BaseController
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ITreeValuesCache _cache;
        private readonly IServerConfig _config;
        private readonly IWebHostEnvironment _environment;


        public AgentController(IUserManager userManager, ITreeValuesCache cache, IServerConfig config, IWebHostEnvironment environment)
            : base(userManager)
        {
            _cache = cache;
            _config = config;
            _environment = environment;
        }


        [HttpGet("installer")]
        [AuthorizeIsAdmin]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // the bundle carries a bearer key
        public IActionResult Installer(Guid productId)
        {
            if (!_cache.TryGetProduct(productId, out var product))
                return NotFound("Product not found.");

            var key = AgentKeySelector.Select(product);
            if (key is null)
                return BadRequest("This product has no usable access key. Create one with send-data permission first.");

            // Without a web root there is no wwwroot/agent/ to read from; report the same graceful 503
            // as a missing binary instead of silently probing a relative "agent/hsm-agent.exe".
            if (string.IsNullOrEmpty(_environment.WebRootPath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    "The agent binary is not available on this server yet. Publish hsm-agent.exe to wwwroot/agent/.");

            var exePath = Path.Combine(_environment.WebRootPath, "agent", AgentInstallerBundle.ExeName);

            byte[] exeBytes;
            try
            {
                // Read directly (no separate File.Exists check) so a missing/locked exe — including the
                // race where it is removed between the check and the read — is the same graceful 503.
                exeBytes = System.IO.File.ReadAllBytes(exePath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    "The agent binary is not available on this server yet. Publish hsm-agent.exe to wwwroot/agent/.");
            }

            var (address, port) = AgentConnectionResolver.Resolve(
                _config.Agent.ExternalConnectionUrl, _config.Kestrel.SensorPort, Request.Scheme, Request.Host.Host);
            var options = new AgentBundleOptions(address, port, key.Id.ToString(), _config.Agent.AllowUntrustedCertificate, _config.Agent.EnableTopCpuProcesses);
            var zip = AgentInstallerBundle.BuildZip(exeBytes, options);

            _logger.Info($"{CurrentUser?.Name} downloaded the HSM Agent bundle for product '{product.DisplayName}' ({productId}).");

            return File(zip, "application/zip", $"hsm-agent-{Sanitize(product.DisplayName)}.zip");
        }


        /// <summary>
        /// Per-product Linux probe download (#1424): a .tar.gz with the byte-identical released .deb, a
        /// generated config.json (no key inside), the key in its own file, the server's public leaf certificate
        /// when this server terminates TLS itself, and install.sh/uninstall.sh. Same guard, key selection
        /// and address resolution as the Windows <see cref="Installer"/>.
        /// </summary>
        [HttpGet("linux-installer")]
        [AuthorizeIsAdmin]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // the bundle carries a bearer key
        public IActionResult LinuxInstaller(Guid productId)
        {
            if (!_cache.TryGetProduct(productId, out var product))
                return NotFound("Product not found.");

            var key = AgentKeySelector.Select(product);
            if (key is null)
                return BadRequest("This product has no usable access key. Create one with send-data permission first.");

            // Cheapest and most basic blocker first: a server without a staged package (the state until the
            // first probe-v* release) answers 503 before anything else is looked at.
            if (string.IsNullOrEmpty(_environment.WebRootPath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, LinuxProbeInstallerBundle.NotStagedMessage);

            var stagingDir = Path.Combine(_environment.WebRootPath, LinuxProbeInstallerBundle.StagingFolder);
            string packageName;
            try
            {
                packageName = LinuxProbeInstallerBundle.SelectStagedPackage(Directory.EnumerateFiles(stagingDir));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                packageName = null;
            }

            if (packageName is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, LinuxProbeInstallerBundle.NotStagedMessage);

            var (address, port) = AgentConnectionResolver.Resolve(
                _config.Agent.ExternalConnectionUrl, _config.Kestrel.SensorPort, Request.Scheme, Request.Host.Host);

            var addressError = LinuxProbeInstallerBundle.ValidateServerAddress(address);
            if (addressError is not null)
                return BadRequest(addressError);

            // A TLS handshake feature on this connection means Kestrel itself terminated TLS with its own
            // certificate; behind a TLS-terminating proxy it is absent and no CA file is shipped.
            // The certificate instance Kestrel installed at startup, read before IsBundledDefault, which
            // its loading sets: a certificate saved in settings but not yet applied by a restart is ignored.
            var (caDecision, serverCa) = LinuxProbeServerCa.Resolve(
                HttpContext.Features.Get<ITlsHandshakeFeature>() is not null,
                () => (_config.ServerCertificate.Certificate, _config.ServerCertificate.IsBundledDefault));
            if (caDecision == LinuxProbeCaDecision.RefuseBundledDefault)
                return BadRequest(LinuxProbeServerCa.BundledDefaultMessage);
            if (caDecision == LinuxProbeCaDecision.OmitUnreadable)
                _logger.Warn($"Linux probe bundle for product '{product.DisplayName}' ({productId}) ships without server-ca.pem: the server certificate could not be read, so a probe will only connect if that certificate is already trusted on its host.");

            byte[] package;
            try
            {
                package = System.IO.File.ReadAllBytes(Path.Combine(stagingDir, packageName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, LinuxProbeInstallerBundle.NotStagedMessage);
            }

            var bundle = LinuxProbeInstallerBundle.BuildTarGz(
                LinuxProbeInstallerBundle.BundleFolderName(product.DisplayName), packageName, package,
                new LinuxProbeBundleOptions(address, port, key.Id.ToString(), serverCa));

            _logger.Info($"{CurrentUser?.Name} downloaded the HSM Linux probe bundle for product '{product.DisplayName}' ({productId}).");

            return File(bundle, "application/gzip", LinuxProbeInstallerBundle.BundleFileName(product.DisplayName));
        }


        /// <summary>
        /// Agent self-update manifest (epic #1174). Returns the version, SHA-256, and whether the
        /// server permits auto-updates. No authentication — version info is not sensitive and agents
        /// need to poll this before they have a user context. Returns 503 when no exe is staged.
        /// </summary>
        [HttpGet("version")]
        [AllowAnonymous]
        public IActionResult GetVersion()
        {
            if (string.IsNullOrEmpty(_environment.WebRootPath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent binary not staged.");

            var exePath = Path.Combine(_environment.WebRootPath, "agent", AgentInstallerBundle.ExeName);

            // Read + hash in one open call — no separate File.Exists to avoid TOCTOU.
            string sha256;
            try
            {
                sha256 = ComputeSha256Hex(exePath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent binary not staged.");
            }

            var version = ReadStagedVersion(_environment.WebRootPath);

            return Ok(new
            {
                version,
                sha256,
                updateEnabled = _config.Agent.AutoUpdateEnabled,
            });
        }


        /// <summary>
        /// Serve the staged agent binary for self-update (epic #1174). Requires a valid product access
        /// key in the <c>Key</c> request header — the agent sends the same key it uses for sensor data.
        /// Sets <c>X-Agent-Sha256</c> on the response so the caller can verify integrity without a
        /// second round-trip.
        /// </summary>
        [HttpGet("exe")]
        [AllowAnonymous]
        public IActionResult GetExe()
        {
            // Validate the Key header — same GUID-based access-key the agent sends for sensor data.
            var keyHeader = Request.Headers["Key"].ToString();
            if (!Guid.TryParse(keyHeader, out var keyGuid) || !_cache.TryGetKey(keyGuid, out _, out _))
                return Unauthorized("A valid product access key is required in the 'Key' header.");

            if (string.IsNullOrEmpty(_environment.WebRootPath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent binary not staged.");

            var exePath = Path.Combine(_environment.WebRootPath, "agent", AgentInstallerBundle.ExeName);

            string sha256;
            try
            {
                sha256 = ComputeSha256Hex(exePath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent binary not staged.");
            }

            Response.Headers["X-Agent-Sha256"] = sha256;
            return PhysicalFile(exePath, "application/octet-stream", AgentInstallerBundle.ExeName);
        }


        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "product";

            var sanitized = name.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
                sanitized = sanitized.Replace(invalid, '_');

            return sanitized.Replace(' ', '_');
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// Read `wwwroot/agent/version.txt` (one-line version string staged by CI). Falls back to
        /// "0.0.0" so the manifest stays valid even when the version file was not staged yet.
        private static string ReadStagedVersion(string webRootPath)
        {
            var versionPath = Path.Combine(webRootPath, "agent", "version.txt");
            try
            {
                var text = System.IO.File.ReadAllText(versionPath).Trim();
                return string.IsNullOrEmpty(text) ? "0.0.0" : text;
            }
            catch
            {
                return "0.0.0";
            }
        }
    }
}
