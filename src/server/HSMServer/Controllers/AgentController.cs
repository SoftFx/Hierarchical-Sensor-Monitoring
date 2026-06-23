using HSMCommon.Constants;
using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.Agent;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;

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
        // The agent registers its default sensors at start and streams values, so its key needs both
        // the add-node/add-sensor and send-data permissions. The product DefaultKey has all of these.
        private const KeyPermissions AgentPermissions =
            KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors;

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

            var key = SelectAgentKey(product);
            if (key is null)
                return BadRequest("This product has no usable access key. Create one with send-data permission first.");

            var exePath = Path.Combine(_environment.WebRootPath ?? string.Empty, "agent", AgentInstallerBundle.ExeName);
            if (!System.IO.File.Exists(exePath))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    "The agent binary is not available on this server yet. Publish hsm-agent.exe to wwwroot/agent/ (CI packaging, W8).");

            var (address, port) = ResolveConnection();
            var options = new AgentBundleOptions(address, port, key.Id.ToString(), AllowUntrustedCertificate: false);
            var zip = AgentInstallerBundle.BuildZip(System.IO.File.ReadAllBytes(exePath), options);

            _logger.Info($"{CurrentUser?.Name} downloaded the HSM Agent bundle for product '{product.DisplayName}' ({productId}).");

            return File(zip, "application/zip", $"hsm-agent-{Sanitize(product.DisplayName)}.zip");
        }


        private (string address, int port) ResolveConnection()
        {
            var external = _config.Agent.ExternalConnectionUrl;
            if (!string.IsNullOrWhiteSpace(external))
            {
                var raw = external.Trim();
                if (!raw.Contains("://"))
                    raw = $"https://{raw}";

                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                {
                    var port = uri.IsDefaultPort ? _config.Kestrel.SensorPort : uri.Port;
                    return ($"{uri.Scheme}://{uri.Host}", port);
                }
            }

            // No external URL configured: best-effort fallback to the request host + the Sensor port.
            return ($"{Request.Scheme}://{Request.Host.Host}", _config.Kestrel.SensorPort);
        }

        private static AccessKeyModel SelectAgentKey(ProductModel product)
        {
            var keys = product.AccessKeys.Values;

            // Prefer the product's DefaultKey (it carries full permissions and never expires).
            foreach (var key in keys)
                if (key.DisplayName == CommonConstants.DefaultAccessKey && key.IsValid(AgentPermissions, out _))
                    return key;

            // Otherwise any valid key with the permissions the agent needs.
            var valid = keys.FirstOrDefault(k => k.IsValid(AgentPermissions, out _));
            if (valid is not null)
                return valid;

            // Last resort: the DefaultKey regardless of state, else any key (the admin can fix it up).
            return keys.FirstOrDefault(k => k.DisplayName == CommonConstants.DefaultAccessKey) ?? keys.FirstOrDefault();
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
