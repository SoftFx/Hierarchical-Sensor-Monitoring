using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.DTOs.Sensor;
using HSMServer.Model.Dashboards;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Drawing;
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

        [HttpGet("Dashboards")]
        public IActionResult Index() => View(_dashboardManager.GetValues().Select(d => new DashboardViewModel(d)).OrderBy(d => d.Name).ToList());

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult AddDashboardPanel(Guid dashBoardId, Guid panelId)
        {
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

            return RedirectToAction(nameof(EditDashboard), new { dashboardId = dashboard.Id });
        }

        [HttpGet("Dashboards/{dashboardId:guid}/SourceUpdate/{panelId:guid}/{sourceId:guid}")]
        public async Task<IActionResult> Source(Guid dashboardId, Guid panelId, Guid sourceId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard) &&
                dashboard.Panels.TryGetValue(panelId, out var panel) &&
                panel.Sources.TryGetValue(sourceId, out var source))
            {
                var updates = source.Source.GetSourceUpdates();

                return Json(updates);
            }

            return _emptyResult;
        }

        [HttpGet("Dashboards/{dashboardId:guid}/{panelId:guid}/{sourceId:guid}")]
        public async Task<IActionResult> GetSource(Guid sourceId, Guid dashboardId, Guid panelId)
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
                            if (source.SensorId == sourceId)
                                return BadRequest("Source already exists");

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
                        var sensorModel = _cache.GetSensor(sourceId);
                        var datasource = new PanelDatasource(sensorModel, dashboard);

                        if (panel.Sources.TryAdd(datasource.Id, datasource) && (await _dashboardManager.TryUpdate(dashboard)))
                        {
                            var (from, to) = datasource.GetFromTo();
                            var response = await datasource.Source.Initialize(from, to);

                            return Json(new SourceDto(response, datasource, newSource));
                        }
                    }
                }
            }

            return Json(new
            {
                errorMessage
            });
        }

        [HttpPut("Dashboards/{dashboardId:guid}/{panelId:guid}/{sourceId:guid}")]
        public async Task<IActionResult> UpdateSource([FromBody] UpdateSourceDto update, Guid dashboardId, Guid panelId, Guid sourceId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard) &&
                dashboard.Panels.TryGetValue(panelId, out var panel) &&
                panel.Sources.TryGetValue(sourceId, out var source))
            {
                source.Color = Color.FromName(update.Color);
                source.Label = update.Name;

                await _dashboardManager.TryUpdate(dashboard);

                return Ok();
            }

            return NotFound("No such source");
        }

        [HttpDelete("Dashboards/{dashboardId:guid}/{panelId:guid}/{sourceId:guid}")]
        public IActionResult DeleteSource(Guid dashboardId, Guid panelId, Guid sourceId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard) &&
                dashboard.Panels.TryGetValue(panelId, out var panel) &&
                panel.Sources.TryRemove(sourceId, out _))
                return Ok();

            return NotFound("No source found to delete");
        }

        [HttpGet("Dashboards/{dashboardId:guid}")]
        public IActionResult EditDashboard(Guid dashboardId, bool isModify = true)
        {
            _dashboardManager.TryGetValue(dashboardId, out var dashboard);

            return View(nameof(EditDashboard), new DashboardViewModel(dashboard, isModify));
        }

        [HttpGet]
        public async Task<IActionResult> CreateDashboard()
        {
            await _dashboardManager.TryAdd(DashboardViewModel.ToDashboardAdd(CurrentUser), out var dashboard);

            return RedirectToAction(nameof(EditDashboard), new { dashboardId = dashboard.Id, isModify = true });
        }

        [HttpPost("Dashboards/{dashboardId:guid?}")]
        public async Task<IActionResult> EditDashboard([FromBody] EditDashBoardViewModel editDashboard, Guid dashboardId)
        {
            // if (!ModelState.IsValid)
            //     return View(nameof(EditDashboard), editDashboard);

            if (editDashboard is null)
                return BadRequest();

            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard))
            {
                foreach (var (id, cords) in editDashboard.Panels)
                {
                    if (dashboard.Panels.TryGetValue(id, out var panel))
                        panel.Cords = cords;
                }
            }

            await _dashboardManager.TryUpdate(dashboard);
            return Ok(dashboard);
        }

        [HttpDelete("Dashboards/{dashboardId:guid}")]
        public async Task RemoveDashboard(Guid dashboardId) => await _dashboardManager.TryRemove(new(dashboardId, CurrentInitiator));
        
        [HttpDelete("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public async Task<IActionResult> RemovePanel(Guid dashboardId, Guid panelId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard) &&
                dashboard.Panels.TryRemove(panelId, out _) &&
                await _dashboardManager.TryUpdate(dashboard))
                return Ok();

            return NotFound();
        }

        [HttpGet]
        public IActionResult GetPanel(Guid dashboardId)
        {
            _dashboardManager.TryGetValue(dashboardId, out var dashboard);
            var newPanel = new Panel(dashboard);
            dashboard.Panels.TryAdd(newPanel.Id, newPanel);
            _dashboardManager.TryUpdate(dashboard);

            return PartialView("_Panel", new PanelViewModel(newPanel, dashboard.Id));
        }
    }
}