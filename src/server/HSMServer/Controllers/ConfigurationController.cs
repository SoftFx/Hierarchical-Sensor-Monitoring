using HSMServer.Attributes;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ConfigurationController : Controller
    {
        private static Dictionary<string, ConfigurationViewModel> _configViewModel = new();

        private readonly IServerConfig _config;
        private readonly TelegramBot _telegramBot;


        public ConfigurationController(IServerConfig config, NotificationsCenter notifications)
        {
            _config = config;
            _configViewModel = ConfigurationViewModel.TelegramSettings(new TelegramConfigurationViewModel(_config.Telegram));

            _telegramBot = notifications.TelegramBot;
        }


        public IActionResult Index()
        {
            return View(_configViewModel);
        }

        [HttpPost]
        public void SaveConfig([FromBody] ConfigurationViewModel viewModel) => ChangeConfigValue(viewModel.PropertyName, viewModel.Value);

        [HttpPost]
        public void SetToDefault([FromQuery] string name) => ChangeConfigValue(name, _configViewModel[name].DefaultValue);

        [HttpGet]
        public Task<string> RestartTelegramBot() => _telegramBot.StartBot();


        private void ChangeConfigValue(string propertyName, string newValue)
        {
            switch (propertyName)
            {
                case nameof(_config.Telegram.BotName):
                    _config.Telegram.BotName = newValue;
                    break;
                case nameof(_config.Telegram.BotToken):
                    _config.Telegram.BotToken = newValue;
                    break;
                case nameof(_config.Telegram.IsRunning) when bool.TryParse(newValue, out var boolValue):
                    _config.Telegram.IsRunning = boolValue;
                    break;
            }

            _config.ResaveSettings();
        }
    }
}