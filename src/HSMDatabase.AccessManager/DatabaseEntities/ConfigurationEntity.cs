using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.Entity
{
    public class ConfigurationEntity : IConfigurationEntity
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}