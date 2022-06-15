namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record AccessKeyEntity
    {
        public string Id { get; init; }
        public string AuthorId { get; init; }
        public string ProductId { get; init; }
        public string Comment { get; init; }
        public byte KeyState { get; init; }
        public long KeyPermissions { get; init; }
        public string DisplayName { get; init; }
        public long CreationTime { get; init; }
        public long ExpirationTime { get; init; }
    }
}