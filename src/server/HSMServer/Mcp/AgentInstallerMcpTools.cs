using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.AgentInstaller;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The agent-installer tool of the MCP surface (#1788): issue a ONE-TIME
    /// short-lived download link for a product's installer bundle — a thin
    /// rendering of <see cref="AgentInstallerAdministrationService"/>, the
    /// same engine the REST controller runs on (the #1393 pattern).
    /// </summary>
    // The orchestrator's hand-over step (ADR docs/adr/0001-provision-not-push.md):
    // the returned URL needs no Authorization header — the orchestrator curls
    // it on the TARGET MACHINE without the raw management bearer. Admission
    // (ProductManager owner; a read-only token suffices) and the bundle build
    // live in the shared service; the link URL is built from THIS request's
    // host, which the streamable-HTTP transport keeps inline in the tool's
    // own POST.
    [McpServerToolType]
    public sealed class AgentInstallerMcpTools
    {
        private readonly AgentInstallerAdministrationService _installer;
        private readonly IHttpContextAccessor _http;

        public AgentInstallerMcpTools(AgentInstallerAdministrationService installer, IHttpContextAccessor http)
        {
            _installer = installer;
            _http = http;
        }


        // ReadOnly=false / Idempotent=false: every call MINTS a fresh
        // single-use link (server-side state that lives until it expires) —
        // a retry issues a NEW credential link, like the creates (#1797
        // review r3).
        [McpServerTool(Name = "get_agent_installer", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Issues a ONE-TIME short-lived (~10 min) download link for a product's HSM agent installer bundle ('windows' zip with the signed exe, or 'linux' tar.gz with the .deb probe). The bundle embeds the product's existing send-data access key in its generated config — it never creates one (a product without a usable key answers a field-keyed error; create_access_key fixes it). The returned url works WITHOUT an Authorization header (the token inside it is the single-use credential): fetch it on the target machine, verify the bytes against sha256, unpack, run the install script. Requires the token owner's ProductManager role and a READ-WRITE token (/mcp is POST-only; read-only tokens cannot reach any tool).")]
        public AgentInstallerLinkDto GetAgentInstaller(
            [Description("Product id the bundle is for (any visible node works — sight is resolved at the node's root product).")] Guid productId,
            [Description("The target machine's OS: 'windows' or 'linux'.")] string os)
        {
            var context = _http.HttpContext
                ?? throw new ModelContextProtocol.McpException("No HTTP context is available for this tool call.");

            return McpToolErrors.Unwrap(_installer.IssueLink(productId, os,
                InstallerRequestContext.Of(User, context)));
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
