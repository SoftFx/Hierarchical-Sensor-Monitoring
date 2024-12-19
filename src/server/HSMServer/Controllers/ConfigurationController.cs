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
using NLog;
using HSMCommon.Extensions;
using System.Text;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ConfigurationController(IServerConfig config, NotificationsCenter notifications, BackupDatabaseService backupService) : Controller
    {
        private readonly TelegramBot _telegramBot = notifications.TelegramBot;

        protected readonly Logger _logger = LogManager.GetLogger(typeof(ConfigurationController).Name);

        public IActionResult Index() => View(new ConfigurationViewModel(config, _telegramBot.IsBotRunning));

        [HttpPost]
        public IActionResult SaveServerSettings(ServerSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                string changes = BuildServerChangesText(config, settings, GetUserName());
                if (string.IsNullOrEmpty(changes))
                    _logger.Warn("SaveServerSettings: no changes detected");
                else
                {
                    _logger.Info($"SaveServerSettings: {changes}");

                    config.Kestrel.SensorPort = settings.SensorsPort;
                    config.Kestrel.SitePort = settings.SitePort;

                    config.ServerCertificate.Name = settings.CertificateName;
                    config.ServerCertificate.Key = settings.CertificateKey;

                    config.ResaveSettings();
                }
            }

            return PartialView("_Server", settings);
        }

        [HttpPost]
        public IActionResult SaveBackupSettings(BackupSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                string changes = BuildBackupChangesText(config.BackupDatabase, settings, GetUserName());
                if (string.IsNullOrEmpty(changes))
                    _logger.Warn("SaveBackupSettings: no changes detected");
                else
                    _logger.Info($"SaveBackupSettings: {changes}");

                config.BackupDatabase.IsEnabled = settings.IsEnabled;
                config.BackupDatabase.PeriodHours = settings.BackupPeriodHours;
                config.BackupDatabase.StoragePeriodDays = settings.BackupStoragePeriodDays;

                config.BackupDatabase.SftpConnectionConfig.IsEnabled = settings.IsSftpEnabled;
                config.BackupDatabase.SftpConnectionConfig.Address = settings.Address;
                config.BackupDatabase.SftpConnectionConfig.Port = settings.Port;
                config.BackupDatabase.SftpConnectionConfig.Username = settings.Username;
                config.BackupDatabase.SftpConnectionConfig.Password = settings.Password;
                config.BackupDatabase.SftpConnectionConfig.RootPath = settings.RootPath;

                if (settings.PrivateKey == null)
                {
                    settings.PrivateKeyFileName = config.BackupDatabase.SftpConnectionConfig.PrivateKeyFileName;
                }
                else
                {
                    using (var stream = new StreamReader(settings.PrivateKey.OpenReadStream()))
                    {
                        config.BackupDatabase.SftpConnectionConfig.PrivateKey = stream.ReadToEnd();
                    }

                    config.BackupDatabase.SftpConnectionConfig.PrivateKeyFileName = settings.PrivateKey.FileName;

                    _logger.Info($"SaveBackupSettings: SftpConnectionConfig.PrivateKey/PrivateKeyFileName were updated");
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
                string changes = BuildMonitoringChangesText(config.MonitoringOptions, settings, GetUserName());
                if (string.IsNullOrEmpty(changes))
                    _logger.Warn("SaveMonitoringSettings: no changes detected");
                else
                {
                    _logger.Info($"SaveMonitoringSettings: {changes}");

                    config.MonitoringOptions.IsMonitoringEnabled = settings.IsMonitoringEnabled;
                    config.MonitoringOptions.TopHeaviestSensorsCount = settings.TopHeaviestSensorsCount;
                    config.MonitoringOptions.DatabaseStatisticsPeriodDays = settings.DatabaseStatisticsPeriodDays;

                    config.ResaveSettings();
                }
            }

            return PartialView("_SelfMonitoring", settings);
        }

        [HttpPost]
        public IActionResult SaveTelegramSettings(TelegramSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                string changes = BuildTelegramChangesText(config.Telegram, settings, GetUserName());
                if (string.IsNullOrEmpty(changes))
                    _logger.Warn("SaveTelegramSettings: no changes detected");
                else
                {
                    _logger.Info($"SaveTelegramSettings: {changes}");

                    config.Telegram.IsRunning = settings.IsEnabled;
                    config.Telegram.BotToken = settings.BotToken;
                    config.Telegram.BotName = settings.BotName;

                    config.ResaveSettings();
                }
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

        private static string BuildServerChangesText(IServerConfig config, ServerSettingsViewModel settings, string userName)
        {
            StringBuilder sb = new StringBuilder();

            if (config.Kestrel.SensorPort != settings.SensorsPort)
                sb.AppendLine($"SensorPort: {config.Kestrel.SensorPort} -> {settings.SensorsPort}");

            if (config.Kestrel.SitePort != settings.SitePort)
                sb.AppendLine($"SitePort: {config.Kestrel.SitePort} -> {settings.SitePort}");

            if (config.ServerCertificate.Name != settings.CertificateName)
                sb.AppendLine($"ServerCertificate.Name: {config.ServerCertificate.Name} -> {settings.CertificateName}");

            if (config.ServerCertificate.Key != settings.CertificateKey)
                sb.AppendLine($"ServerCertificate.Key: {config.ServerCertificate.Key} -> {settings.CertificateKey}");

            string changes = sb.ToString();

            if (string.IsNullOrEmpty(changes))
                return null;

            return $"{userName} is changing server config:{Environment.NewLine}{sb.ToString()}";
        }

        private static string BuildTelegramChangesText(TelegramConfig telegramConfig, TelegramSettingsViewModel telegramSettings, string userName)
        {
            StringBuilder sb = new StringBuilder();

            if (telegramConfig.IsRunning != telegramSettings.IsEnabled)
                sb.AppendLine($"IsRunning: {telegramConfig.IsRunning} -> {telegramSettings.IsEnabled}");

            if (telegramConfig.BotToken != telegramSettings.BotToken)
                sb.AppendLine($"BotToken: {telegramConfig.BotToken} -> {telegramSettings.BotToken}");

            if (telegramConfig.BotName != telegramSettings.BotName)
                sb.AppendLine($"BotName: {telegramConfig.BotName} -> {telegramSettings.BotName}");

            string changes = sb.ToString();

            if (string.IsNullOrEmpty(changes))
                return null;

            return $"{userName} is changing Telegram config:{Environment.NewLine}{sb.ToString()}";
        }


        private static string BuildBackupChangesText(BackupDatabaseConfig backupConfig, BackupSettingsViewModel backupSettings, string userName)
        {
            StringBuilder sb = new StringBuilder();

            if (backupConfig.IsEnabled != backupSettings.IsEnabled)
                sb.AppendLine($"IsEnabled: {backupConfig.IsEnabled} -> {backupSettings.IsEnabled}");

            if (backupConfig.PeriodHours != backupSettings.BackupPeriodHours)
                sb.AppendLine($"PeriodHours: {backupConfig.PeriodHours} -> {backupSettings.BackupPeriodHours}");

            if (backupConfig.StoragePeriodDays != backupSettings.BackupStoragePeriodDays)
                sb.AppendLine($"StoragePeriodDays: {backupConfig.StoragePeriodDays} -> {backupSettings.BackupStoragePeriodDays}");


            if (backupConfig.SftpConnectionConfig.IsEnabled != backupSettings.IsSftpEnabled)
                sb.AppendLine($"SftpConnectionConfig.IsEnabled: {backupConfig.SftpConnectionConfig.IsEnabled} -> {backupSettings.IsSftpEnabled}");

            if (backupConfig.SftpConnectionConfig.Address != backupSettings.Address)
                sb.AppendLine($"SftpConnectionConfig.Address: {backupConfig.SftpConnectionConfig.Address} -> {backupSettings.Address}");

            if (backupConfig.SftpConnectionConfig.Port != backupSettings.Port)
                sb.AppendLine($"SftpConnectionConfig.Port: {backupConfig.SftpConnectionConfig.Port} -> {backupSettings.Port}");

            if (backupConfig.SftpConnectionConfig.Username != backupSettings.Username)
                sb.AppendLine($"SftpConnectionConfig.Username: {backupConfig.SftpConnectionConfig.Username} -> {backupSettings.Username}");

            if (backupConfig.SftpConnectionConfig.Password != backupSettings.Password)
                sb.AppendLine($"SftpConnectionConfig.Password: {backupConfig.SftpConnectionConfig.Password} -> {backupSettings.Password}");

            if (backupConfig.SftpConnectionConfig.RootPath != backupSettings.RootPath)
                sb.AppendLine($"SftpConnectionConfig.RootPath: {backupConfig.SftpConnectionConfig.RootPath} -> {backupSettings.RootPath}");



            string changes = sb.ToString();

            if (string.IsNullOrEmpty(changes))
                return null;

            return $"{userName} is changing Backup config:{Environment.NewLine}{sb.ToString()}";
        }

        private static string BuildMonitoringChangesText(MonitoringOptions options, MonitoringSettingsViewModel monitoringModel, string userName)
        {
            StringBuilder sb = new StringBuilder();

            if (options.IsMonitoringEnabled != monitoringModel.IsMonitoringEnabled)
                sb.AppendLine($"IsMonitoringEnabled: {options.IsMonitoringEnabled} -> {monitoringModel.IsMonitoringEnabled}");

            if (options.TopHeaviestSensorsCount != monitoringModel.TopHeaviestSensorsCount)
                sb.AppendLine($"TopHeaviestSensorsCount: {options.TopHeaviestSensorsCount} -> {monitoringModel.TopHeaviestSensorsCount}");

            if (options.DatabaseStatisticsPeriodDays != monitoringModel.DatabaseStatisticsPeriodDays)
                sb.AppendLine($"DatabaseStatisticsPeriodDays: {options.DatabaseStatisticsPeriodDays} -> {monitoringModel.DatabaseStatisticsPeriodDays}");

            string changes = sb.ToString();

            if (string.IsNullOrEmpty(changes))
                return null;

            return $"{userName} is changing Monitoring config:{Environment.NewLine}{sb.ToString()}";
        }

        private string GetUserName()
        {
            return (HttpContext.User as HSMServer.Model.Authentication.User)?.Name ?? string.Empty;
        }
    }
}