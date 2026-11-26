using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Threading;
using HSMServer.Model.ManagementApi.Chats;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The chat tools of the MCP surface: the id source an agent needs to
    /// compose destination.chats when creating or editing alert policies and
    /// alert templates (a thin rendering of <see cref="ChatsReadService"/>,
    /// the #1393 pattern: one visibility rule behind both transports), plus
    /// the verification pair of #1826 — a test message that answers its
    /// delivery outcome and the one-shot Telegram invitation issuer.
    /// </summary>
    // The same bearer token, the same caller-wide gate and folder sight, the
    // same DTOs as the REST surface; expected failures surface as tool errors
    // (isError) for the calling agent to self-correct, never as
    // protocol-level crashes. Not all read-only: the verification pair writes
    // (ReadOnly = false) and the invitation starts a Telegram binding. The
    // rest of the chat lifecycle (create/edit/delete, webhook changes) stays
    // web-UI-only until #1829.
    [McpServerToolType]
    public sealed class ChatsMcpTools
    {
        private readonly ChatsReadService _reader;
        private readonly ChatVerificationService _verification;
        private readonly IHttpContextAccessor _http;

        public ChatsMcpTools(ChatsReadService reader, ChatVerificationService verification, IHttpContextAccessor http)
        {
            _reader = reader;
            _verification = verification;
            _http = http;
        }


        [McpServerTool(Name = "list_chats", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
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


        [McpServerTool(Name = "get_chat", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Gets one notification chat by id (same caller-wide gate as the list). Unknown and invisible ids answer the same error.")]
        public ChatDto GetChat(
            [Description("Chat id.")] Guid chatId) =>
            McpToolErrors.Unwrap(_reader.GetChat(chatId, User));

        [McpServerTool(Name = "send_chat_test_message", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Sends a test message to one of a chat's channels and answers the delivery outcome: `delivered`, or the error text the channel reported (never a response body) — the proof that a webhook or a Telegram binding works. The channel must be one the chat carries (see get_chat's flags).")]
        public async Task<ChatTestMessageResultDto> SendChatTestMessage(
            [Description("Chat id.")] Guid chatId,
            [Description("The delivery channel to test: \"telegram\", \"slack\" or \"mattermost\".")] string channel) =>
            McpToolWriteErrors.Unwrap(await _verification.SendTestMessageAsync(chatId, channel, User));

        [McpServerTool(Name = "create_chat_telegram_invitation", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Issues a ONE-SHOT Telegram invitation for a chat, valid 10 minutes: the link for a direct chat or the /start@bot command for a group, plus the steps a PERSON performs inside Telegram — the orchestrator cannot press Start itself. The binding lands when that person completes it; read get_chat's telegramChatId afterwards. Requires the Telegram bot to be running (see update_telegram_bot).")]
        public async Task<ChatInvitationDto> CreateChatTelegramInvitation(
            [Description("Chat id.")] Guid chatId,
            [Description("The invitation kind: \"direct\" (a link to open and press Start) or \"group\" (a command to post into the group after adding the bot with admin rights).")] string kind) =>
            McpToolWriteErrors.Unwrap(await _verification.CreateInvitationAsync(chatId, kind, User));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
