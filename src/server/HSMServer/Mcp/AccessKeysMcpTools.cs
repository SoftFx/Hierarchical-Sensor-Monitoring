using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.AccessKeys;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The access-key tools of the MCP surface (#1787/#1789): one product's
    /// keys (the id-discovery list) and the credential-lifecycle writes —
    /// thin renderings of <see cref="AccessKeysReadService"/> and
    /// <see cref="AccessKeysAdministrationService"/>, the same engines the
    /// REST controller runs on (the #1393 pattern).
    /// </summary>
    // The id-discovery surface of the agent-installation scenario: the listed
    // key ids are the handles the write tools take, and each item's
    // lastUseTime/lastIp answer "has the installed agent connected" without
    // any new endpoint. The key id IS the credential and travels deliberately
    // (web parity — see the DTO's remarks); sight, the 404 uniformity, the
    // master-key exclusion and the ProductManager write gate all live in the
    // shared services — the tools are the adapter. Read-write enforcement
    // needs no tool-level check: /mcp serves every message via POST, and the
    // HsmApiToken management policy rejects read-only tokens on unsafe
    // methods outright.
    [McpServerToolType]
    public sealed class AccessKeysMcpTools
    {
        private readonly AccessKeysReadService _reader;
        private readonly AccessKeysAdministrationService _writer;
        private readonly IHttpContextAccessor _http;

        public AccessKeysMcpTools(AccessKeysReadService reader, AccessKeysAdministrationService writer,
            IHttpContextAccessor http)
        {
            _reader = reader;
            _writer = writer;
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


        // Idempotent=false: a create — re-sending it after a network retry
        // would issue a SECOND credential (or answer the duplicate-name
        // error).
        [McpServerTool(Name = "create_access_key", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Issues an access key on a product — the credential hand-over step of installing an HSM agent (the response's key id IS the credential). The write needs the token owner's ProductManager role. Permissions default to the agent profile ('canSendSensorData', 'canAddNodes', 'canAddSensors') when omitted; at least one when given. The display name: 1..100 chars, no control/bidi characters, unique among the product's keys. The response echoes the created key.")]
        public AccessKeyDto CreateAccessKey(
            [Description("Product id to issue the key on (any visible node works — the key attaches to that node).")] Guid productId,
            [Description("The new key's display name: 1..100 characters, unique among the product's keys.")] string displayName,
            [Description("The key's permission names ('canSendSensorData', 'canAddNodes', 'canAddSensors', 'canReadSensorData'); at least one. Omitted = the agent profile (send data, add nodes, add sensors).")] string[] permissions = null) =>
            McpToolWriteErrors.Unwrap(_writer.CreateAccessKey(productId,
                new CreateAccessKeyRequestDto { DisplayName = displayName, Permissions = permissions }, User));

        // Idempotent=true: the retry-safe state-set writes — re-sending the
        // same partial update converges on the same key.
        [McpServerTool(Name = "update_access_key", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Modifies one key of a product: rename, re-permission, block or unblock (the kill-switch for a misbehaving agent — its key is refused, the data keeps flowing elsewhere). Every argument is optional — omit what stays unchanged. Unblocking a key whose expiration has passed lands on 'expired'. The write needs the token owner's ProductManager role. The response echoes the updated key.")]
        public AccessKeyDto UpdateAccessKey(
            [Description("Product id the key belongs to.")] Guid productId,
            [Description("Key id (from list_access_keys).")] Guid keyId,
            [Description("Optional new display name (same rules as on create).")] string displayName = null,
            [Description("Optional new permission names (at least one when given).")] string[] permissions = null,
            [Description("Optional new state: 'active' (unblock) or 'blocked'.")] string state = null) =>
            McpToolWriteErrors.Unwrap(_writer.UpdateAccessKey(productId, keyId,
                new UpdateAccessKeyRequestDto { DisplayName = displayName, Permissions = permissions, State = state }, User));

        // Idempotent=false: a delete — re-sending it after a network retry
        // answers the not-found error for an already-removed key.
        [McpServerTool(Name = "delete_access_key", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes one key of a product — the credential stops being accepted on its next presentation. The write needs the token owner's ProductManager role.")]
        public McpDeletedResult DeleteAccessKey(
            [Description("Product id the key belongs to.")] Guid productId,
            [Description("Key id (from list_access_keys).")] Guid keyId)
        {
            McpToolWriteErrors.Unwrap(_writer.DeleteAccessKey(productId, keyId, User));

            return new McpDeletedResult { Id = keyId };
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
