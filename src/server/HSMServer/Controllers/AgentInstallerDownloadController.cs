using System;
using HSMServer.Model.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The one-time-link redemption endpoint of the agent-installer surface
    /// (#1788): serves the bundle bytes to whoever holds the link — no
    /// Authorization header (the /api/v1 area's bearer policy cannot admit a
    /// tokenless request, so the redemption lives here, beside the agent
    /// self-update endpoints that authenticate by the Key header the same
    /// way). The token in the URL IS the credential: single use, ~10 minutes.
    /// </summary>
    // The link substitutes the management bearer for exactly one download —
    // ADR docs/adr/0001-provision-not-push.md: the orchestrator never needs
    // the raw token on the target machine. An unknown, expired or
    /// already-redeemed token answers one plain 404 (the link must not become
    // a reuse or enumeration oracle).
    [Route("api/agent")]
    public sealed class AgentInstallerDownloadController : ControllerBase
    {
        private readonly AgentInstallerDownloadLinks _links;

        public AgentInstallerDownloadController(AgentInstallerDownloadLinks links)
        {
            _links = links;
        }


        [HttpGet("installer-download")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // the bundle carries a bearer key
        public IActionResult Download(Guid token)
        {
            var link = token == Guid.Empty ? null : _links.TryRedeem(token);

            return link is null
                ? NotFound("The download link is unknown, expired or already used.")
                : File(link.Bundle, link.ContentType, link.FileName);
        }
    }
}
