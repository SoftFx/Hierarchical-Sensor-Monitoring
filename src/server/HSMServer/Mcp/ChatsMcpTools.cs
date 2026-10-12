using System;
using System.ComponentModel;
using System.Security.Claims;
using System.Threading;
using HSMServer.Model.ManagementApi.Chats;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The two chat tools of the MCP surface: the id source an agent needs to
    /// compose destination.chats when creating or editing alert policies and
    /// alert templates — a thin rendering of <see cref="ChatsReadService"/>
    /// (the #1393 pattern: one visibility rule behind both transports).
    /// </summary>
    // The same bearer token, the same caller-wide gate and folder sight, the
    // same DTOs as the REST surface; expected failures surface as tool errors
    // (isError) for the calling agent to self-correct, never as
    // protocol-level crashes. Read-only like every tool of the current
    // surface: chat lifecycle (create/edit/delete, webhook and Telegram
    // bindings) stays web-UI-only.
    [McpServerToolType]
    public sealed class ChatsMcpTools
    {
        private readonly ChatsReadService _reader;
        private readonly IHttpContextAccessor _http;

        public ChatsMcpTools(ChatsReadService reader, IHttpContextAccessor http)
        {
            _reader = reader;
            _http = http;
        }


        [McpServerTool(Name = "list_chats", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists notification chats visible to the token's owner, ordered by name — the valid destination chat ids for alert policies and templates. A chat with an empty `folders` list is global (usable everywhere); a chat bound to folders is usable only from the folders listed (invisible folders are omitted). Webhook URLs are secrets and never exposed, only hasSlackWebhook/hasMattermostWebhook flags; Telegram/author detail fields are null unless the owner is an admin or a manager of a bound folder. Chats have no narrowing dimension, so `page` walks beyond the limit.")]
        public McpChatsResult ListChats(
            [Description("Maximum chats to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1,
            CancellationToken cancellationToken = default)
        {
            var result = McpToolErrors.Unwrap(_reader.ListChats(User, HsmMcp.NormalizePage(page),
                HsmMcp.NormalizeLimit(limit), cancellationToken));

            return new McpChatsResult
            {
                Chats = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }


        [McpServerTool(Name = "get_chat", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one notification chat by id (same caller-wide gate as the list). Unknown and invisible ids answer the same error.")]
        public ChatDto GetChat(
            [Description("Chat id.")] Guid chatId) =>
            McpToolErrors.Unwrap(_reader.GetChat(chatId, User));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
