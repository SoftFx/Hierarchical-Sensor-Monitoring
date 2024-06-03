using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using HSMServer.Sftp;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;


namespace HSMServer.BackgroundServices
{
    public class BackupDatabaseService : BaseDelayedBackgroundService
    {
        private readonly IDatabaseSettings _dbSettings = new DatabaseSettings();
        private readonly IDatabaseCore _database;
        private readonly IServerConfig _config;

        private readonly BackupSensors _backupSensors;

        private bool IsBackupEnabled => _config.BackupDatabase.IsEnabled;

        private TimeSpan StoragePeriod => TimeSpan.FromDays(_config.BackupDatabase.StoragePeriodDays);

        private volatile bool _isBackupRunning;

        public override TimeSpan Delay => TimeSpan.FromHours(_config.BackupDatabase.PeriodHours);


        public BackupDatabaseService(IDatabaseCore database, IServerConfig config, DataCollectorWrapper datacollectorWrapper)
        {
            _config = config;
            _database = database;
            _backupSensors = datacollectorWrapper.BackupSensors;
        }

        public async Task<string> CheckSftpWritePermisionAsync(SftpConnectionConfig connection)
        {
            try
            {
                if (string.IsNullOrEmpty(connection.PrivateKey))
                    connection.PrivateKey = _config.BackupDatabase.SftpConnectionConfig.PrivateKey;

                using (var sftp = new SftpWrapper(connection, _logger))
                {
                    await sftp.CheckWritePermissionsAsync(connection.RootPath);
                }
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
                if (_isBackupRunning)
                    return "Backup already running";

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

            if (!_isBackupRunning)
            {
                _isBackupRunning = true;

                try
                {
                    var enviromentBackupResult = Backup(_dbSettings.EnvironmentDatabaseName, _database.BackupEnvironment);
                    if (enviromentBackupResult.IsOk)
                    {
                        _backupSensors.AddBackupCreateInfo(true, enviromentBackupResult.Value);
                        DeleteOldBackups(_dbSettings.EnvironmentDatabaseName);
                    }
                    else
                    {
                        _backupSensors.AddBackupCreateInfo(false, enviromentBackupResult.Error);
                    }

                    var dashboardBackupResult = Backup(_dbSettings.ServerLayoutDatabaseName, _database.Dashboards.Backup);
                    if (dashboardBackupResult.IsOk)
                    {
                        _backupSensors.AddBackupCreateInfo(true, dashboardBackupResult.Value);
                        DeleteOldBackups(_dbSettings.ServerLayoutDatabaseName);
                    }
                    else
                    {
                        _backupSensors.AddBackupCreateInfo(false, dashboardBackupResult.Error);
                    }

                    if (_config.BackupDatabase.SftpConnectionConfig.IsEnabled)
                        await SynchronizeSftpFolderAsync();
                }
                finally
                {
                    _isBackupRunning = false;
                }
            }
        }

        private TaskResult<string> Backup(string dbName, Func<string, TaskResult<string>> backupAction)
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

                var backupsFiles =
                   new DirectoryInfo(_dbSettings.DatabaseBackupsFolder).GetFiles($"{dbName}*", SearchOption.TopDirectoryOnly)
                            .OrderBy(d => d.CreationTime)
                            .ToList();

                for (int i = 0; i < backupsFiles.Count - 1; ++i) // all backups except the last one
                {
                    var backup = backupsFiles[i];

                    try
                    {
                        var creationTime = backup.CreationTime;

                        if (creationTime < (now - StoragePeriod) || creationTime.Date == now.Date)
                            backup.Delete();
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

        private async Task SynchronizeSftpFolderAsync()
        {

            try
            {
                using (var sftp = new SftpWrapper(_config.BackupDatabase.SftpConnectionConfig, _logger))
                {
                    var root = _config.BackupDatabase.SftpConnectionConfig.RootPath;

                    sftp.CreateAllDirectories(root);

                    var sftpFiles = sftp.ListDirectory(root).Where(x => x.IsDirectory == false).ToList();
                    foreach (var localFile in new DirectoryInfo(_dbSettings.DatabaseBackupsFolder).GetFiles())
                    {
                        var sftpFile = sftpFiles.FirstOrDefault(x => x.Name == localFile.Name);

                        if (sftpFile == null)
                        {
                            try
                            {
                                await sftp.UploadFileAsync(localFile.FullName, root);
                                _backupSensors.AddBackupUploadInfo(true, $"{localFile.FullName} uploaded to {sftp.Host}\\{root} successfuly");
                            }
                            catch (Exception ex)
                            {
                                _backupSensors.AddBackupUploadInfo(false, $"An error {ex.Message} has been occured while uploading {localFile.FullName} to {sftp.Host}/{root}");
                                throw;
                            }
                        }
                        else
                            sftpFiles.Remove(sftpFile);
                    }

                    foreach (var sftpFile in sftpFiles)
                    {
                        await sftp.DeleteFileAsync(sftpFile.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _backupSensors.AddBackupUploadInfo(false, $"An error {ex.Message} has been occured while connecting to sftp server: {_config.BackupDatabase.SftpConnectionConfig.Address}/{_config.BackupDatabase.SftpConnectionConfig.RootPath}");
                throw;
            }
        }

        private record BackupDirectory(string Name, DateTime CreationTime);
    }
}
