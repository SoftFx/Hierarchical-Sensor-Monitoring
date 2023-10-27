using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record TelegramChatEntity : BaseSiteEntity
    {
        public byte Type { get; init; }

        public long ChatId { get; init; }

        public long AuthorizationTime { get; init; }


        public bool SendMessages { get; init; }

        public int MessagesAggregationTimeSec { get; init; }
    }
}