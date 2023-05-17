using HSMCommon.Constants;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMServer.Configuration
{
    public class ConfigurationProvider : IConfigurationProvider
    {
        #region Private fields

        private readonly IDatabaseCore _databaseCore;
        
        private readonly ILogger<ConfigurationProvider> _logger;
        
        private readonly List<string> _configurationObjectNamesList = new()
        {
            ConfigurationConstants.SensorExpirationTime, 
            ConfigurationConstants.BotName,
            ConfigurationConstants.BotToken,
            ConfigurationConstants.AreBotMessagesEnabled
        };
        
        
        private string _clientAppFolderPath;
        
        #endregion

        public ConfigurationProvider(IDatabaseCore databaseCore, ILogger<ConfigurationProvider> logger)
        {
            _logger = logger;
            _databaseCore = databaseCore;
            _logger.LogInformation("ConfigurationProvider initialized.");
        }

        #region Public interface implementation

        public string ClientAppFolderPath => _clientAppFolderPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CommonConstants.ClientAppFolderName);

        public List<string> GetAllParameterNames() => _configurationObjectNamesList;

        public void AddConfigurationObject(string name, string value)
        {
            var config = new ConfigurationObject() { Name = name, Value = value };
            _databaseCore.WriteConfigurationObject(config);
        }

        public void SetConfigurationObjectToDefault(string name)
        {
            _databaseCore.RemoveConfigurationObject(name);
        }

        ///Use 'name' from ConfigurationConstants! 
        public ConfigurationObject ReadOrDefault(string name)
        {
            var currentObject = _databaseCore.GetConfigurationObject(name);
            return currentObject ?? ConfigurationObject.CreateConfiguration(name,
                ConfigurationConstants.GetDefault(name), ConfigurationConstants.GetDescription(name));
        }

        public List<ConfigurationObject> GetAllConfigurationObjects() => _configurationObjectNamesList.Select(name => ReadOrDefault(name)).ToList();

        public ConfigurationObject ReadConfigurationObject(string name)
        {
            var objectFromDB = _databaseCore.GetConfigurationObject(name);
            if (objectFromDB != null)
                objectFromDB.Description = ConfigurationConstants.GetDescription(name);
            
            return objectFromDB;
        }

        #endregion
    }
}
