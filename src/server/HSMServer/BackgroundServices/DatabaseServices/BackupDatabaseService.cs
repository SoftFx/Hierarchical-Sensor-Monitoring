using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class BackupDatabaseService : BaseDelayedBackgroundService
    {
        private readonly IDatabaseSettings _dbSettings = new DatabaseSettings();


        public override TimeSpan Delay { get; }


        public BackupDatabaseService(IServerConfig config)
        {
            Delay = TimeSpan.FromMinutes(config.BackupDatabase.PeriodHours); // TODO: change to fromhours
        }


        protected override async Task ServiceAction()
        {
            try
            {
                var backupPath = Path.Combine(_dbSettings.DatabaseBackupsFolder, $"{_dbSettings.EnvironmentDatabaseName}_{DateTime.UtcNow.ToWindowsFormat()}");

                var environmentDb = new DirectoryInfo(_dbSettings.PathToEnvironmentDb);
                var backupDb = Directory.CreateDirectory(backupPath);

                foreach (var fileInfo in environmentDb.GetFiles())
                {
                    try
                    {
                        using var file = File.Open(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(file);
                        using var writer = new StreamWriter(Path.Combine(backupDb.FullName, fileInfo.Name));

                        await writer.WriteAsync(await reader.ReadToEndAsync());
                    }
                    catch(Exception ex)
                    {
                        _logger.Error($"Copying files from Environment database error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Environment database backup error: {ex}");
            }
        }
    }
}
