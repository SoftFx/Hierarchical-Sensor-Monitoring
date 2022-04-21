using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record ProductEntity
    {   
        public string Id { get; init; }
        public string AuthorId { get; init; }
        public string ParentProductId { get; init; }
        public int State { get; init; } 
        public string DisplayName { get; init; }
        public string Description { get; init; }
        public long CreationDate { get; init; }
        public long DateAdded { get; set; }
        public List<string> SubProductsIds { get; init; }
        public List<string> SensorsIds { get; init; }

        //ToDo: Remove
        public bool IsConverted { get; init; }
    }
}