using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public bool IsExportRunning => database.IsExportRunning;

        public List<string> Databases => [.. database.SensorValuesDatabases.Select(x => x.Name)];
    }
}
