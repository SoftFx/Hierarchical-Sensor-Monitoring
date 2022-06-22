using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMServer.Core.Model;

namespace HSMServer.Core.Configuration
{
    public interface IConfigurationProvider
    {
        string ClientAppFolderPath { get; }
        ClientVersionModel ClientVersion { get; }

        List<string> GetAllParameterNames();
        void AddConfigurationObject(string name, string value);
        void SetConfigurationObjectToDefault(string name);
        ConfigurationObject ReadConfigurationObject(string name);
        /// <summary>
        /// Try reading the configuration object from the database. Return the obtained value if exists, default value otherwise.
        /// </summary>
        /// <param name="name">The parameter name, which MUST be a member of <see cref="ConfigurationConstants"/> class.</param>
        /// <returns>A <see cref="ConfigurationObject"/> entity, containing the parameter value.</returns>
        ConfigurationObject ReadOrDefaultConfigurationObject(string name);
        /// <summary>
        /// Get list of all configuration objects, which names are specified in <see cref="ConfigurationConstants"/> class.
        /// Method <see cref="ReadOrDefaultConfigurationObject"/> is used to retrieve values.
        /// </summary>
        /// <returns>List of <see cref="ConfigurationObject"/>.</returns>
        List<ConfigurationObject> GetAllConfigurationObjects();

        string GetCurrentVersion();
    }
}