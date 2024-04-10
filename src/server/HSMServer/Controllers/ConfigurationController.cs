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

        //[HttpPost]
        //public void SaveConfig([FromBody] ConfigurationViewModel viewModel) => ChangeConfigValue(viewModel.PropertyName, viewModel.Value);

        //[HttpPost]
        //public void SetToDefault([FromQuery] string name) => ChangeConfigValue(name, _configViewModel[name].DefaultValue);

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