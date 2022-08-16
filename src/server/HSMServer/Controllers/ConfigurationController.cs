﻿using HSMServer.Attributes;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using HSMServer.Core.Notifications;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin(true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ConfigurationController : Controller
    {
        private readonly IConfigurationProvider _configurationProvider;
        private readonly INotificationsCenter _notificationsCenter;


        public ConfigurationController(IConfigurationProvider configurationProvider, INotificationsCenter notificationsCenter)
        {
            _configurationProvider = configurationProvider;
            _notificationsCenter = notificationsCenter;
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

                var value = _configurationProvider.ReadOrDefaultConfigurationObject(paramName);
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

        public void RestartTelegramBot()
        {
            _notificationsCenter.StartBot();
        }


        private ConfigurationObject GetModelFromViewModel(ConfigurationObjectViewModel viewModel)
        {
            ConfigurationObject result = new ConfigurationObject();
            result.Value = viewModel.Value;
            result.Name = viewModel.Name;
            return result;
        }
    }
}
