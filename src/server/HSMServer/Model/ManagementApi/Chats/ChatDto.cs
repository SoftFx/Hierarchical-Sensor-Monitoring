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
    public sealed record ChatDto
    {
        /// <summary>Chat id.</summary>
        public Guid Id { get; init; }

        /// <summary>Admin-owned display name (unique per server).</summary>
        public string Name { get; init; }

        /// <summary>Free-form description (may be empty).</summary>
        public string Description { get; init; }

        /// <summary>Chat creation time (UTC).</summary>
        public DateTime CreationDate { get; init; }

        /// <summary>Author display name; null when unknown (the id itself is never exposed).</summary>
        public string Author { get; init; }

        /// <summary>Whether this chat currently receives messages (the admin enable/disable switch).</summary>
        public bool SendMessages { get; init; }

        /// <summary>Delay (seconds) messages of one sensor are aggregated for before sending.</summary>
        public int MessagesAggregationTimeSec { get; init; }

        /// <summary>Telegram chat id; null when no Telegram binding. Shown in the clear by the web UI.</summary>
        public long? TelegramChatId { get; init; }

        /// <summary>Telegram binding kind — "direct" or "group"; null when no Telegram binding.</summary>
        public string TelegramType { get; init; }

        /// <summary>When the Telegram binding was authorized via the bot; null when unbound.</summary>
        public DateTime? TelegramAuthorizationTime { get; init; }

        /// <summary>Telegram chat title mirrored from the bot; null when unbound.</summary>
        public string TelegramChatTitle { get; init; }

        /// <summary>Telegram chat description mirrored from the bot; null when unbound.</summary>
        public string TelegramChatDescription { get; init; }

        /// <summary>Whether a Slack webhook is configured (the URL itself is a secret and is never exposed).</summary>
        public bool HasSlackWebhook { get; init; }

        /// <summary>Whether a Mattermost webhook is configured (the URL itself is a secret and is never exposed).</summary>
        public bool HasMattermostWebhook { get; init; }

        /// <summary>
        /// Ids of the folders the chat is bound to. EMPTY means a global chat —
        /// available from every node's policies; otherwise the chat is usable
        /// only from the folders listed here (the same rule policy/template
        /// writes validate destination chats against).
        /// </summary>
        public List<Guid> Folders { get; init; }
    }
}
