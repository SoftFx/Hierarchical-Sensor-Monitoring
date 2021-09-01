using System;
using System.Collections.Generic;
using System.IO;
using HSMCommon.Model;
using HSMServer.Constants;
using HSMServer.DataLayer;
using Microsoft.Extensions.Logging;

namespace HSMServer.Configuration
{
    public class ConfigurationProvider : IConfigurationProvider
    {
        #region Private fields

        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly ILogger<ConfigurationProvider> _logger;
        private ClientVersionModel _clientVersion;
        private string _clientAppFolderPath;
        private readonly List<string> _configurationObjectNamesList = new List<string>
        {
            ConfigurationConstants.MaxPathLength, ConfigurationConstants.AesEncryptionKey, 
            ConfigurationConstants.SensorExpirationTime, ConfigurationConstants.SMTPServer, ConfigurationConstants.SMTPPort,
            ConfigurationConstants.SMTPLogin, ConfigurationConstants.SMTPPassword, ConfigurationConstants.SMTPFromEmail,
            ConfigurationConstants.ServerCertificatePassword
        };
        #endregion

        public ConfigurationProvider(IDatabaseAdapter databaseAdapter, ILogger<ConfigurationProvider> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _logger.LogInformation("ConfigurationProvider initialized.");

            MigrateConfigurationObjectsToNewDatabase();
        }

        /// <summary>
        /// This method MUST be called when update from 2.1.4 or lower to 2.1.5 or higher
        /// </summary>
        private void MigrateConfigurationObjectsToNewDatabase()
        {
            var currentObjects = _databaseAdapter.GetAllConfigurationObjectsOld();
            foreach (var currentObject in currentObjects)
            {
                _databaseAdapter.WriteConfigurationObject(currentObject);   
            }
        }
        #region Public interface implementation

        public string ClientAppFolderPath => _clientAppFolderPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TextConstants.ClientAppFolderName);
        public ClientVersionModel ClientVersion => _clientVersion ??= ReadClientVersion();

        public List<string> GetAllParameterNames()
        {
            return _configurationObjectNamesList;
        }

        public void AddConfigurationObject(string name, string value)
        {
            var config = new ConfigurationObject() { Name = name, Value = value };
            //_databaseAdapter.WriteConfigurationObjectOld(config);
            _databaseAdapter.WriteConfigurationObject(config);
        }

        public void SetConfigurationObjectToDefault(string name)
        {
            //_databaseAdapter.RemoveConfigurationObjectOld(name);
            _databaseAdapter.RemoveConfigurationObject(name);
        }

        ///Use 'name' from ConfigurationConstants! 
        public ConfigurationObject ReadOrDefaultConfigurationObject(string name)
        {
            //var currentObject = _databaseAdapter.GetConfigurationObjectOld(name);
            var currentObject = _databaseAdapter.GetConfigurationObject(name);
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
            //var objectFromDB = _databaseAdapter.GetConfigurationObjectOld(name);
            var objectFromDB = _databaseAdapter.GetConfigurationObject(name);
            if (objectFromDB != null)
            {
                objectFromDB.Description = ConfigurationConstants.GetDescription(name);
            }
            return objectFromDB;
        }

        public event EventHandler<ConfigurationObject> ConfigurationObjectUpdated;

        #endregion

        private void OnConfigurationObjectUpdated(ConfigurationObject newObject)
        {
            ConfigurationObjectUpdated?.Invoke(this, newObject);
        }
        private void SaveConfigurationObject(ConfigurationObject configurationObject)
        {
            //_databaseAdapter.WriteConfigurationObjectOld(configurationObject);
            _databaseAdapter.WriteConfigurationObject(configurationObject);
        }

        private ClientVersionModel ReadClientVersion()
        {
            try
            {
                string versionFilePath = Path.Combine(ClientAppFolderPath, TextConstants.ClientVersionFileName);
                string text = File.ReadAllText(versionFilePath);
                return ClientVersionModel.Parse(text);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to read client app version!");
            }
            return new ClientVersionModel() {ExtraVersion = 0, MainVersion = 0, SubVersion = 0, Postfix = "Failed to read!"};            
        }
    }
}
