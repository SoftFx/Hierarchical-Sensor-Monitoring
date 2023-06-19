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
        private readonly IServerConfig _config;
        private readonly TelegramBot _telegramBot;
        private static Dictionary<string, ConfigurationViewModel> _configViewModel = new ();

        public ConfigurationController(IServerConfig config, NotificationsCenter notifications)
        {
            _config = config;
            _configViewModel = (ConfigurationViewModel.TelegramSettings(new TelegramConfigurationViewModel(_config.Telegram)));
            
            _telegramBot = notifications.TelegramBot;
        }


        public IActionResult Index()
        {
            return View(_configViewModel);
        }

        [HttpPost]
        public void SaveConfig([FromBody] ConfigurationViewModel viewModel)
        {
            var item = _configViewModel[viewModel.PropertyName];
            
            if (item.PropertyName == nameof(_config.Telegram.BotName))
                _config.Telegram.BotName = viewModel.Value;
            
            if (item.PropertyName == nameof(_config.Telegram.BotToken))
                _config.Telegram.BotToken = viewModel.Value;

            if (item.PropertyName == nameof(_config.Telegram.IsRunning) && bool.TryParse(viewModel.Value, out var boolValue))
                _config.Telegram.IsRunning = boolValue;
            
            _config.ResaveSettings();
        }

        public void SetToDefault([FromQuery] string name)
        {
            var item = _configViewModel[name];
            var defaultValue = item.DefaultValue;

            if (item.PropertyName == nameof(_config.Telegram.BotName))
                _config.Telegram.BotName = defaultValue.ToString();
            
            if (item.PropertyName == nameof(_config.Telegram.BotToken))
                _config.Telegram.BotToken = defaultValue.ToString();

            if (item.PropertyName == nameof(_config.Telegram.IsRunning))
                _config.Telegram.IsRunning = (bool) defaultValue;
            
            _config.ResaveSettings();
        }

        [HttpGet]
        public Task<string> RestartTelegramBot() => _telegramBot.StartBot();
    }
}
