using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed record ProductUpdate : BaseNodeUpdate
    {
        public NotificationSettingsEntity NotificationSettings { get; init; }
    }
}
