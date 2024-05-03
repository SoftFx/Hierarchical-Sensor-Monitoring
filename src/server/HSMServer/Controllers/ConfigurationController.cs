using HSMServer.Attributes;
using HSMServer.Model.Configuration;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ConfigurationController(IServerConfig config, NotificationsCenter notifications) : Controller
    {
        private readonly IServerConfig _config = config;
        private readonly TelegramBot _telegramBot = notifications.TelegramBot;


        public IActionResult Index() => View(new ConfigurationViewModel(_config));

        [HttpPost]
        public IActionResult SaveServerSettings(ServerSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                _config.Kestrel.SensorPort = settings.SensorsPort;
                _config.Kestrel.SitePort = settings.SitePort;

                _config.ServerCertificate.Name = settings.CertificateName;
                _config.ServerCertificate.Key = settings.CertificateKey;

                _config.ResaveSettings();
            }

            return PartialView("_Server", settings);
        }

        [HttpPost]
        public IActionResult SaveBackupSettings(BackupSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                _config.BackupDatabase.IsEnabled = settings.IsEnabled;
                _config.BackupDatabase.PeriodHours = settings.BackupPeriodHours;
                _config.BackupDatabase.StoragePeriodDays = settings.BackupStoragePeriodDays;

                _config.ResaveSettings();
            }

            return PartialView("_Backup", settings);
        }

        [HttpPost]
        public IActionResult SaveMonitoringSettings(MonitoringSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                _config.MonitoringOptions.IsMonitoringEnabled = settings.IsMonitoringEnabled;
                _config.MonitoringOptions.TopHeaviestSensorsCount = settings.TopHeaviestSensorsCount;
                _config.MonitoringOptions.DatabaseStatisticsPeriodDays = settings.DatabaseStatisticsPeriodDays;

                _config.ResaveSettings();
            }

            return PartialView("_SelfMonitoring", settings);
        }

        [HttpPost]
        public IActionResult SaveTelegramSettings(TelegramSettingsViewModel settings)
        {
            if (ModelState.IsValid)
            {
                _config.Telegram.IsRunning = settings.IsEnabled;
                _config.Telegram.BotToken = settings.BotToken;
                _config.Telegram.BotName = settings.BotName;

                _config.ResaveSettings();
            }

            return PartialView("_Telegram", settings);
        }

        [HttpGet]
        public Task<string> RestartTelegramBot() => _telegramBot.StartBot();
    }
}