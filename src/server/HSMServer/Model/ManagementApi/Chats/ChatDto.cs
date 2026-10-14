using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// A notification chat — the destination alert policies and alert templates
    /// route their messages to. One chat row can carry a Telegram binding
    /// (private or group via the bot), a Slack webhook and a Mattermost
    /// webhook; the booleans below say which channels are configured without
    /// ever exposing the webhook URLs themselves (secrets, masked even in the
    /// web UI).
    /// </summary>
    // The DTO splits into two halves (the web UI's authorization model, which
    // shows chat detail only to admins and managers of a bound folder):
    //  - id-discovery fields — id, name, telegramType, the channel flags and
    //    folders — answer every caller past the caller-wide gate; they are
    //    what the write endpoints validate destination.chats against. The
    //    hasSlackWebhook/hasMattermostWebhook flags are DELIBERATELY
    //    discovery-tier — the one exception to the detail gate: presence
    //    tells a caller which delivery channels a chat offers when composing
    //    destination.chats, and a boolean carries no secret (the URLs are
    //    never serialized at all);
    //  - detail fields (marked below) — null unless the token's owner is an
    //    admin or a manager of a folder the chat is bound to; for a direct
    //    chat telegramChatId is the person's Telegram user id, so it never
    //    answers a read-only Viewer.
    [Description("A notification chat — the destination alert policies and templates route their messages to. One chat can carry a Telegram binding, a Slack webhook and a Mattermost webhook; the booleans say which channels are configured (the webhook URLs themselves are never exposed).")]
    public sealed record ChatDto
    {
        /// <summary>Chat id.</summary>
        [Description("Chat id — the value policy/template destinations reference.")]
        public Guid Id { get; init; }

        /// <summary>Admin-owned display name (unique per server).</summary>
        [Description("Admin-owned display name (unique per server).")]
        public string Name { get; init; }

        /// <summary>Free-form description (may be empty). Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("Free-form description (may be empty). Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public string Description { get; init; }

        /// <summary>Chat creation time (UTC). Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("Chat creation time (UTC). Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public DateTime? CreationDate { get; init; }

        /// <summary>Author display name (detail field: null unless the owner is an admin or a manager of a bound folder; the id itself is never exposed).</summary>
        [Description("Author display name (detail field: null unless the owner is an admin or a manager of a bound folder; the id itself is never exposed).")]
        public string Author { get; init; }

        /// <summary>Whether this chat currently receives messages (the admin enable/disable switch). Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("Whether this chat currently receives messages (the admin enable/disable switch). Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public bool? SendMessages { get; init; }

        /// <summary>Delay (seconds) messages of one sensor are aggregated for before sending. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("Delay (seconds) messages of one sensor are aggregated for before sending. Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public int? MessagesAggregationTimeSec { get; init; }

        /// <summary>Telegram chat id (for a direct chat, the person's Telegram user id). Detail field: null unless the owner is an admin or a manager of a bound folder — the web UI shows it in the clear only to admins/PMs.</summary>
        [Description("Telegram chat id (for a direct chat, the person's Telegram user id). Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public long? TelegramChatId { get; init; }

        /// <summary>Telegram binding kind — "direct" or "group"; null when no Telegram binding.</summary>
        [Description("Telegram binding kind — \"direct\" or \"group\"; null when no Telegram binding.")]
        public string TelegramType { get; init; }

        /// <summary>When the Telegram binding was authorized via the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("When the Telegram binding was authorized via the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public DateTime? TelegramAuthorizationTime { get; init; }

        /// <summary>Telegram chat title mirrored from the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("Telegram chat title mirrored from the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public string TelegramChatTitle { get; init; }

        /// <summary>Telegram chat description mirrored from the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        [Description("Telegram chat description mirrored from the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.")]
        public string TelegramChatDescription { get; init; }

        /// <summary>Whether a Slack webhook is configured (the URL itself is a secret and is never exposed).</summary>
        [Description("Whether a Slack webhook is configured (the URL itself is a secret and is never exposed).")]
        public bool HasSlackWebhook { get; init; }

        /// <summary>Whether a Mattermost webhook is configured (the URL itself is a secret and is never exposed).</summary>
        [Description("Whether a Mattermost webhook is configured (the URL itself is a secret and is never exposed).")]
        public bool HasMattermostWebhook { get; init; }

        /// <summary>
        /// Ids of the folders the chat is bound to, FILTERED to the folders
        /// the caller can see (an invisible folder id is indistinguishable
        /// from an unknown one — the area's anti-enumeration rule). EMPTY
        /// means a global chat — available from every node's policies;
        /// otherwise the chat is usable from the folders listed here (the
        /// same rule policy/template writes validate destination chats
        /// against). A chat bound only to invisible folders is not visible
        /// at all, so a non-empty unfiltered case cannot arise.
        /// </summary>
        [Description("Ids of the folders the chat is bound to, filtered to the folders the caller can see. EMPTY means a global chat — usable from every node's policies; otherwise the chat is usable only from the folders listed (the rule policy/template writes validate destination chats against).")]
        public List<Guid> Folders { get; init; }
    }
}
