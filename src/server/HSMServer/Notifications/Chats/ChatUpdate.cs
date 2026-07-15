using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications.Chats
{
    public record ChatUpdate : BaseUpdateRequest
    {
        // Common
        public bool? SendMessages { get; init; }

        public int? MessagesAggregationTimeSec { get; init; }


        // Telegram (optional)
        public byte? TelegramType { get; init; }

        public long? TelegramChatId { get; init; }

        public long? AuthorizationTime { get; init; }


        // Slack (optional)
        public string SlackWebhookUrl { get; init; }


        // Mattermost (optional)
        public string MattermostWebhookUrl { get; init; }
    }
}
