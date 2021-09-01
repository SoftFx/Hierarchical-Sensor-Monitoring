using HSMDatabase.Entity;
using HSMServer.Attributes;

namespace HSMServer.DataLayer.Model
{
    [SwaggerIgnore]
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
