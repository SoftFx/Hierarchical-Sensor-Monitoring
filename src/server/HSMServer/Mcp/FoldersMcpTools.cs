using System;
using System.ComponentModel;
using System.Security.Claims;
using System.Threading;
using HSMServer.Model.ManagementApi.Folders;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The two folder tools of the MCP surface: the folderId source an agent
    /// needs when creating or editing alert templates (the entity that scopes
    /// them and binds notification chats) — a thin rendering of
    /// <see cref="FoldersReadService"/> (the #1393 pattern: one visibility
    /// rule behind both transports).
    /// </summary>
    // The same bearer token, the same folder sight, the same DTOs as the REST
    // surface; expected failures surface as tool errors (isError) for the
    // calling agent to self-correct, never as protocol-level crashes. Note
    // the entity is the access-grouping folder, not the tree's nested
    // products (get_node serves those). Read-only: folder CRUD stays
    // web-UI-only.
    [McpServerToolType]
    public sealed class FoldersMcpTools
    {
        private readonly FoldersReadService _reader;
        private readonly IHttpContextAccessor _http;

        public FoldersMcpTools(FoldersReadService reader, IHttpContextAccessor http)
        {
            _reader = reader;
            _http = http;
        }


        [McpServerTool(Name = "list_folders", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists access-grouping folders visible to the token's owner, ordered by name — the valid folderId values for alert templates (NOT the tree's nested products; get_node serves those). Each folder carries its products, bound chat ids, default-chats routing and retention defaults. Folders have no narrowing dimension, so `page` walks beyond the limit.")]
        public McpFoldersResult ListFolders(
            [Description("Maximum folders to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1,
            CancellationToken cancellationToken = default)
        {
            var result = _reader.ListFolders(User, HsmMcp.NormalizePage(page), HsmMcp.NormalizeLimit(limit),
                cancellationToken);

            return new McpFoldersResult
            {
                Folders = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }


        [McpServerTool(Name = "get_folder", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one access-grouping folder by id — products, bound chats, default-chats routing and retention defaults. Unknown and invisible ids answer the same error.")]
        public FolderDto GetFolder(
            [Description("Folder id.")] Guid folderId) =>
            McpToolErrors.Unwrap(_reader.GetFolder(folderId, User));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
