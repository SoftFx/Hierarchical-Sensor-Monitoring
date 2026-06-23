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

namespace HSMServer.Controllers
{
    /// <summary>
    /// Per-product HSM Agent download (epic #1167, W6/W7). The admin opens a product, clicks "Download
    /// Windows agent", and gets a zip with the byte-identical signed exe + a generated config.json
    /// (this server's address + the product's access key) + silent install scripts. The server ONLY
    /// generates + serves the zip — it is not an installer; the client install is the C++ exe's own
    /// <c>--install</c> (no .NET/MSI on the client).
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
            var options = new AgentBundleOptions(address, port, key.Id.ToString(), _config.Agent.AllowUntrustedCertificate);
            var zip = AgentInstallerBundle.BuildZip(exeBytes, options);

            _logger.Info($"{CurrentUser?.Name} downloaded the HSM Agent bundle for product '{product.DisplayName}' ({productId}).");

            return File(zip, "application/zip", $"hsm-agent-{Sanitize(product.DisplayName)}.zip");
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
    }
}
