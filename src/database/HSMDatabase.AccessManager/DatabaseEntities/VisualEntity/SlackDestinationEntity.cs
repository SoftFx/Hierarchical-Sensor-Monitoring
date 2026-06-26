using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record SlackDestinationEntity : BaseServerEntity
    {
        public string WebhookUrl { get; set; }

        public bool SendMessages { get; set; }
    }
}
