using HSMServer.Model.ManagementApi.ChangeSets;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The chat to create (#1829). A Telegram binding is not requested here: it is a
    /// person's own /start against an invitation (ADR docs/adr/0002).
    /// </summary>
    [Description("The notification chat to create. A Telegram binding is not requested here: it is bound by a person's /start against an invitation (create_chat_telegram_invitation).")]
    public sealed record CreateChatRequestDto
    {
        /// <summary>Display name: required, at most 100 characters, unique per server ignoring case, without control or bidirectional-override characters.</summary>
        [Description("Display name: required, at most 100 characters, unique per server ignoring case, without control or bidirectional-override characters.")]
        public string Name { get; init; }

        /// <summary>Free-form description; optional.</summary>
        [Description("Free-form description; optional.")]
        public string Description { get; init; }

        /// <summary>Whether the chat receives messages; true when omitted.</summary>
        [Description("Whether the chat receives messages; true when omitted.")]
        public bool? SendMessages { get; init; }

        /// <summary>Delay (seconds) that messages of one sensor are aggregated for before sending: 0 sends every message at once, 86400 (a day) at most; 60 when omitted.</summary>
        [Description("Delay in seconds that messages of one sensor are aggregated for before sending: 0 sends every message at once, at most 86400 (a day); 60 when omitted.")]
        public int? MessagesAggregationTimeSec { get; init; }

        /// <summary>Slack incoming-webhook URL: an absolute http or https URL. The URL is a secret — it is written, never echoed.</summary>
        [Description("Slack incoming-webhook URL: an absolute http or https URL. The URL is a secret: it is written, never echoed (the response answers hasSlackWebhook).")]
        public string SlackWebhookUrl { get; init; }

        /// <summary>Mattermost incoming-webhook URL: the same rules as <see cref="SlackWebhookUrl"/>.</summary>
        [Description("Mattermost incoming-webhook URL: an absolute http or https URL, written and never echoed (the response answers hasMattermostWebhook).")]
        public string MattermostWebhookUrl { get; init; }

        /// <summary>Ids of the folders to bind the chat to; each must be a folder the token's owner manages. Omitted or empty: a global chat.</summary>
        [Description("Ids of the folders to bind the chat to; each must be a folder the token's owner manages. Omitted or empty: a global chat, usable from every folder.")]
        public List<Guid> Folders { get; init; }
    }

    /// <summary>
    /// The changes to a notification chat (#1829). Every field is optional — an omitted
    /// (null) field stays unchanged — and a channel is removed only by its clear flag.
    /// </summary>
    [Description("The changes to a notification chat. Every field is optional: an omitted (null) field stays unchanged. A webhook or the Telegram binding is removed only by its clear flag; an empty webhook value changes nothing.")]
    public sealed record UpdateChatRequestDto
    {
        /// <summary>New display name: the rules of the creation apply; the chat's own name in another letter case is allowed.</summary>
        [Description("New display name: required, at most 100 characters, unique per server ignoring case (the chat's own name in another letter case is allowed), without control or bidirectional-override characters.")]
        public string Name { get; init; }

        /// <summary>New free-form description; an empty string clears it.</summary>
        [Description("New free-form description; an empty string clears it.")]
        public string Description { get; init; }

        /// <summary>Whether the chat receives messages.</summary>
        [Description("Whether the chat receives messages.")]
        public bool? SendMessages { get; init; }

        /// <summary>New delay (seconds) that messages of one sensor are aggregated for before sending: 0 sends every message at once, 86400 (a day) at most.</summary>
        [Description("New delay in seconds that messages of one sensor are aggregated for before sending: 0 sends every message at once, at most 86400 (a day).")]
        public int? MessagesAggregationTimeSec { get; init; }

        /// <summary>New Slack incoming-webhook URL: an absolute http or https URL, written and never echoed. An empty value changes nothing.</summary>
        [Description("New Slack incoming-webhook URL: an absolute http or https URL, written and never echoed. An empty value changes nothing; clearSlackWebhook removes the webhook.")]
        public string SlackWebhookUrl { get; init; }

        /// <summary>New Mattermost incoming-webhook URL: the rules of <see cref="SlackWebhookUrl"/> apply.</summary>
        [Description("New Mattermost incoming-webhook URL: the rules of slackWebhookUrl apply.")]
        public string MattermostWebhookUrl { get; init; }

        /// <summary>Remove the Slack webhook.</summary>
        [Description("Remove the Slack webhook.")]
        public bool? ClearSlackWebhook { get; init; }

        /// <summary>Remove the Mattermost webhook.</summary>
        [Description("Remove the Mattermost webhook.")]
        public bool? ClearMattermostWebhook { get; init; }

        /// <summary>Remove the Telegram binding. A new one is bound only by a person's /start against an invitation.</summary>
        [Description("Remove the Telegram binding. A new one is bound only by a person's /start against an invitation (create_chat_telegram_invitation).")]
        public bool? ClearTelegramBinding { get; init; }

        /// <summary>
        /// The complete list of folders to bind the chat to — a full replace among the
        /// folders the token's owner manages: a managed binding not listed is removed, a
        /// binding to a folder the owner cannot manage stays, every listed id must be a
        /// folder the owner manages. Null (omitted): the bindings stay as they are. When
        /// the update leaves the chat bound only to folders the caller cannot see, the echo
        /// shows an empty <c>folders</c> and the chat is no longer in the caller's sight (a
        /// later read answers 404).
        /// </summary>
        [Description("The complete list of folders to bind the chat to: a full replace among the folders the token's owner manages. A managed binding not listed is removed; a binding to a folder the owner cannot manage stays; every listed id must be a folder the owner manages. Omitted: the bindings stay as they are. If the update leaves the chat bound only to folders you cannot see, the echo shows an empty `folders` and the chat leaves your sight (a later read answers 404).")]
        public List<Guid> Folders { get; init; }
    }

    /// <summary>
    /// The dry-run preview of a chat deletion (#1829): the chat, the folder bindings the
    /// deletion would take with it, and how many sensors' alerts reference the chat.
    /// </summary>
    [Description("The dry-run preview of a chat deletion: the chat, the folder bindings it would take with it, and how many sensors' alerts reference the chat. Nothing was applied.")]
    public sealed record ChatDeletePreviewDto : WritePreviewDto
    {
        /// <summary>Number of sensors whose effective alert configuration references the chat — the counter behind the web chat list's usage badge.</summary>
        [Description("Number of sensors whose effective alert configuration references the chat (the web chat list's usage count).")]
        public int SensorUsageCount { get; init; }

        /// <summary>True when the count is a lower bound — at least one sensor was skipped during the scan.</summary>
        [Description("True when the count is a lower bound: at least one sensor was skipped during the scan, so read sensorUsageCount as \"at least\".")]
        public bool SensorUsageIncomplete { get; init; }
    }
}
