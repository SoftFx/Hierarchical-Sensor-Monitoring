using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications
{
    public record TelegramChatUpdate : BaseUpdateRequest
    {
        public bool? SendMessages { get; init; }

        public int? MessagesAggregationTimeSec { get; init; }
    }
}