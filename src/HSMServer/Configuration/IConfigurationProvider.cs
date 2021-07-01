using System;
using HSMCommon.Model;

namespace HSMServer.Configuration
{
    public interface IConfigurationProvider
    {
        string ClientAppFolderPath { get; }
        ClientVersionModel ClientVersion { get; }
        ConfigurationObject CurrentConfigurationObject { get; }
        void UpdateConfigurationObject(ConfigurationObject newObject);
        event EventHandler<ConfigurationObject> ConfigurationObjectUpdated; 
    }
}