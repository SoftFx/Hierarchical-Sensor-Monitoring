using HSMServer.Attributes;
using HSMServer.BackgroundServices;
using HSMServer.Model.Configuration;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Sftp;
using System;
using System.IO;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ConfigurationController(IServerConfig config, NotificationsCenter notifications, BackupDatabaseService backupService) : Controller
    {
        private readonly TelegramBot _telegramBot = notifications.TelegramBot;


        public IActionResult Index() => View(new ConfigurationViewModel(config, _telegramBot.IsBotRunning));

        [HttpPost]
        public IActionResult SaveServerSettings(ServerSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                config.Kestrel.SensorPort = settings.SensorsPort;
                config.Kestrel.SitePort = settings.SitePort;

                config.ServerCertificate.Name = settings.CertificateName;
                config.ServerCertificate.Key = settings.CertificateKey;

                config.ResaveSettings();
            }

            return PartialView("_Server", settings);
        }

        [HttpPost]
        public IActionResult SaveBackupSettings(BackupSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                config.BackupDatabase.IsEnabled = settings.IsEnabled;
                config.BackupDatabase.PeriodHours = settings.BackupPeriodHours;
                config.BackupDatabase.StoragePeriodDays = settings.BackupStoragePeriodDays;

                config.BackupDatabase.SftpConnectionConfig.IsEnabled  = settings.IsSftpEnabled;
                config.BackupDatabase.SftpConnectionConfig.Address    = settings.Address;
                config.BackupDatabase.SftpConnectionConfig.Port       = settings.Port;
                config.BackupDatabase.SftpConnectionConfig.Username   = settings.Username;
                config.BackupDatabase.SftpConnectionConfig.Password   = settings.Password;
                config.BackupDatabase.SftpConnectionConfig.RootPath   = settings.RootPath;
                if (settings.PrivateKey != null)
                {
                    using (var stream = new StreamReader(settings.PrivateKey.OpenReadStream()))
                    {
                        config.BackupDatabase.SftpConnectionConfig.PrivateKey = stream.ReadToEnd();
                    }

                    config.BackupDatabase.SftpConnectionConfig.PrivateKeyFileName = settings.PrivateKey.FileName;
                }
                else
                {
                    settings.PrivateKeyFileName = config.BackupDatabase.SftpConnectionConfig.PrivateKeyFileName;
                }

                config.ResaveSettings();
            }

            return PartialView("_Backup", settings);
        }

        [HttpPost]
        public IActionResult SaveMonitoringSettings(MonitoringSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                config.MonitoringOptions.IsMonitoringEnabled = settings.IsMonitoringEnabled;
                config.MonitoringOptions.TopHeaviestSensorsCount = settings.TopHeaviestSensorsCount;
                config.MonitoringOptions.DatabaseStatisticsPeriodDays = settings.DatabaseStatisticsPeriodDays;

                config.ResaveSettings();
            }

            return PartialView("_SelfMonitoring", settings);
        }

        [HttpPost]
        public IActionResult SaveTelegramSettings(TelegramSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                config.Telegram.IsRunning = settings.IsEnabled;
                config.Telegram.BotToken = settings.BotToken;
                config.Telegram.BotName = settings.BotName;

                config.ResaveSettings();
            }

            return PartialView("_Telegram", settings);
        }

        [HttpGet]
        public Task<string> RestartTelegramBot() => _telegramBot.StartBotAsync();


        [HttpGet]
        public Task<string> CreateBackup() => backupService.CreateBackupAsync();

        [HttpPost]
        public Task<string> CheckSftpConnection(BackupSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                var connection = new SftpConnectionConfig()
                {
                    Address    = settings.Address,
                    Port       = settings.Port,
                    Username   = settings.Username,
                    Password   = settings.Password,
                    RootPath   = settings.RootPath
                };

                if (settings.PrivateKey != null)
                {
                    using (var stream = new StreamReader(settings.PrivateKey.OpenReadStream()))
                    {
                        connection.PrivateKey = stream.ReadToEnd();
                    }
                }

                return backupService.CheckSftpWritePermisionAsync(connection);
            }

            return Task.FromResult("ViewModel is invalid!");
        }
    }
}