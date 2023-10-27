namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public abstract record BaseSiteEntity
    {
        public byte[] Id { get; init; }

        public byte[] Author { get; init; }


        public string Name { get; init; }

        public string Description { get; init; }

        public long CreationDate { get; init; }
    }
}