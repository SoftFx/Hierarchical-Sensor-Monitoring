using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications.Chats
{
    public record ChatUpdate : BaseUpdateRequest
    {
        // Common
        public bool? SendMessages { get; init; }

        public int? MessagesAggregationTimeSec { get; init; }


        // Telegram chat id (optional; TelegramType / AuthorizationTime are init-only on Chat)
        public long? TelegramChatId { get; init; }


        // Slack (optional)
        public string SlackWebhookUrl { get; init; }


        // Mattermost (optional)
        public string MattermostWebhookUrl { get; init; }


        // Negative-clear flags. Chat.ApplyUpdate uses `?? current` for SlackWebhookUrl /
        // MattermostWebhookUrl, so a null update value means "don't change" and submitting an
        // empty string through EditChat cannot clear a saved webhook. Telegram binding is even
        // stricter — TelegramType / AuthorizationTime are private-set and only mutable here.
        // These flags route around both issues by explicitly signalling "set this channel to null".
        public bool? ClearTelegramBinding { get; init; }

        public bool? ClearSlackWebhook { get; init; }

        public bool? ClearMattermostWebhook { get; init; }
    }
}
