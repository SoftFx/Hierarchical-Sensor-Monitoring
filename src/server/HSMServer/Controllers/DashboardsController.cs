using System;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Views.Dashboards.Source;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    public class DashboardsController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;
        private readonly ITreeValuesCache _cache;


        public DashboardsController(TreeViewModel treeViewModel, IUserManager userManager, ITreeValuesCache cache) : base(userManager)
        {
            _treeViewModel = treeViewModel;
            _cache = cache;
        }


        public IActionResult Index() => View(_treeViewModel);

        public IActionResult AddDashboard() => View("EditDashboard", _treeViewModel);
        
        public IActionResult AddDashboardPanel() => View("AddDashboardPanel", _treeViewModel);

        [HttpGet]
        public async Task<SourceDto> GetSource(Guid id)
        {
            if (_treeViewModel.Sensors.TryGetValue(id, out var sensorNodeViewModel))
            {
                var values = (await _cache.GetSensorValuesPage(sensorNodeViewModel.Id, DateTime.UtcNow.AddDays(-30),
                    DateTime.UtcNow, 500, RequestOptions.IncludeTtl).Flatten()).Select(x => (object)x);

                var model = new SourceDto(sensorNodeViewModel, values.ToList());
                
                return model;
            }
            
            return new SourceDto();
        }
    }
}
