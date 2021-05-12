using HSMCommon.Model;

namespace HSMServer.Configuration
{
    public interface IConfigurationProvider
    {
        string ClientAppFolderPath { get; }
        ClientVersionModel ClientVersion { get; }
    }
}