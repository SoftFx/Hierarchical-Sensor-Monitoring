using HSMDatabase.Entity;

namespace HSMServer.Core.Model
{
    public class ConfigurationObject
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }

        public static ConfigurationObject CreateConfiguration(string name, string value, string description)
        {
            return new ConfigurationObject()
            {
                Name = name,
                Value = value,
                Description = description
            };
        }

        public ConfigurationObject(){}
        public ConfigurationObject(ConfigurationEntity entity)
        {
            if (entity == null) return;

            Name = entity.Name;
            Value = entity.Value;
        }
    }
}
