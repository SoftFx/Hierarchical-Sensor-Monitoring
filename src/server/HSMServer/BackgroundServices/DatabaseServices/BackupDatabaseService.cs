using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class BackupDatabaseService : BaseDelayedBackgroundService
    {
        private readonly IDatabaseSettings _dbSettings = new DatabaseSettings();
        private readonly IDatabaseCore _database;


        public override TimeSpan Delay { get; }


        public BackupDatabaseService(IDatabaseCore database, IServerConfig config)
        {
            _database = database;

            Delay = TimeSpan.FromHours(config.BackupDatabase.PeriodHours);
        }


        protected override Task ServiceAction()
        {
            try
            {
                var backupPath = Path.Combine(_dbSettings.DatabaseBackupsFolder, $"{_dbSettings.EnvironmentDatabaseName}_{DateTime.UtcNow.ToWindowsFormat()}");
                var backupDb = Directory.CreateDirectory(backupPath);

                _database.BackupEnvironment(backupDb.FullName);
            }
            catch (Exception ex)
            {
                _logger.Error($"Environment database backup error: {ex}");
            }

            return Task.CompletedTask;
        }
    }
}
