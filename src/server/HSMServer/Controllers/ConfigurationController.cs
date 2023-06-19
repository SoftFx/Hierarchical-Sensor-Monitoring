using System;
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
        private static Dictionary<string, ConfigurationViewModel> configViewModel = new Dictionary<string, ConfigurationViewModel>();

        public ConfigurationController(IServerConfig config, NotificationsCenter notifications)
        {
            _config = config;

            configViewModel = (ConfigurationViewModel.TelegramSettings(new TelegramConfigurationViewModel(_config.Telegram)));
            
            _telegramBot = notifications.TelegramBot;
        }


        public IActionResult Index()
        {
            //var paramNames = _config.GetAllParameterNames();
            //foreach (var paramName in paramNames)
            //{
            //    var valueFromDB = _config.ReadConfigurationObject(paramName);
            //    if (valueFromDB != null)
            //    {
            //        viewModels.Add(new ConfigurationObjectViewModel(valueFromDB, false));
            //        continue;
            //    }

            //    var value = _config.ReadOrDefault(paramName);
            //    viewModels.Add(new ConfigurationObjectViewModel(value, true));
            //}
            //viewModels.Sort((vm1, vm2) => vm1.Name.CompareTo(vm2.Name));
            return View(configViewModel);
        }

        [HttpPost]
        public void SaveConfig(string propertyName, string newValue)
        {
            
        }
        
        [HttpPost]
        public void SaveConfigObject([FromBody] ConfigurationViewModel viewModel)
        {
            //ConfigurationObject model = GetModelFromViewModel(viewModel);
            //_config.AddConfigurationObject(model.Name, model.Value);
            var item = configViewModel[viewModel.PropertyName];
            
            if (item.PropertyName == nameof(_config.Telegram.BotName))
                _config.Telegram.BotName = viewModel.Value;
            
            if (item.PropertyName == nameof(_config.Telegram.BotToken))
                _config.Telegram.BotToken = viewModel.Value;

            if (item.PropertyName == nameof(_config.Telegram.IsRunning) && bool.TryParse(viewModel.Value, out var boolValue))
                _config.Telegram.IsRunning = boolValue;
            
            _config.ResaveSettings();
        }

        public void SetToDefault([FromQuery(Name = "Name")] string configObjName)
        {
            var item = configViewModel[configObjName];
            var defaultValue = item.DefaultValue;

            if (item.PropertyName == nameof(_config.Telegram.BotName))
                _config.Telegram.BotName = defaultValue.ToString();
            
            if (item.PropertyName == nameof(_config.Telegram.BotToken))
                _config.Telegram.BotToken = defaultValue.ToString();

            if (item.PropertyName == nameof(_config.Telegram.IsRunning))
                _config.Telegram.IsRunning = (bool) defaultValue;
            
            _config.ResaveSettings();
            //_config.SetConfigurationObjectToDefault(configObjName);
        }

        [HttpGet]
        public Task<string> RestartTelegramBot() => _telegramBot.StartBot();


        //private ConfigurationObject GetModelFromViewModel(ConfigurationObjectViewModel viewModel)
        //{
        //    ConfigurationObject result = new ConfigurationObject();
        //    result.Value = viewModel.Value;
        //    result.Name = viewModel.Name;
        //    return result;
        //}
    }
}
