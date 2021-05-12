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

        #endregion

        public ConfigurationProvider(IDatabaseClass database)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _database = database;
        }

        #region Public fields

        public string ClientAppFolderPath => _clientAppFolderPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TextConstants.ClientAppFolderName);
        public ClientVersionModel ClientVersion => _clientVersion ??= ReadClientVersion();

        #endregion


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
