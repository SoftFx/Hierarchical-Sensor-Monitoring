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


        public ConfigurationController(IServerConfig config, NotificationsCenter notifications)
        {
            _config = config;

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
            var model = new ConfigurationViewModel();
            model.Telegram = new TelegramConfigurationViewModel(_config.Telegram);
            return View(model);
        }

        [HttpPost]
        public void SaveConfig(string propertyName, string newValue)
        {
            
        }
        
        [HttpPost]
        public void SaveConfigObject([FromBody] ConfigurationObjectViewModel viewModel)
        {
            //ConfigurationObject model = GetModelFromViewModel(viewModel);
            //_config.AddConfigurationObject(model.Name, model.Value);
        }

        public void SetToDefault([FromQuery(Name = "Name")] string configObjName)
        {
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
