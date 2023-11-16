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

        private readonly TimeSpan _storagePeriod;


        public override TimeSpan Delay { get; }


        public BackupDatabaseService(IDatabaseCore database, IServerConfig config)
        {
            _database = database;

            _storagePeriod = TimeSpan.FromDays(config.BackupDatabase.StoragePeriodDays);
            Delay = TimeSpan.FromHours(config.BackupDatabase.PeriodHours);
        }


        protected override Task ServiceAction()
        {
            Backup();
            DeleteOldBackups();

            return Task.CompletedTask;
        }

        private void Backup()
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
        }

        private void DeleteOldBackups()
        {
            var environmentBackupsDirectories =
               Directory.GetDirectories(_dbSettings.DatabaseBackupsFolder, $"{_dbSettings.EnvironmentDatabaseName}*", SearchOption.TopDirectoryOnly);


            foreach (var backup in environmentBackupsDirectories)
            {
                try
                {
                    if (!Directory.Exists(backup))
                        continue;

                    if (Directory.GetCreationTimeUtc(backup) < (DateTime.UtcNow - _storagePeriod))
                        Directory.Delete(backup, true);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Deleting '{backup}' error: {ex}");
                }
            }
        }
    }
}
