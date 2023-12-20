using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record TelegramChatEntity : BaseServerEntity
    {
        public byte Type { get; set; }

        public long ChatId { get; set; }

        public long AuthorizationTime { get; set; }


        public bool SendMessages { get; set; }

        public int MessagesAggregationTimeSec { get; set; }
    }
}