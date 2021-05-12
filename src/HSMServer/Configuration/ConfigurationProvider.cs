using HSMCommon.Model;
using HSMServer.DataLayer;
using NLog;

namespace HSMServer.Configuration
{
    internal class ConfigurationProvider : IConfigurationProvider
    {
        #region Private fields

        private readonly IDatabaseClass _database;
        private readonly ILogger _logger;
        private ClientVersionModel _clientVersion;

        #endregion

        public ConfigurationProvider(IDatabaseClass database)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _database = database;
        }

        #region Public fields

        public ClientVersionModel ClientVersion => _clientVersion ??= ReadClientVersion();

        #endregion


        private ClientVersionModel ReadClientVersion()
        {
            throw new System.NotImplementedException();
        }
    }
}
