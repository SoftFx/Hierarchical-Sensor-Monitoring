namespace HSMCommon.Constants
{
    public class CommonConstants
    {
        public const string ClientAppFolderName = "Client";

        public const string DefaultAccessKey = "DefaultKey";

        public const char SensorPathSeparator = '/';

        public const string InfrastructureLoggerName = "InfrastructureLogger";

        // The root product of the server's own collector. The server finds it by this
        // name at every start and creates it when the name is not there, so the name
        // is the product's identity: the cache refuses to rename it.
        public const string SelfMonitoringProductName = "HSM Server Monitoring";
    }
}
