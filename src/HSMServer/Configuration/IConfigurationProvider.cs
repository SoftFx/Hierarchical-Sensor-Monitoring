using System;
using HSMCommon.Model;

namespace HSMServer.Configuration
{
    public interface IConfigurationProvider
    {
        string ClientAppFolderPath { get; }
        ClientVersionModel ClientVersion { get; }

        event EventHandler<ConfigurationObject> ConfigurationObjectUpdated;

        void AddConfigurationObject(string name, string value);
        void UpdateConfigurationObject(ConfigurationObject newObject);
        ConfigurationObject ReadConfigurationObject(string name);
    }
}