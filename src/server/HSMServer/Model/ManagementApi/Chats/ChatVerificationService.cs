using HSMServer.Authentication;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.Authentication;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The chat verification surface (#1826): a test message that answers its delivery
    /// outcome, and the Telegram invitation issuer for the management surface. Transport-
    /// agnostic — the REST controller and the MCP tools both call it; the caller supplies
    /// the token principal and gets the area's write envelope back, never an IActionResult.
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
        private const string BotNotRunningMessage =
            "The Telegram bot is not running. Configure or start it first (update_telegram_bot).";

        private readonly IChatsManager _chats;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly IChatDelivery _delivery;


        public ChatVerificationService(IChatsManager chats, IApiTokenAuthorizationService authorization,
            IUserManager users, IChatDelivery delivery)
        {
            _chats = chats;
            _authorization = authorization;
            _users = users;
            _delivery = delivery;
        }


        public async Task<PolicyWriteResult<ChatTestMessageResultDto>> SendTestMessageAsync(Guid chatId, string channel, ClaimsPrincipal caller)
        {
            // The storage indexer answers null for an unknown id — the area's dictionary
            // semantics (ChatsReadService uses the same lookup shape).
            var chat = _chats[chatId];

            if (chat is null)
                return Fail(PolicyWriteOutcome.NotFound);

            if (!CanOperate(caller, chat))
                return Fail(PolicyWriteOutcome.Forbidden);

            if (!TryChannel(channel, out var parsed))
                return Fail(PolicyWriteOutcome.Invalid, field: "channel", message: string.Format(UnknownChannelMessage, channel));

            var carried = parsed switch
            {
                ChatChannel.Telegram => chat.TelegramChatId is not null,
                ChatChannel.Slack => !string.IsNullOrEmpty(chat.SlackWebhookUrl),
                ChatChannel.Mattermost => !string.IsNullOrEmpty(chat.MattermostWebhookUrl),
                _ => false,
            };

            if (!carried)
                return Fail(PolicyWriteOutcome.Invalid, field: "channel", message: string.Format(ChannelNotCarriedMessage, parsed));

            var result = await _delivery.SendTestAsync(chat, parsed);

            return PolicyWriteResult<ChatTestMessageResultDto>.Ok(new ChatTestMessageResultDto
            {
                Delivered = result.Delivered,
                Error = result.Error,
            });
        }

        public Task<PolicyWriteResult<ChatInvitationDto>> CreateInvitationAsync(Guid chatId, string kind, ClaimsPrincipal caller) =>
            Task.FromResult(CreateInvitation(chatId, kind, caller));

        private PolicyWriteResult<ChatInvitationDto> CreateInvitation(Guid chatId, string kind, ClaimsPrincipal caller)
        {
            var chat = _chats[chatId];

            if (chat is null)
                return FailInvitation(PolicyWriteOutcome.NotFound);

            if (!CanOperate(caller, chat))
                return FailInvitation(PolicyWriteOutcome.Forbidden);

            var isDirect = kind is "direct";
            var isGroup = kind is "group";

            if (!isDirect && !isGroup)
                return FailInvitation(PolicyWriteOutcome.Invalid, field: "kind", message: $"Unknown invitation kind '{kind}'.");

            if (!_delivery.IsTelegramBotRunning)
                return FailInvitation(PolicyWriteOutcome.Invalid, field: "kind", message: BotNotRunningMessage);

            // The invitation attributes the eventual /start to the calling user, exactly as the
            // web flow's token does — resolve the token's owner.
            var owner = ResolveOwner(caller);

            if (owner is null)
                return FailInvitation(PolicyWriteOutcome.Forbidden);

            var value = isDirect
                ? _chats.GetChatInvitationLink(chatId, owner, InvitationLifetime)
                : _chats.GetChatGroupInvitation(chatId, owner, InvitationLifetime);

            return PolicyWriteResult<ChatInvitationDto>.Ok(new ChatInvitationDto
            {
                Kind = kind,
                Value = value,
                ExpiresAt = DateTime.UtcNow.Add(InvitationLifetime),
                NextSteps = isDirect ? [.. ChatInvitationDto.DirectNextSteps] : [.. ChatInvitationDto.GroupNextSteps],
            });
        }

        private bool CanOperate(ClaimsPrincipal caller, Chat chat) =>
            _authorization.CanSeeChatDetail(caller, StableCopy.Of(() => chat.Folders));

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

        private static PolicyWriteResult<ChatTestMessageResultDto> Fail(PolicyWriteOutcome outcome,
            string field = null, string message = null) =>
            PolicyWriteResult<ChatTestMessageResultDto>.Fail(outcome,
                field is null ? null : new Dictionary<string, string[]> { [field] = [message] }, message);

        private static PolicyWriteResult<ChatInvitationDto> FailInvitation(PolicyWriteOutcome outcome,
            string field = null, string message = null) =>
            PolicyWriteResult<ChatInvitationDto>.Fail(outcome,
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
    [System.ComponentModel.Description("The outcome of a chat test message: delivered, or the error the delivery channel reported (the same text it logs).")]
    public sealed record ChatTestMessageResultDto
    {
        /// <summary>Whether the message reached the channel.</summary>
        [System.ComponentModel.Description("Whether the test message reached the channel.")]
        public bool Delivered { get; init; }

        /// <summary>The delivery error text when not delivered; null otherwise.</summary>
        [System.ComponentModel.Description("The delivery error text when not delivered; null otherwise. Never a response body.")]
        public string Error { get; init; }
    }

    /// <summary>A one-shot Telegram invitation for a chat, issued to an orchestrator (#1826).</summary>
    [System.ComponentModel.Description("A one-shot Telegram invitation issued through the management surface: the link (direct chat) or the group command, its expiry, and the human steps inside Telegram.")]
    public sealed record ChatInvitationDto
    {
        /// <summary>The invitation kind — "direct" or "group".</summary>
        [System.ComponentModel.Description("The invitation kind — \"direct\" or \"group\".")]
        public string Kind { get; init; }

        /// <summary>The invitation value: a https://t.me/… link for a direct chat, or the /start@bot command to post in the group.</summary>
        [System.ComponentModel.Description("The invitation value: a https://t.me/… link for a direct chat, or the /start@bot command to post into the group.")]
        public string Value { get; init; }

        /// <summary>When the invitation expires (one-shot; a second /start with it is rejected).</summary>
        [System.ComponentModel.Description("When the invitation expires (UTC ISO 8601). One-shot: a second /start with the same token is rejected.")]
        public DateTime ExpiresAt { get; init; }

        /// <summary>The steps a PERSON does inside Telegram — the orchestrator cannot press Start.</summary>
        [System.ComponentModel.Description("The steps a PERSON performs inside Telegram; the orchestrator cannot do them (ADR docs/adr/0002).")]
        public List<string> NextSteps { get; init; }

        internal static readonly string[] DirectNextSteps =
        [
            "Open the invitation link.",
            "Press Start in the opened chat with the bot.",
        ];

        internal static readonly string[] GroupNextSteps =
        [
            "Create the Telegram group (if it does not exist yet).",
            "Add the bot to the group and grant it admin rights.",
            "Post the command into the group.",
        ];
    }

    /// <summary>The channel a test message targets.</summary>
    [System.ComponentModel.Description("The channel a chat test message targets: \"telegram\", \"slack\" or \"mattermost\".")]
    public sealed record ChatTestMessageRequestDto
    {
        /// <summary>The delivery channel to test — one the chat carries.</summary>
        [System.ComponentModel.Description("The delivery channel to test: \"telegram\", \"slack\" or \"mattermost\" — one the chat carries (see get_chat's channel flags).")]
        public string Channel { get; init; }
    }

    /// <summary>The kind of a Telegram invitation.</summary>
    [System.ComponentModel.Description("The kind of a Telegram invitation: \"direct\" or \"group\".")]
    public sealed record ChatInvitationRequestDto
    {
        /// <summary>"direct" (a link) or "group" (a command to post into the group).</summary>
        [System.ComponentModel.Description("\"direct\" (a link to open and press Start) or \"group\" (a command to post into the group after adding the bot with admin rights).")]
        public string Kind { get; init; }
    }
}
