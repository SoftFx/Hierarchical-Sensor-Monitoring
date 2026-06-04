namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record McpAccessKeyEntity
    {
        public string Id { get; init; }

        public string UserId { get; init; }

        public byte State { get; init; }

        public long Permissions { get; init; }

        public string DisplayName { get; init; }

        public long CreationTime { get; init; }

        public long ExpirationTime { get; init; }
    }
}
