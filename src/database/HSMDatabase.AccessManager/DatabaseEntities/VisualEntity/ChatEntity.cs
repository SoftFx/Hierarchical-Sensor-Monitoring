using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record ChatEntity : BaseServerEntity
    {
        // Common
        public bool SendMessages { get; set; }

        public int MessagesAggregationTimeSec { get; set; }


        // Telegram (optional)
        public byte? TelegramType { get; set; }

        public long? TelegramChatId { get; set; }

        public long? AuthorizationTime { get; set; }


        // Slack (optional)
        public string SlackWebhookUrl { get; set; }


        // Mattermost (optional, channel not implemented yet)
        public string MattermostWebhookUrl { get; set; }
    }
}
