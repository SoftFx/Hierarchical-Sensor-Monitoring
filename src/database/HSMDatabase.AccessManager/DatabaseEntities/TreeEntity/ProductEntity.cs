﻿namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record ProductEntity : BaseNodeEntity
    {
        public NotificationSettingsEntity NotificationSettings { get; init; }


        public string ParentProductId { get; init; }

        public string FolderId { get; init; }

        public int State { get; init; }
    }
}