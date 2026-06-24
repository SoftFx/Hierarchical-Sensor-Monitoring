using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications
{
    public record SlackAddRequest : BaseAddRequest
    {
        public required string WebhookUrl { get; init; }
    }
}
