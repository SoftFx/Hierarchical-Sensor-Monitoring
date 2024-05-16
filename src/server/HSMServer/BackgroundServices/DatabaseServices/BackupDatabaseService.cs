using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Extensions;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HSMServer.BackgroundServices
{
    public class BackupDatabaseService : BaseDelayedBackgroundService
    {
        private readonly IDatabaseSettings _dbSettings = new DatabaseSettings();
        private readonly IDatabaseCore _database;
        private readonly IServerConfig _config;

        private readonly TimeSpan _storagePeriod;


        private bool IsBackupEnabled => _config.BackupDatabase.IsEnabled;


        public override TimeSpan Delay { get; }


        public BackupDatabaseService(IDatabaseCore database, IServerConfig config)
        {
            _config = config;
            _database = database;

            _storagePeriod = TimeSpan.FromDays(config.BackupDatabase.StoragePeriodDays);
            Delay = TimeSpan.FromHours(config.BackupDatabase.PeriodHours);
        }

        public async Task<string> CreateBackup()
        {
            try
            {
                await ServiceAction();
                return string.Empty;
            }
            catch (Exception ex) 
            {
                var msg = $"An error ({ex.Message}) has been occurred while create backup.";
                _logger.Error(ex, msg);
                return msg;
            }
        }

        protected override async Task ServiceAction()
        {
            if (!IsBackupEnabled)
                return;


            void EnvironmentBackup(string path) => _database.BackupEnvironment(path);

            void DashboardsBackup(string path) => _database.Dashboards.Backup(path);


            Backup(_dbSettings.EnvironmentDatabaseName, EnvironmentBackup);
            DeleteOldBackups(_dbSettings.EnvironmentDatabaseName);

            Backup(_dbSettings.ServerLayoutDatabaseName, DashboardsBackup);
            DeleteOldBackups(_dbSettings.ServerLayoutDatabaseName);

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
                _logger.Error($"Getting all {dbName} database backups error: {ex}");
            }
        }


        private record BackupDirectory(string Name, DateTime CreationTime);
    }
}
