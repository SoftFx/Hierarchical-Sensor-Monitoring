using System;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class ExtraKeyEntity
    {
        public Guid Id { get; set; }
        public Guid AuthorId { get; set; }
        public Guid ProductId { get; set; }
        public bool IsLocked { get; set; }
        public byte KeyRole { get; set; }
        public string DisplayName { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime ExpirationTime { get; set; }
    }
}