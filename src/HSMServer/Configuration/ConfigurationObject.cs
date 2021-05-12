using HSMServer.Constants;

namespace HSMServer.Configuration
{
    public class ConfigurationObject
    {
        public int MaxPathLength { get; set; }

        internal static ConfigurationObject CreateDefaultObject()
        {
            return new ConfigurationObject() {MaxPathLength = ConfigurationConstants.DefaultMaxPathLength};
        }
    }
}
