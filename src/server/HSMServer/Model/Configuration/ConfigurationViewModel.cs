using HSMServer.ServerConfiguration;

namespace HSMServer.Model.Configuration
{
    public class ConfigurationViewModel(IServerConfig config, bool isBotRunning)
    {
        public ServerSettingsViewModel Server { get; } = new(config);

        public BackupSettingsViewModel Backup { get; } = new(config);

        public TelegramSettingsViewModel Telegram { get; } = new(config, isBotRunning);

        public MonitoringSettingsViewModel Monitoring { get; } = new(config);
    }
}
