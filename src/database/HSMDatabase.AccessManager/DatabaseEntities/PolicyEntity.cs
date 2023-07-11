namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record PolicyEntity
    {
        public string Id { get; init; }

        public byte[] Policy { get; init; }
    }
}
