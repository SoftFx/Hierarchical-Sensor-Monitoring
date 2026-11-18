using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.AccessKeys;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The access-key list tool of the MCP surface (#1787): one product's
    /// access keys — a thin rendering of <see cref="AccessKeysReadService"/>,
    /// the same engine the REST controller runs on (the #1393 pattern).
    /// </summary>
    // The id-discovery surface of the agent-installation scenario: the listed
    // key ids are the handles the write tools (#1789) will take, and each
    // item's lastUseTime/lastIp answer "has the installed agent connected"
    // without any new endpoint. The key id IS the credential and travels
    // deliberately (web parity — see the DTO's remarks); sight, the 404
    // uniformity and the master-key exclusion all live in the read service,
    // shared with REST — the tool is the adapter.
    [McpServerToolType]
    public sealed class AccessKeysMcpTools
    {
        private readonly AccessKeysReadService _reader;
        private readonly IHttpContextAccessor _http;

        public AccessKeysMcpTools(AccessKeysReadService reader, IHttpContextAccessor http)
        {
            _reader = reader;
            _http = http;
        }


        [McpServerTool(Name = "list_access_keys", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists one product's access keys (the credentials HSM agents send in the 'Key' header), ordered by display name then id. The key id IS the credential itself — exposed deliberately (web parity). Server-wide master keys never appear. An item's lastUseTime/lastIp answer 'has the installed agent connected'. Unknown/invisible product ids answer a tool error (indistinguishable).")]
        public McpAccessKeysResult ListAccessKeys(
            [Description("Product id whose keys to list (any visible node works — sight is resolved at the node's root product).")] Guid productId,
            [Description("Maximum keys to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1)
        {
            var result = McpToolErrors.Unwrap(_reader.ListProductAccessKeys(User, productId,
                HsmMcp.NormalizePage(page), HsmMcp.NormalizeLimit(limit)));

            return new McpAccessKeysResult
            {
                AccessKeys = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
