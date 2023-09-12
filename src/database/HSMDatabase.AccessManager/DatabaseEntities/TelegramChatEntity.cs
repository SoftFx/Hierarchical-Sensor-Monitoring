namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class TelegramChatEntity
    {
        public byte[] Id { get; init; }

        public byte Type { get; init; }

        public long ChatId { get; init; }

        public byte[] Author { get; init; }

        public long AuthorizationTime { get; init; }


        public bool SendMessages { get; init; }

        public int MessagesAggregationTimeSec { get; init; }


        public string Name { get; init; }

        public string Description { get; init; }
    }
}
