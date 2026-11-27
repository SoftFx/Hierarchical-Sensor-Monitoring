using HSMServer.Authentication;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.Authentication;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The chat verification surface (#1826): a test message that answers its delivery
    /// outcome, and the Telegram invitation issuer for the management surface. Transport-
    /// agnostic — the REST controller and the MCP tools both call it; the caller supplies
    /// the token principal and gets the area's write envelope back, never an IActionResult.
    /// Every answer carries the next steps of the setup (#1831).
    /// </summary>
    // Admission mirrors the web actions' gate (TelegramRoleFilterById): the chat's detail
    // rule — an admin or the manager of a folder the chat is bound to. A global chat is
    // therefore admin-only, exactly like the web editor.
    public sealed class ChatVerificationService
    {
        // The relay server -> orchestrator -> person is slower than the web UI's
        // issue-at-the-moment-of-use; 10 minutes is the decision of the initiative
        // (docs/initiatives/chat-setup-via-mcp.md), the installer-link precedent.
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(10);

        private const string UnknownChannelMessage = "Unknown channel '{0}'.";
        private const string ChannelNotCarriedMessage = "The chat has no {0} configured.";

        // The remedy names the tool that starts the bot, and where the steps of its credential are.
        private static readonly string BotNotRunningMessage =
            "The Telegram bot is not running. Pass the bot token to update_telegram_bot: a token change starts the bot. " +
            "If the token is stored already, restart the bot on the web Configuration page. " +
            $"The steps of the credential: {ChatSetupNextSteps.Guide(ChatSetupChannel.TelegramBot)}.";

        private readonly IChatsManager _chats;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly IChatDelivery _delivery;
        private readonly IServerConfig _config;


        public ChatVerificationService(IChatsManager chats, IApiTokenAuthorizationService authorization,
            IUserManager users, IChatDelivery delivery, IServerConfig config)
        {
            _chats = chats;
            _authorization = authorization;
            _users = users;
            _delivery = delivery;
            _config = config;
        }


        public async Task<PolicyWriteResult<ChatTestMessageResultDto>> SendTestMessageAsync(Guid chatId, string channel, ClaimsPrincipal caller)
        {
            // The storage indexer answers null for an unknown id — the area's dictionary
            // semantics (ChatsReadService uses the same lookup shape).
            var chat = _chats[chatId];

            // The area's anti-enumeration split: an INVISIBLE chat answers the same 404 as an
            // unknown one (global or bound to a folder the caller can see); only a visible
            // chat the caller may not operate on answers 403.
            if (chat is null || !IsChatVisible(caller, chat))
                return Fail<ChatTestMessageResultDto>(PolicyWriteOutcome.NotFound);

            if (!CanOperate(caller, chat))
                return Fail<ChatTestMessageResultDto>(PolicyWriteOutcome.Forbidden);

            if (!TryChannel(channel, out var parsed))
                return Fail<ChatTestMessageResultDto>(PolicyWriteOutcome.Invalid, field: "channel", message: string.Format(UnknownChannelMessage, channel));

            var carried = parsed switch
            {
                ChatChannel.Telegram => chat.TelegramChatId is not null,
                ChatChannel.Slack => !string.IsNullOrEmpty(chat.SlackWebhookUrl),
                ChatChannel.Mattermost => !string.IsNullOrEmpty(chat.MattermostWebhookUrl),
                _ => false,
            };

            if (!carried)
                return Fail<ChatTestMessageResultDto>(PolicyWriteOutcome.Invalid, field: "channel", message: string.Format(ChannelNotCarriedMessage, parsed));

            var result = await _delivery.SendTestAsync(chat, parsed);

            return PolicyWriteResult<ChatTestMessageResultDto>.Ok(new ChatTestMessageResultDto
            {
                Delivered = result.Delivered,
                Error = result.Error,
                NextSteps = ChatSetupNextSteps.AfterTestMessage(chatId, parsed, result.Delivered),
            });
        }

        public Task<PolicyWriteResult<ChatInvitationDto>> CreateInvitationAsync(Guid chatId, string kind, ClaimsPrincipal caller) =>
            Task.FromResult(CreateInvitation(chatId, kind, caller));

        private PolicyWriteResult<ChatInvitationDto> CreateInvitation(Guid chatId, string kind, ClaimsPrincipal caller)
        {
            var chat = _chats[chatId];

            if (chat is null || !IsChatVisible(caller, chat))
                return Fail<ChatInvitationDto>(PolicyWriteOutcome.NotFound);

            if (!CanOperate(caller, chat))
                return Fail<ChatInvitationDto>(PolicyWriteOutcome.Forbidden);

            var isDirect = kind is "direct";
            var isGroup = kind is "group";

            if (!isDirect && !isGroup)
                return Fail<ChatInvitationDto>(PolicyWriteOutcome.Invalid, field: "kind", message: $"Unknown invitation kind '{kind}'.");

            if (!_delivery.IsTelegramBotRunning)
                return Fail<ChatInvitationDto>(PolicyWriteOutcome.Invalid, field: "telegram", message: BotNotRunningMessage);

            // The invitation attributes the eventual /start to the calling user, exactly as the
            // web flow's token does — resolve the token's owner.
            var owner = ResolveOwner(caller);

            if (owner is null)
                return Fail<ChatInvitationDto>(PolicyWriteOutcome.Forbidden);

            // Read the clock BEFORE the token is minted: the reported expiry must never be
            // later than the token's real one.
            var expiresAt = DateTime.UtcNow.Add(InvitationLifetime);

            var value = isDirect
                ? _chats.GetChatInvitationLink(chatId, owner, InvitationLifetime)
                : _chats.GetChatGroupInvitation(chatId, owner, InvitationLifetime);

            return PolicyWriteResult<ChatInvitationDto>.Ok(new ChatInvitationDto
            {
                Kind = kind,
                Value = value,
                ExpiresAt = expiresAt,
                NextSteps = HumanSteps(isDirect, value),
            });
        }

        // The person's steps of the kind: the guide's own words, the ones the web Telegram modal
        // prints, with the words that stand for an invitation reading as what was just issued.
        private List<string> HumanSteps(bool isDirect, string value)
        {
            var issued = isDirect ? ChatSetupInvitationText.IssuedLink(value) : ChatSetupInvitationText.IssuedCommand(value);
            var guide = isDirect ? ChatSetupGuide.TelegramDirect : ChatSetupGuide.TelegramGroup;

            return [.. guide.Select(step => step.ToText(_config.Telegram.BotName, issued))];
        }

        private bool CanOperate(ClaimsPrincipal caller, Chat chat) =>
            _authorization.CanSeeChatDetail(caller, StableCopy.Of(() => chat.Folders));

        // The chat sight rule of the read service (ChatsReadService.IsChatVisible): global, or
        // bound to at least one folder the caller can see.
        private bool IsChatVisible(ClaimsPrincipal caller, Chat chat)
        {
            var folders = StableCopy.Of(() => chat.Folders);

            return folders.Count == 0 || folders.Any(id => _authorization.IsVisible(caller, ApiTokenResource.Folder(id)));
        }

        private User ResolveOwner(ClaimsPrincipal caller) =>
            Guid.TryParse(caller.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value, out var ownerId)
                ? _users[ownerId]
                : null;

        private static bool TryChannel(string channel, out ChatChannel parsed)
        {
            switch (channel?.ToLowerInvariant())
            {
                case "telegram": parsed = ChatChannel.Telegram; return true;
                case "slack": parsed = ChatChannel.Slack; return true;
                case "mattermost": parsed = ChatChannel.Mattermost; return true;
                default: parsed = default; return false;
            }
        }

        private static PolicyWriteResult<T> Fail<T>(PolicyWriteOutcome outcome,
            string field = null, string message = null) =>
            PolicyWriteResult<T>.Fail(outcome,
                field is null ? null : new Dictionary<string, string[]> { [field] = [message] }, message);
    }

    /// <summary>
    /// The delivery half the verification service needs: the channels and the bot behind
    /// NotificationsCenter (their concrete types are sealed, so the seam is this adapter).
    /// </summary>
    public interface IChatDelivery
    {
        Task<ChatDeliveryResult> SendTestAsync(Chat chat, ChatChannel channel);

        bool IsTelegramBotRunning { get; }
    }

    public sealed class NotificationsCenterChatDelivery : IChatDelivery
    {
        private readonly NotificationsCenter _notifications;

        public NotificationsCenterChatDelivery(NotificationsCenter notifications) =>
            _notifications = notifications;

        public Task<ChatDeliveryResult> SendTestAsync(Chat chat, ChatChannel channel) => channel switch
        {
            ChatChannel.Telegram => _notifications.TelegramBot.SendTestWithResultAsync(chat),
            ChatChannel.Slack => _notifications.SlackChannel.SendTestAsync(chat),
            ChatChannel.Mattermost => _notifications.MattermostChannel.SendTestAsync(chat),
            _ => Task.FromResult(ChatDeliveryResult.Fail($"Unknown channel '{channel}'.")),
        };

        public bool IsTelegramBotRunning => _notifications.TelegramBot.IsBotRunning;
    }

    /// <summary>The outcome of a chat test message: delivered, or the delivery error text.</summary>
    [Description("The outcome of a chat test message: delivered, or the error the delivery channel reported (the same text it logs) — and what to do next.")]
    public sealed record ChatTestMessageResultDto
    {
        /// <summary>Whether the message reached the channel.</summary>
        [Description("Whether the test message reached the channel.")]
        public bool Delivered { get; init; }

        /// <summary>The delivery error text when not delivered; null otherwise.</summary>
        [Description("The delivery error text when not delivered; null otherwise. Never a response body.")]
        public string Error { get; init; }

        /// <summary>The next step of the setup, naming its tool call.</summary>
        [Description("What to do next, naming its tool call: delivered — the channel works, and alerts can be routed to the chat; not delivered — where to look, and the call that stores a new webhook.")]
        public List<string> NextSteps { get; init; }
    }

    /// <summary>A one-shot Telegram invitation for a chat, issued to an orchestrator (#1826).</summary>
    [Description("A one-shot Telegram invitation issued through the management surface: the link (direct chat) or the group command, its expiry, and the human steps inside Telegram.")]
    public sealed record ChatInvitationDto
    {
        /// <summary>The invitation kind — "direct" or "group".</summary>
        [Description("The invitation kind — \"direct\" or \"group\".")]
        public string Kind { get; init; }

        /// <summary>The invitation value: a https://t.me/… link for a direct chat, or the /start@bot command to post into the group.</summary>
        [Description("The invitation value: a https://t.me/… link for a direct chat, or the /start@bot command to post into the group.")]
        public string Value { get; init; }

        /// <summary>When the invitation expires (one-shot; a second /start with it is rejected).</summary>
        [Description("When the invitation expires (UTC ISO 8601). One-shot: a second /start with the same token is rejected.")]
        public DateTime ExpiresAt { get; init; }

        /// <summary>The steps a PERSON does inside Telegram — the orchestrator cannot press Start.</summary>
        [Description("The steps a PERSON performs inside Telegram, with this invitation in place — relay them; the orchestrator cannot do them (ADR docs/adr/0002). The same steps as get_chat_setup_guide(channel: \"telegram-chat\").")]
        public List<string> NextSteps { get; init; }
    }

    /// <summary>The channel a test message targets.</summary>
    [Description("The channel a chat test message targets: \"telegram\", \"slack\" or \"mattermost\".")]
    public sealed record ChatTestMessageRequestDto
    {
        /// <summary>The delivery channel to test — one the chat carries.</summary>
        [Description("The delivery channel to test: \"telegram\", \"slack\" or \"mattermost\" — one the chat carries (see get_chat's channel flags).")]
        public string Channel { get; init; }
    }

    /// <summary>The kind of a Telegram invitation.</summary>
    [Description("The kind of a Telegram invitation: \"direct\" or \"group\".")]
    public sealed record ChatInvitationRequestDto
    {
        /// <summary>"direct" (a link) or "group" (a command to post into the group).</summary>
        [Description("\"direct\" (a link to open and press Start) or \"group\" (a command to post into the group after adding the bot with admin rights).")]
        public string Kind { get; init; }
    }
}
