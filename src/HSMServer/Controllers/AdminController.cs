using HSMServer.Attributes;
using HSMServer.Configuration;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace HSMServer.Controllers
{
    [Authorize]
    [AuthorizeIsAdmin(true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class AdminController : Controller
    {
        private readonly IConfigurationProvider _configurationProvider;
        public AdminController(IConfigurationProvider configurationProvider)
        {
            _configurationProvider = configurationProvider;
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

        private ConfigurationObject GetModelFromViewModel(ConfigurationObjectViewModel viewModel)
        {
            ConfigurationObject result = new ConfigurationObject();
            result.Value = viewModel.Value;
            result.Name = viewModel.Name;
            return result;
        }
    }
}
