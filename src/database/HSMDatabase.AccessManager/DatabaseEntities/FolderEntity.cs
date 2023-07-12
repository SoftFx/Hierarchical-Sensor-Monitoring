using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record FolderEntity : BaseNodeEntity
    {
        [Obsolete("Remove after policy migration")]
        public List<OldTimeIntervalEntity> ServerPolicies { get; init; } = new();

        public NotificationSettingsEntity Notifications { get; init; }

        public int Color { get; init; }
    }
}