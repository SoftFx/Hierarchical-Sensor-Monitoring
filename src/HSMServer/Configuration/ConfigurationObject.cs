namespace HSMServer.Configuration
{
    public class ConfigurationObject
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public static ConfigurationObject CreateConfiguration(string name, string value)
        {
            return new ConfigurationObject()
            {
                Name = name,
                Value = value
            };
        }
    }
}
