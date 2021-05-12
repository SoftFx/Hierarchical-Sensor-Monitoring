using HSMCommon.Model;

namespace HSMServer.Configuration
{
    internal interface IConfigurationProvider
    {
        ClientVersionModel ClientVersion { get; }
    }
}