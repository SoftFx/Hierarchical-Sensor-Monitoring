using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.Entity
{
    public class ExtraKeyEntity : IExtraKeyEntity
    {
        public string Name { get; set; }
        public string Key { get; set; }
    }
}