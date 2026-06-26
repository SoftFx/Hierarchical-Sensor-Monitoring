using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications
{
    public record SlackDestinationUpdate : BaseUpdateRequest
    {
        public string WebhookUrl { get; init; }

        public bool? SendMessages { get; init; }
    }
}
