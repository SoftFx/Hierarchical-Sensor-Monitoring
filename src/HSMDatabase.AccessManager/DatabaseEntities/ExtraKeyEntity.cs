using System;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    [Obsolete]
    public class ExtraKeyEntity
    {
        public string Name { get; set; }
        public string Key { get; set; }
    }
    public sealed record AccessKeyEntity
    {
        public string Id { get; init; }
        public string AuthorId { get; init; }
        public string ProductId { get; init; }
        public bool IsLocked { get; init; }
        public byte KeyRole { get; init; }
        public string DisplayName { get; init; }
        public long CreationTime { get; init; }
        public long ExpirationTime { get; init; }
    }
}