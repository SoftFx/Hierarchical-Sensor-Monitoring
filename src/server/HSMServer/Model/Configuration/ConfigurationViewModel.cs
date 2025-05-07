using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using System;

namespace HSMServer.Model.Configuration
{
    public class ConfigurationViewModel(IServerConfig config, bool isBotRunning, IDatabaseCore database)
    {
        public ServerSettingsViewModel Server { get; } = new(config);

        public BackupSettingsViewModel Backup { get; } = new(config);

        public TelegramSettingsViewModel Telegram { get; } = new(config, isBotRunning);

        public MonitoringSettingsViewModel Monitoring { get; } = new(config);

        public double TotalDbSize => Math.Round(database.TotalDbSize / (double)(1<<20), 2, MidpointRounding.AwayFromZero);

        public bool IsCompactRunning => database.IsCompactRunning;

    }
}
