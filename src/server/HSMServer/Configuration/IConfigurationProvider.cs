using HSMServer.Core.Configuration;
using System.Collections.Generic;

namespace HSMServer.Configuration
{
    public interface IConfigurationProvider
    {
        string ClientAppFolderPath { get; }

        List<string> GetAllParameterNames();
        void AddConfigurationObject(string name, string value);
        void SetConfigurationObjectToDefault(string name);
        ConfigurationObject ReadConfigurationObject(string name);
        /// <summary>
        /// Try reading the configuration object from the database. Return the obtained value if exists, default value otherwise.
        /// </summary>
        /// <param name="name">The parameter name, which MUST be a member of <see cref="ConfigurationConstants"/> class.</param>
        /// <returns>A <see cref="ConfigurationObject"/> entity, containing the parameter value.</returns>
        ConfigurationObject ReadOrDefault(string name);
        /// <summary>
        /// Get list of all configuration objects, which names are specified in <see cref="ConfigurationConstants"/> class.
        /// Method <see cref="ReadOrDefault"/> is used to retrieve values.
        /// </summary>
        /// <returns>List of <see cref="ConfigurationObject"/>.</returns>
        List<ConfigurationObject> GetAllConfigurationObjects();
    }
}