using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using System;
using System.IO;
using System.Linq;
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
            try
            {
                var now = DateTime.UtcNow;

                var environmentBackupsDirectories =
                   Directory.GetDirectories(_dbSettings.DatabaseBackupsFolder, $"{_dbSettings.EnvironmentDatabaseName}*", SearchOption.TopDirectoryOnly)
                            .Select(d => new BackupDirectory(d, Directory.GetCreationTimeUtc(d)))
                            .OrderBy(d => d.CreationTime)
                            .ToList();

                for (int i = 0; i < environmentBackupsDirectories.Count - 1; ++i) // all backups except the last one
                {
                    var backup = environmentBackupsDirectories[i];

                    try
                    {
                        var creationTime = backup.CreationTime;

                        if (creationTime < (now - _storagePeriod) || creationTime.Date == now.Date)
                            Directory.Delete(backup.Name, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Deleting '{backup}' error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Getting all environment database backups error: {ex}");
            }
        }


        private record BackupDirectory(string Name, DateTime CreationTime);
    }
}
