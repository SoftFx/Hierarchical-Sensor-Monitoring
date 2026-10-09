using System;
using System.Collections.Generic;

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
    //    what the write endpoints validate destination.chats against;
    //  - detail fields (marked below) — null unless the token's owner is an
    //    admin or a manager of a folder the chat is bound to; for a direct
    //    chat telegramChatId is the person's Telegram user id, so it never
    //    answers a read-only Viewer.
    public sealed record ChatDto
    {
        /// <summary>Chat id.</summary>
        public Guid Id { get; init; }

        /// <summary>Admin-owned display name (unique per server).</summary>
        public string Name { get; init; }

        /// <summary>Free-form description (may be empty). Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public string Description { get; init; }

        /// <summary>Chat creation time (UTC). Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public DateTime? CreationDate { get; init; }

        /// <summary>Author display name (detail field: null unless the owner is an admin or a manager of a bound folder; the id itself is never exposed).</summary>
        public string Author { get; init; }

        /// <summary>Whether this chat currently receives messages (the admin enable/disable switch). Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public bool? SendMessages { get; init; }

        /// <summary>Delay (seconds) messages of one sensor are aggregated for before sending. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public int? MessagesAggregationTimeSec { get; init; }

        /// <summary>Telegram chat id (for a direct chat, the person's Telegram user id). Detail field: null unless the owner is an admin or a manager of a bound folder — the web UI shows it in the clear only to admins/PMs.</summary>
        public long? TelegramChatId { get; init; }

        /// <summary>Telegram binding kind — "direct" or "group"; null when no Telegram binding.</summary>
        public string TelegramType { get; init; }

        /// <summary>When the Telegram binding was authorized via the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public DateTime? TelegramAuthorizationTime { get; init; }

        /// <summary>Telegram chat title mirrored from the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public string TelegramChatTitle { get; init; }

        /// <summary>Telegram chat description mirrored from the bot. Detail field: null unless the owner is an admin or a manager of a bound folder.</summary>
        public string TelegramChatDescription { get; init; }

        /// <summary>Whether a Slack webhook is configured (the URL itself is a secret and is never exposed).</summary>
        public bool HasSlackWebhook { get; init; }

        /// <summary>Whether a Mattermost webhook is configured (the URL itself is a secret and is never exposed).</summary>
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
        public List<Guid> Folders { get; init; }
    }
}
