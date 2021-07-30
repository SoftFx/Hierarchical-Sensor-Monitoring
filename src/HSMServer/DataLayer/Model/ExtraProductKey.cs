using HSMDatabase.Entity;

namespace HSMServer.DataLayer.Model
{
    public class ExtraProductKey
    {
        public string Name { get; set; }
        public string Key { get; set; }

        public ExtraProductKey() {}
        public ExtraProductKey(string name, string key)
        {
            Name = name;
            Key = key;
        }

        public ExtraProductKey(ExtraKeyEntity entity)
        {
            Name = entity.Name;
            Key = entity.Key;
        }
    }
}
