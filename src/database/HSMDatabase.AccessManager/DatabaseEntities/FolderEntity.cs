using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class FolderEntity
    {
        public string Id { get; init; }

        public string AuthorId { get; init; }

        public long CreationDate { get; init; }


        public string DisplayName { get; init; }

        public string Description { get; init; }

        public int Color { get; init; }


        public List<TimeIntervalEntity> ServerPolicies { get; init; } = new();

        public NotificationSettingsEntity Notifications { get; init; }
    }
}
