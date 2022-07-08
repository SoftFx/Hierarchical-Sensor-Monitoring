namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record PolicyEntity
    {
        public string Id { get; init; }

        public object Policy { get; init; }
    }
}
