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
        private readonly IServerConfig _config;


        private bool IsBackupEnabled => _config.BackupDatabase.IsEnabled;

        private TimeSpan StoragePeriod => TimeSpan.FromDays(_config.BackupDatabase.StoragePeriodDays);


        public override TimeSpan Delay => TimeSpan.FromHours(_config.BackupDatabase.PeriodHours);


        public BackupDatabaseService(IDatabaseCore database, IServerConfig config)
        {
            _config = config;
            _database = database;
        }


        protected override Task ServiceAction()
        {
            if (!IsBackupEnabled)
                return Task.CompletedTask;


            void EnvironmentBackup(string path) => _database.BackupEnvironment(path);

            void DashboardsBackup(string path) => _database.Dashboards.Backup(path);


            Backup(_dbSettings.EnvironmentDatabaseName, EnvironmentBackup);
            DeleteOldBackups(_dbSettings.EnvironmentDatabaseName);

            Backup(_dbSettings.ServerLayoutDatabaseName, DashboardsBackup);
            DeleteOldBackups(_dbSettings.ServerLayoutDatabaseName);


            return Task.CompletedTask;
        }

        private void Backup(string dbName, Action<string> backupAction)
        {
            try
            {
                var backupPath = Path.Combine(_dbSettings.DatabaseBackupsFolder, $"{dbName}_{DateTime.UtcNow.ToWindowsFormat()}");
                var backupDb = Directory.CreateDirectory(backupPath);

                backupAction(backupDb.FullName);
            }
            catch (Exception ex)
            {
                _logger.Error($"{dbName} database backup error: {ex}");
            }
        }

        private void DeleteOldBackups(string dbName)
        {
            try
            {
                var now = DateTime.UtcNow;

                var backupsDirectories =
                   Directory.GetDirectories(_dbSettings.DatabaseBackupsFolder, $"{dbName}*", SearchOption.TopDirectoryOnly)
                            .Select(d => new BackupDirectory(d, Directory.GetCreationTimeUtc(d)))
                            .OrderBy(d => d.CreationTime)
                            .ToList();

                for (int i = 0; i < backupsDirectories.Count - 1; ++i) // all backups except the last one
                {
                    var backup = backupsDirectories[i];

                    try
                    {
                        var creationTime = backup.CreationTime;

                        if (creationTime < (now - StoragePeriod) || creationTime.Date == now.Date)
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
                _logger.Error($"Getting all {dbName} database backups error: {ex}");
            }
        }


        private record BackupDirectory(string Name, DateTime CreationTime);
    }
}
