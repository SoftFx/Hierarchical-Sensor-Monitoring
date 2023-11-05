using System;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.DTOs.Sensor;
using HSMServer.Extensions;
using HSMServer.Model.Dashboards;
using HSMServer.Model.TreeViewModel;
using HSMServer.Dashboards;
using HSMServer.Model.Dashboards;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    public class DashboardsController : BaseController
    {
        private readonly ITreeValuesCache _cache;
        private readonly IDashboardManager _dashboardManager;
        private readonly TreeViewModel _treeViewModel;

        public DashboardsController(IDashboardManager dashboardManager, IUserManager userManager, ITreeValuesCache cache, TreeViewModel treeViewModel) : base(userManager)
        {
            _dashboardManager = dashboardManager;
            _cache = cache;
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index() => View(_dashboardManager.GetValues().Select(d => new DashboardViewModel(d)).OrderBy(d => d.Name).ToList());

        [HttpPost]
        public async Task<IActionResult> AddDashboard(DashboardViewModel newDashboard)
        {
            if (!ModelState.IsValid)
                return View(nameof(EditDashboard), newDashboard);

            await _dashboardManager.TryAdd(newDashboard.ToDashboardAdd(CurrentUser), out var dashboard);

            return RedirectToAction(nameof(EditDashboard), new { dashboardId = dashboard.Id });
        }
        
        public IActionResult AddDashboardPanel([FromQuery] Guid dashBoardId)
        {
            _dashboardManager.TryGetValue(dashBoardId, out var dashboard);
            return View("AddDashboardPanel");
        }

        [HttpPost]
        public IActionResult SaveDashboardPanel(PanelViewModel panelViewModel)
        {
            
        }

        [HttpGet]
        public IActionResult EditDashboard(Guid? dashboardId) =>
            dashboardId.HasValue && _dashboardManager.TryGetValue(dashboardId.Value, out var dashboard)
                ? View(nameof(EditDashboard), new DashboardViewModel(dashboard))
                : View(nameof(EditDashboard));

        [HttpPost]
        public async Task<IActionResult> EditDashboard(DashboardViewModel editDashboard)
        {
            await _dashboardManager.TryUpdate(editDashboard.ToDashboardUpdate());

            return View(nameof(EditDashboard), new DashboardViewModel(_dashboardManager[editDashboard.Id]));
        }

        [HttpGet]
        public async Task RemoveDashboard(Guid dashboardId) =>
            await _dashboardManager.TryRemove(new(dashboardId, CurrentInitiator));

        [HttpGet]
        public IActionResult GetPanel() => PartialView("_Panel");
        
        [HttpGet]
        public async Task<JsonResult> GetSource(Guid sourceId, Guid panelId)
        {
            if (!CurrentUser.ConfiguredPanels.TryGetValue(panelId, out var panel))
                panel = new PanelViewModel();

            var errorMessage = string.Empty;
            if (_treeViewModel.Sensors.TryGetValue(sourceId, out var sensorNodeViewModel) && panel.TryAddSource(sensorNodeViewModel, out errorMessage))
            {
                var values = (await _cache.GetSensorValuesPage(sensorNodeViewModel.Id, DateTime.UtcNow.AddDays(-30),
                    DateTime.UtcNow, 500, RequestOptions.IncludeTtl).Flatten()).Select(x => (object)x);

                return Json(new SourceDto(sensorNodeViewModel, values.ToList(), panel.Id));
            }

            return Json(new
            {
                errorMessage
            });
        }

        [HttpPost]
        public void CreatePanel(Guid[] sourceIds, Guid panelId)
        {
            if (CurrentUser.ConfiguredPanels.TryGetValue(panelId, out var panel))
                foreach (var id in sourceIds)
                {
                    if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
                        panel.UpdateSources(sensor);
                }
        }
    }
}
