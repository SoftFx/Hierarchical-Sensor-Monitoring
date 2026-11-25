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
    /// the #1393 pattern: one visibility rule behind both transports), the
    /// verification pair of #1826 — a test message that answers its
    /// delivery outcome and the one-shot Telegram invitation issuer — and the
    /// write trio of #1829 (a thin rendering of <see cref="ChatWriteService"/>,
    /// on the write engine the web editor runs): create, update and delete.
    /// </summary>
    // The same bearer token, the same caller-wide gate and folder sight, the
    // same DTOs as the REST surface; expected failures surface as tool errors
    // (isError) for the calling agent to self-correct, never as
    // protocol-level crashes. Not all read-only: the verification pair writes
    // (ReadOnly = false), the invitation starts a Telegram binding, and the
    // write trio changes the chats. Read-write enforcement needs no tool-level
    // check: /mcp serves every message via POST, and the HsmApiToken
    // management policy rejects read-only tokens on unsafe methods outright.
    [McpServerToolType]
    public sealed class ChatsMcpTools
    {
        private readonly ChatsReadService _reader;
        private readonly ChatVerificationService _verification;
        private readonly ChatWriteService _writer;
        private readonly IHttpContextAccessor _http;

        public ChatsMcpTools(ChatsReadService reader, ChatVerificationService verification, ChatWriteService writer,
            IHttpContextAccessor http)
        {
            _reader = reader;
            _verification = verification;
            _writer = writer;
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

        // Idempotent=false: a create — re-sent after a network retry it would
        // answer the duplicate-name error (or make a second chat).
        [McpServerTool(Name = "create_chat", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Creates a notification chat — ADMIN tokens only: a chat created with an attacker-supplied webhook would exfiltrate every alert sent to it. Optionally with a Slack and/or Mattermost webhook and folder bindings; a Telegram binding is added afterwards by a person's /start against an invitation (create_chat_telegram_invitation). Webhook URLs are write-only: the response answers hasSlackWebhook/hasMattermostWebhook, never the URL. The response echoes the created chat.")]
        public async Task<ChatDto> CreateChat(
            [Description("Display name: 1..100 characters, unique per server ignoring case, without control or bidirectional-override characters.")] string name,
            [Description("Optional free-form description.")] string description = null,
            [Description("Whether the chat receives messages; true when omitted.")] bool? sendMessages = null,
            [Description("Delay in seconds that messages of one sensor are aggregated for before sending: 0 sends every message at once, at most 86400 (a day); 60 when omitted.")] int? messagesAggregationTimeSec = null,
            [Description("Optional Slack incoming-webhook URL (absolute http or https) — write-only, never echoed.")] string slackWebhookUrl = null,
            [Description("Optional Mattermost incoming-webhook URL (absolute http or https) — write-only, never echoed.")] string mattermostWebhookUrl = null,
            [Description("Optional ids of the folders to bind the chat to; each must be a folder the token's owner manages. Omitted: a global chat, usable from every folder.")] Guid[] folders = null) =>
            McpToolWriteErrors.Unwrap(await _writer.CreateChat(new CreateChatRequestDto
            {
                Name = name,
                Description = description,
                SendMessages = sendMessages,
                MessagesAggregationTimeSec = messagesAggregationTimeSec,
                SlackWebhookUrl = slackWebhookUrl,
                MattermostWebhookUrl = mattermostWebhookUrl,
                Folders = folders is null ? null : [.. folders],
            }, User));

        // Idempotent=true: the retry-safe state-set write — re-sending the
        // same partial update converges on the same chat.
        [McpServerTool(Name = "update_chat", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Changes a notification chat — every argument is optional, an omitted one stays unchanged. A webhook or the Telegram binding is removed only by its clear flag; an empty webhook value changes nothing. `folders` is the complete list of bindings among the folders the token's owner manages: a managed binding not listed is removed, a binding to a folder the owner cannot manage stays, every listed id must be a folder the owner manages; omitted: the bindings stay as they are. Needs an admin token, or the manager of a folder the chat is bound to. Webhook URLs are write-only. The response echoes the stored chat.")]
        public async Task<ChatDto> UpdateChat(
            [Description("Chat id.")] Guid chatId,
            [Description("Optional new display name (the rules of create_chat apply; the chat's own name in another letter case is allowed).")] string name = null,
            [Description("Optional new description; an empty string clears it.")] string description = null,
            [Description("Whether the chat receives messages.")] bool? sendMessages = null,
            [Description("Delay in seconds that messages of one sensor are aggregated for before sending: 0 sends every message at once, at most 86400 (a day).")] int? messagesAggregationTimeSec = null,
            [Description("Optional new Slack incoming-webhook URL (absolute http or https) — write-only, never echoed.")] string slackWebhookUrl = null,
            [Description("Optional new Mattermost incoming-webhook URL (absolute http or https) — write-only, never echoed.")] string mattermostWebhookUrl = null,
            [Description("Remove the Slack webhook.")] bool? clearSlackWebhook = null,
            [Description("Remove the Mattermost webhook.")] bool? clearMattermostWebhook = null,
            [Description("Remove the Telegram binding (a new one is bound only by a person's /start against an invitation).")] bool? clearTelegramBinding = null,
            [Description("Optional complete list of the folders to bind the chat to — see the tool description; omitted: unchanged.")] Guid[] folders = null) =>
            McpToolWriteErrors.Unwrap(await _writer.UpdateChat(chatId, new UpdateChatRequestDto
            {
                Name = name,
                Description = description,
                SendMessages = sendMessages,
                MessagesAggregationTimeSec = messagesAggregationTimeSec,
                SlackWebhookUrl = slackWebhookUrl,
                MattermostWebhookUrl = mattermostWebhookUrl,
                ClearSlackWebhook = clearSlackWebhook,
                ClearMattermostWebhook = clearMattermostWebhook,
                ClearTelegramBinding = clearTelegramBinding,
                Folders = folders is null ? null : [.. folders],
            }, User));

        // Idempotent=false: a delete — re-sent after a network retry it answers
        // the not-found error for an already-removed chat.
        [McpServerTool(Name = "delete_chat", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes a notification chat — the same removal the web UI's list runs: the chat's references are pruned from folders and alert policies, and the write leaves a journal record. Needs an admin token, or the manager of a folder the chat is bound to. Use dryRun first to learn how many sensors' alerts reference the chat and which folder bindings the deletion would take with it.")]
        public async Task<McpChatDeleteResult> DeleteChat(
            [Description("Chat id.")] Guid chatId,
            [Description("Preview the deletion — how many sensors' alerts reference the chat and which folder bindings it would take with it — WITHOUT removing anything; the answer carries `preview` instead of `value`.")] bool dryRun = false)
        {
            var result = await _writer.DeleteChat(chatId, User, dryRun);

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = (ChatDeletePreviewDto)result.Preview }
                : new() { Value = new McpDeletedResult { Id = chatId } };
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
