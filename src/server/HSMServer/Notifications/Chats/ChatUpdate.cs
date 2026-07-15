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
    }
}
