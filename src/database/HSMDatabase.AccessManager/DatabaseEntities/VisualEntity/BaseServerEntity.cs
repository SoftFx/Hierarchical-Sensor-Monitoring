namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public abstract record BaseServerEntity
    {
        public byte[] Id { get; init; }

        public byte[] Author { get; init; }


        public string Name { get; init; }

        public string Description { get; init; }

        public long CreationDate { get; init; }
    }
}