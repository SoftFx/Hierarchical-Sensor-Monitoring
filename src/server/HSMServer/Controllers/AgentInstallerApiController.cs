using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AgentInstaller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The agent-installer download surface of the management API (#1788): the
    /// bundle (zip for windows, tar.gz for linux) under the management bearer,
    /// either streamed directly (scripts) or handed over as a ONE-TIME
    /// short-lived download link (MCP-native orchestrators — the link needs no
    /// Authorization header on the target machine). The bundle embeds the
    /// product's existing send-data access key; it never generates one.
    /// </summary>
    // Admission (ProductManager owner, read-write OR read-only token — the
    // initiative's decision #5) and the build live in
    // AgentInstallerAdministrationService, shared with the MCP tool. The
    // not-staged binary answers the area's 503; the request's own scheme/host
    // drive the address resolution and the link URL (never the agent
    // ExternalConnectionUrl — that is the data channel).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/products/{productId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // the bundle carries a bearer key
    public sealed class AgentInstallerApiController : ControllerBase
    {
        private readonly AgentInstallerAdministrationService _installer;

        public AgentInstallerApiController(AgentInstallerAdministrationService installer)
        {
            _installer = installer;
        }


        /// <summary>
        /// Stream the product's agent-installer bundle: a signed-exe zip for
        /// `os=windows`, a .deb tar.gz for `os=linux` — the same bundle the web
        /// Downloads page serves, with the product's send-data key embedded in
        /// the generated config. A read-only token suffices (the ProductManager
        /// owner can already read the key).
        /// </summary>
        /// <param name="productId">Product id (any visible node works — sight is resolved at the node's root product).</param>
        /// <param name="os">'windows' or 'linux'.</param>
        [HttpGet("agent-installer")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult GetInstaller(Guid productId, string os)
        {
            var result = _installer.GetBundle(productId, os, InstallerRequestContext.Of(User, HttpContext));

            return result.Success
                ? File(result.Value.Bundle, result.Value.ContentType, result.Value.FileName)
                : result.ToActionResult();
        }

        /// <summary>
        /// Issue a ONE-TIME short-lived (~10 min) download link for the product's
        /// bundle: `{url, expiresAt, fileName, size, sha256}`. The URL needs no
        /// Authorization header — the token inside it is the credential, valid
        /// for a single download — so an orchestrator can curl the bundle on the
        /// target machine without the management bearer. The sha256 pins the
        /// EXACT bytes the redemption serves.
        /// </summary>
        /// <param name="productId">Product id (any visible node works — sight is resolved at the node's root product).</param>
        /// <param name="os">'windows' or 'linux'.</param>
        [HttpPost("agent-installer/link")]
        [ProducesResponseType(typeof(AgentInstallerLinkDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult IssueInstallerLink(Guid productId, string os) =>
            _installer.IssueLink(productId, os, InstallerRequestContext.Of(User, HttpContext)).ToActionResult();
    }
}
