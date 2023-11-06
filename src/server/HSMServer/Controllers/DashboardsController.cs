using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.DTOs.Sensor;
using HSMServer.Extensions;
using HSMServer.Model.Dashboards;
using HSMServer.Model.TreeViewModel;
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


        [Route("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult AddDashboardPanel(Guid dashBoardId, Guid panelId)
        {
            Console.WriteLine(dashBoardId);
            Console.WriteLine(panelId);
            _dashboardManager.TryGetValue(dashBoardId, out var dashboard);
            dashboard.Panels.TryGetValue(panelId, out var panel);
            return View("AddDashboardPanel", new PanelViewModel(panel, dashboard.Id));
        }

        [HttpPost("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult SaveDashboardPanel(Guid dashBoardId, Guid panelId, [FromForm] PanelViewModel model)
        {
            _dashboardManager.TryGetValue(dashBoardId, out var dashboard);
            dashboard.Panels.TryGetValue(panelId, out var panel);
            panel?.Update(new PanelUpdate() { Id = panel.Id, Name = model.Name, Description = model.Description });
            _dashboardManager.TryUpdate(dashboard);

            return View("AddDashboardPanel", new PanelViewModel()
            {
                DashboardId = dashBoardId,
                Id = panelId
            });
        }

        [HttpGet("Dashboards/{dashboardId:guid}/{panelId:guid}/{sourceId:guid}")]
        public async Task<JsonResult> GetSource2(Guid sourceId, Guid dashboardId, Guid panelId)
        {
            string errorMessage = string.Empty;
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard))
            {
                if (dashboard.Panels.TryGetValue(panelId, out var panel))
                {
                    SensorType? currentType = null;
                    Unit? currentUnitType = null;

                    if (panel?.Sources != null)
                        foreach (var (id, source) in panel.Sources)
                        {
                            _treeViewModel.Sensors.TryGetValue(source.SensorId, out var sensorNodeViewModel);
                            currentType = sensorNodeViewModel?.Type;
                            currentUnitType = sensorNodeViewModel?.SelectedUnit ?? currentUnitType;
                        }

                    var viewModel = new PanelViewModel()
                    {
                        SensorType = currentType,
                        UnitType = currentUnitType,
                    };

                    if (_treeViewModel.Sensors.TryGetValue(sourceId, out var newSource) && viewModel.TryAddSource(newSource, out errorMessage))
                    {
                        panel.Sources.TryAdd(newSource.Id, new PanelDataSource(newSource.Id));
                        _dashboardManager.TryUpdate(dashboard);
                        var values = (await _cache.GetSensorValuesPage(newSource.Id, DateTime.UtcNow.AddDays(-30),
                            DateTime.UtcNow, 500, RequestOptions.IncludeTtl).Flatten()).Select(x => (object)x);

                        return Json(new SourceDto(newSource, values.ToList(), panel.Id));
                    }
                }
            }

            return Json(new
            {
                errorMessage
            });
        }

        [HttpGet]
        public async Task<IActionResult> EditDashboard(Guid? dashboardId)
        {
            Dashboard dashboard = null;

            var isModify = dashboardId.HasValue && _dashboardManager.TryGetValue(dashboardId.Value, out dashboard);
            if (!isModify)
                await _dashboardManager.TryAdd(DashboardViewModel.ToDashboardAdd(CurrentUser), out dashboard);

            return View(nameof(EditDashboard), new DashboardViewModel(dashboard, isModify));
        }

        [HttpPost]
        public async Task<IActionResult> EditDashboard(DashboardViewModel editDashboard)
        {
            if (!ModelState.IsValid)
                return View(nameof(EditDashboard), editDashboard);

            await _dashboardManager.TryUpdate(editDashboard.ToDashboardUpdate());

            return View(nameof(EditDashboard), new DashboardViewModel(_dashboardManager[editDashboard.Id]));
        }

        [HttpGet]
        public async Task RemoveDashboard(Guid dashboardId) =>
            await _dashboardManager.TryRemove(new(dashboardId, CurrentInitiator));

        [HttpGet]
        public IActionResult GetPanel(Guid dashboardId)
        {
            Console.WriteLine(dashboardId);
            _dashboardManager.TryGetValue(dashboardId, out var dashboard);
            var newPanel = new Panel();
            dashboard.Panels.TryAdd(newPanel.Id, newPanel);
            _dashboardManager.TryUpdate(dashboard);

            return PartialView("_Panel", new PanelViewModel() { Id = newPanel.Id, DashboardId = dashboard.Id });
        }

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
