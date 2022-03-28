using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record ProductEntity
    {
        [Obsolete]
        public string Key { get; set; }
        [Obsolete]
        public string Name { get; set; }
        [Obsolete]
        public DateTime DateAdded { get; set; }
        [Obsolete]
        public List<ExtraKeyEntity> ExtraKeys { get; set; }

        public string Id { get; init; }
        public string AuthorId { get; init; }
        public string ParentProductId { get; init; }
        public long State { get; init; } // ToDo: Add StateEnum
        public string DisplayName { get; init; }
        public string Description { get; init; }
        public long CreationDate { get; init; }
        public List<string> SubProductsIds { get; init; }
        public List<string> SensorsIds { get; init; }
    }
}