namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record FolderEntity : BaseNodeEntity
    {
        public NotificationSettingsEntity Notifications { get; init; }

        public int Color { get; init; }
    }
}