using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Extensions;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using HSMServer.Sftp;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;

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
        public SftpWrapper SftpWrapper { get; private set; }


        public BackupDatabaseService(IDatabaseCore database, IServerConfig config)
        {
            _config = config;
            _database = database;

            _storagePeriod = TimeSpan.FromDays(config.BackupDatabase.StoragePeriodDays);
            Delay = TimeSpan.FromHours(config.BackupDatabase.PeriodHours);

            if (config.BackupDatabase.SftpConnectionConfig.IsEnabled)
                SftpWrapper = new SftpWrapper(config.BackupDatabase.SftpConnectionConfig);
        }

        public string CheckSftpConnection(SftpConnectionConfig connection)
        {
            try
            {
                var sftp = new SftpWrapper(connection);
                sftp.CheckConnection();
                return string.Empty;
            }
            catch (Exception ex)
            {
                var msg = $"An error ({ex.Message}) has been occurred while check connection.";
                _logger.Error(ex, msg);
                return ex.Message;
            }
        }

        public async Task<string> CreateBackupAsync()
        {
            try
            {
                await ServiceActionAsync();
                return string.Empty;
            }
            catch (Exception ex) 
            {
                var msg = $"An error ({ex.Message}) has been occurred while create backup.";
                _logger.Error(ex, msg);
                return msg;
            }
        }

        protected override async Task ServiceActionAsync()
        {
            if (!IsBackupEnabled)
                return;

            string EnvironmentBackup(string path) => _database.BackupEnvironment(path);

            string DashboardsBackup(string path) => _database.Dashboards.Backup(path);


            var backupFileName = Backup(_dbSettings.EnvironmentDatabaseName, EnvironmentBackup);
            if (!string.IsNullOrEmpty(backupFileName))
            {
                await UploadBackupAsync(backupFileName);
                DeleteOldBackups(_dbSettings.EnvironmentDatabaseName);
            }

            backupFileName = Backup(_dbSettings.ServerLayoutDatabaseName, DashboardsBackup);
            if (!string.IsNullOrEmpty(backupFileName))
            {
                await UploadBackupAsync(backupFileName);
                DeleteOldBackups(_dbSettings.ServerLayoutDatabaseName);
            }
        }

        private string Backup(string dbName, Func<string, string> backupAction)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(Path.Combine(_dbSettings.DatabaseBackupsFolder, $"{dbName}_{DateTime.UtcNow.ToWindowsFormat()}"));

                return backupAction(directoryInfo.FullName);

            }
            catch (Exception ex)
            {
                _logger.Error($"{dbName} database backup error: {ex}");
                return null;
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

        private async Task UploadBackupAsync(string backupFileName)
        {
             SftpWrapper.UploadFile(backupFileName, SftpWrapper.RootPath, CancellationToken.None);
        }

        private record BackupDirectory(string Name, DateTime CreationTime);
    }
}
