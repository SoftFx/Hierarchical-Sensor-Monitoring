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


        // Telegram (synced from bot on every start; not admin-editable)
        public string TelegramChatTitle { get; set; }

        public string TelegramChatDescription { get; set; }


        // Slack (optional)
        public string SlackWebhookUrl { get; set; }


        // Mattermost (optional)
        public string MattermostWebhookUrl { get; set; }
    }
}
