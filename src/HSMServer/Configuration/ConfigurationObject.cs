using HSMServer.Constants;

namespace HSMServer.Configuration
{
    public class ConfigurationObject
    {
        public int MaxPathLength { get; set; }

        public static ConfigurationObject CreateDefaultObject()
        {
            return new ConfigurationObject() {MaxPathLength = ConfigurationConstants.DefaultMaxPathLength};
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || !GetType().Equals(obj.GetType()))
                return false;

            ConfigurationObject rhs = (ConfigurationObject) obj;
            return MaxPathLength == rhs.MaxPathLength;
        }
    }
}
