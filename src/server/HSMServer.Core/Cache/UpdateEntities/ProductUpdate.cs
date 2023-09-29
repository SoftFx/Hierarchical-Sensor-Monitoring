using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed record ProductUpdate : BaseNodeUpdate
    {
        public Guid? FolderId { get; init; }

        [Obsolete("Should be removed after telegram chats migration")]
        public NotificationSettingsEntity NotificationSettings { get; init; }
    }
}
