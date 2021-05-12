using System;
using System.IO;
using HSMCommon.Model;
using HSMServer.Constants;
using HSMServer.DataLayer;
using NLog;

namespace HSMServer.Configuration
{
    public class ConfigurationProvider : IConfigurationProvider
    {
        #region Private fields

        private readonly IDatabaseClass _database;
        private readonly ILogger _logger;
        private ClientVersionModel _clientVersion;
        private string _clientAppFolderPath;
        private ConfigurationObject _currentConfigurationObject;
        #endregion

        public ConfigurationProvider(IDatabaseClass database)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _database = database;
        }

        #region Public interface implementation

        public string ClientAppFolderPath => _clientAppFolderPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TextConstants.ClientAppFolderName);
        public ClientVersionModel ClientVersion => _clientVersion ??= ReadClientVersion();

        public ConfigurationObject CurrentConfigurationObject =>
            _currentConfigurationObject ??= ReadConfigurationObject();
        public void UpdateConfigurationObject(ConfigurationObject newObject)
        {
            _currentConfigurationObject = newObject;
            SaveConfigurationObject(newObject);
            OnConfigurationObjectUpdated(newObject);
        }

        public event EventHandler<ConfigurationObject> ConfigurationObjectUpdated;

        #endregion

        private void OnConfigurationObjectUpdated(ConfigurationObject newObject)
        {
            ConfigurationObjectUpdated?.Invoke(this, newObject);
        }
        private void SaveConfigurationObject(ConfigurationObject configurationObject)
        {
            _database.WriteConfigurationObject(configurationObject);
        }
        private ConfigurationObject ReadConfigurationObject()
        {
            var currentObject = _database.ReadConfigurationObject();
            return currentObject ?? ConfigurationObject.CreateDefaultObject();
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
                _logger.Error(e, "Failed to read client app version!");
            }
            return new ClientVersionModel() {ExtraVersion = 0, MainVersion = 0, SubVersion = 0, Postfix = "Failed to read!"};            
        }
    }
}
