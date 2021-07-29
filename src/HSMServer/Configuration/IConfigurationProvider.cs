using System;
using HSMCommon.Model;
using HSMServer.Constants;

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
        /// <summary>
        /// Try reading the configuration object from the database. Return the obtained value if exists, default value otherwise.
        /// </summary>
        /// <param name="name">The parameter name, which MUST be a member of <see cref="ConfigurationConstants"/> class.</param>
        /// <returns>A <see cref="ConfigurationObject"/> entity, containing the parameter value.</returns>
        ConfigurationObject ReadOrDefaultConfigurationObject(string name);
    }
}