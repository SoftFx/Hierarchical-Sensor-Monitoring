using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.Agent;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
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
            if (!System.IO.File.Exists(exePath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent binary not staged.");

            var sha256 = ComputeSha256Hex(exePath);
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
            if (!System.IO.File.Exists(exePath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent binary not staged.");

            var sha256 = ComputeSha256Hex(exePath);
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
