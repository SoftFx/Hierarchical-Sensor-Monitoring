using HSMServer.Attributes;
using HSMServer.Configuration;
using HSMServer.Core.Configuration;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin(true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ConfigurationController : Controller
    {
        private readonly IConfigurationProvider _configurationProvider;
        private readonly TelegramBot _telegramBot;


        public ConfigurationController(IConfigurationProvider configurationProvider, NotificationsCenter notifications)
        {
            _configurationProvider = configurationProvider;
            _telegramBot = notifications.TelegramBot;
        }


        public IActionResult Index()
        {
            List<ConfigurationObjectViewModel> viewModels = new List<ConfigurationObjectViewModel>();
            var paramNames = _configurationProvider.GetAllParameterNames();
            foreach (var paramName in paramNames)
            {
                var valueFromDB = _configurationProvider.ReadConfigurationObject(paramName);
                if (valueFromDB != null)
                {
                    viewModels.Add(new ConfigurationObjectViewModel(valueFromDB, false));
                    continue;
                }

                var value = _configurationProvider.ReadOrDefault(paramName);
                viewModels.Add(new ConfigurationObjectViewModel(value, true));
            }
            viewModels.Sort((vm1, vm2) => vm1.Name.CompareTo(vm2.Name));

            return View(viewModels);
        }

        [HttpPost]
        public void SaveConfigObject([FromBody] ConfigurationObjectViewModel viewModel)
        {
            ConfigurationObject model = GetModelFromViewModel(viewModel);
            _configurationProvider.AddConfigurationObject(model.Name, model.Value);
        }

        public void SetToDefault([FromQuery(Name = "Name")] string configObjName)
        {
            _configurationProvider.SetConfigurationObjectToDefault(configObjName);
        }

        [HttpGet]
        public Task<string> RestartTelegramBot() => _telegramBot.StartBot();


        private ConfigurationObject GetModelFromViewModel(ConfigurationObjectViewModel viewModel)
        {
            ConfigurationObject result = new ConfigurationObject();
            result.Value = viewModel.Value;
            result.Name = viewModel.Name;
            return result;
        }
    }
}
