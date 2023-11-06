using HSMServer.ConcurrentStorage;
using System;

namespace HSMServer.Notifications
{
    public record TelegramChatUpdate : IUpdateRequest
    {
        public required Guid Id { get; init; }

        public string Name { get; init; }


        public string Description { get; init; }

        public bool? SendMessages { get; init; }

        public int? MessagesAggregationTimeSec { get; init; }
    }
}
