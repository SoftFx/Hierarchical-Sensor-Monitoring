using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class ProductEntity
    {
        public Guid Id { get; set; }
        public Guid AuthorId { get; set; }
        public Guid ParentProductId { get; set; }
        public byte State { get; set; } // ToDo: Add StateEnum
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public DateTime CreationDate { get; set; }
        public List<Guid> SubProductsIds { get; set; }
        public List<Guid> SensorsIds { get; set; }
    }
}