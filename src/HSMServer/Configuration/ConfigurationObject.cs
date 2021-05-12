namespace HSMServer.Configuration
{
    internal class ConfigurationObject
    {
        public int MaxPathLength { get; set; }

        public static ConfigurationObject CreateDefaultObject()
        {
            return new ConfigurationObject() {MaxPathLength = ConfigurationConstants.DefaultMaxPathLength};
        }
    }
}
