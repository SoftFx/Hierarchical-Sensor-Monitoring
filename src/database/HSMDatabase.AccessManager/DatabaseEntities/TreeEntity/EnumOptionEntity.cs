

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record EnumOptionEntity
    {
        public string Value { get; set; }
        public string Description { get; set; }
        public int Color { get; set; }
    }
}