namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public interface IBaseEntity
    {
        public byte[] Id { get; }
    }


    public abstract record BaseServerEntity : IBaseEntity
    {
        public byte[] Id { get; init; }

        public byte[] Author { get; init; }


        public string Name { get; init; }

        public string Description { get; init; }

        public long CreationDate { get; init; }
    }
}