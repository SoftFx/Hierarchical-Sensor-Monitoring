using HSMCommon.Constants;
using HSMCommon.Model;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSMServer.Core.Configuration
{
    public class ConfigurationProvider : IConfigurationProvider
    {
        #region Private fields

        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<ConfigurationProvider> _logger;
        private ClientVersionModel _clientVersion;
        private string _clientAppFolderPath;
        private readonly List<string> _configurationObjectNamesList = new List<string>
        {
            ConfigurationConstants.MaxPathLength, ConfigurationConstants.AesEncryptionKey,
            ConfigurationConstants.SensorExpirationTime, ConfigurationConstants.SMTPServer, 
            ConfigurationConstants.SMTPPort, ConfigurationConstants.SMTPLogin, 
            ConfigurationConstants.SMTPPassword, ConfigurationConstants.SMTPFromEmail,
            ConfigurationConstants.ServerCertificatePassword, ConfigurationConstants.BotName,
            ConfigurationConstants.BotToken, ConfigurationConstants.AreBotMessagesEnabled
        };

        #endregion

        public ConfigurationProvider(IDatabaseCore databaseCore, ILogger<ConfigurationProvider> logger)
        {
            _logger = logger;
            _databaseCore = databaseCore;
            _logger.LogInformation("ConfigurationProvider initialized.");
        }

        #region Public interface implementation

        public string ClientAppFolderPath => _clientAppFolderPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CommonConstants.ClientAppFolderName);

        public List<string> GetAllParameterNames()
        {
            return _configurationObjectNamesList;
        }

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
        public ConfigurationObject ReadOrDefaultConfigurationObject(string name)
        {
            var currentObject = _databaseCore.GetConfigurationObject(name);
            return currentObject ?? ConfigurationObject.CreateConfiguration(name,
                ConfigurationConstants.GetDefault(name), ConfigurationConstants.GetDescription(name));
        }

        public List<ConfigurationObject> GetAllConfigurationObjects()
        {
            List<ConfigurationObject> result = new List<ConfigurationObject>();
            foreach (var name in _configurationObjectNamesList)
            {
                result.Add(ReadOrDefaultConfigurationObject(name));
            }

            return result;
        }

        public ConfigurationObject ReadConfigurationObject(string name)
        {
            var objectFromDB = _databaseCore.GetConfigurationObject(name);
            if (objectFromDB != null)
            {
                objectFromDB.Description = ConfigurationConstants.GetDescription(name);
            }
            return objectFromDB;
        }

        #endregion
    }
}
