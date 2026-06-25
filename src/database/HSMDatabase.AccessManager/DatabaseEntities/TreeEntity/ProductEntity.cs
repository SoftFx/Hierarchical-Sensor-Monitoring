using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record ProductEntity : BaseNodeEntity
    {
        public string ParentProductId { get; init; }

        public string FolderId { get; init; }

        public int State { get; init; }

        /// Sensor groups disabled by a server admin for this product's agents (#1198).
        /// Null means "all enabled" (existing records have no field → default open).
        public List<string> DisabledSensorGroups { get; init; }
    }
}