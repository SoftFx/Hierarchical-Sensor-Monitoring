using HSMServer.Authentication;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.DTOs.Sensor;
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
        private readonly IDashboardManager _dashboardManager;
        private readonly TreeViewModel _treeViewModel;


        public DashboardsController(IDashboardManager dashboardManager, IUserManager userManager, TreeViewModel treeViewModel) : base(userManager)
        {
            _dashboardManager = dashboardManager;
            _treeViewModel = treeViewModel;
        }


        [HttpGet("Dashboards")]
        public IActionResult Index() => View(_dashboardManager.GetValues().Select(d => new DashboardViewModel(d)).OrderBy(d => d.Name).ToList());

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public async Task<IActionResult> AddDashboardPanel(Guid dashBoardId, Guid panelId)
        {
            _dashboardManager.TryGetValue(dashBoardId, out var dashboard);
            dashboard.Panels.TryGetValue(panelId, out var panel);
            var vm = new PanelViewModel(panel, dashboard.Id);

            return View("AddDashboardPanel", await vm.InitPanelData());
        }

        [HttpPost("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult SaveDashboardPanel(Guid dashBoardId, Guid panelId, [FromBody] PanelViewModel model)
        {
            if (string.IsNullOrEmpty(model.Name) || string.IsNullOrWhiteSpace(model.Name))
                return BadRequest("Invalid Name");

            if (model.Name.Length > 30)
                return BadRequest("Name length is grater than 30 characters");

            if (model.Description.Length > 250)
                return BadRequest("Description length is greater than 100 characters");

            _dashboardManager.TryGetValue(dashBoardId, out var dashboard);

            if (dashboard.Panels.TryGetValue(panelId, out var panel))
                panel.Update(new PanelUpdate(panel.Id)
                {
                    Name = model.Name,
                    Description = model.Description
                });

            return Ok(dashboard.Id);
        }

        [HttpGet("Dashboards/{dashboardId:guid}/SourceUpdate/{panelId:guid}/{sourceId:guid}")]
        public IActionResult Source(Guid dashboardId, Guid panelId, Guid sourceId)
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
                        MainSensorType = currentType,
                        MainUnit = currentUnitType,
                    };

                    if (_treeViewModel.Sensors.TryGetValue(sourceId, out var newSource) && viewModel.TryAddSource(newSource, out errorMessage))
                    {
                        try
                        {
                            if (panel.TryAddSource(sourceId, out var datasource))
                            {
                                var response = await datasource.Source.Initialize();

                                return Json(new SourceDto(response, datasource, newSource));
                            }
                        }
                        catch (Exception exception)
                        {
                            errorMessage = exception.Message;
                        }
                    }
                }
            }

            return Json(new
            {
                errorMessage
            });
        }

        [HttpPut("Dashboards/{dashboardId:guid}/Relayout")]
        public async Task<IActionResult> Relayout(Guid dashboardId, [FromQuery] int width)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard))
            {
                Panel.Relayout(dashboard.Panels, width);

                if (await _dashboardManager.TryUpdate(dashboard))
                    return Ok("Successfully relayout");
            }

            return BadRequest("Couldn't relayout");
        }

        [HttpPut("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult UpdateLegendDisplay([FromQuery] bool showlegend, Guid dashboardId, Guid panelId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard) && dashboard.Panels.TryGetValue(panelId, out var panel))
            {
                panel.Update(new PanelUpdate(panel.Id)
                {
                    ShowLegend = showlegend,
                });

                return Ok("Successfully updated");
            }

            return BadRequest("Couldn't update panel");
        }

        [HttpPut("Dashboards/{dashboardId:guid}/{panelId:guid}/{sourceId:guid}")]
        public async Task<IActionResult> UpdateSource([FromBody] PanelSourceUpdate update, Guid dashboardId, Guid panelId, Guid sourceId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard) &&
                dashboard.Panels.TryGetValue(panelId, out var panel) &&
                panel.Sources.TryGetValue(sourceId, out var source))
            {
                source.Update(update);

                await _dashboardManager.TryUpdate(dashboard); //can remove?

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
        public async Task<IActionResult> EditDashboard(Guid dashboardId, bool isModify = false)
        {
            _dashboardManager.TryGetValue(dashboardId, out var dashboard);

            var vm = new DashboardViewModel(dashboard, isModify);

            return View(nameof(EditDashboard), await vm.InitDashboardData());
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
            if (editDashboard is null)
                return BadRequest();

            if (string.IsNullOrEmpty(editDashboard.Name) || string.IsNullOrWhiteSpace(editDashboard.Name))
                return BadRequest("Invalid name");

            if (editDashboard.Name.Length > 30)
                return BadRequest("Name length is grater than 30 characters");

            if (editDashboard.Description.Length > 250)
                return BadRequest("Description length is greater than 100 characters");

            var isReload = false;

            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard))
            {
                isReload = dashboard.DataPeriod != editDashboard.FromPeriod;
                dashboard.Update(editDashboard.ToUpdate());
                foreach (var (id, settings) in editDashboard.Panels)
                {
                    if (dashboard.Panels.TryGetValue(id, out var panel))
                        panel.Update(settings.ToUpdate(panel.Id));
                }
            }

            await _dashboardManager.TryUpdate(dashboard);

            return Ok(new
            {
                reload = isReload,
            });
        }

        [HttpDelete("Dashboards/{dashboardId:guid}")]
        public Task RemoveDashboard(Guid dashboardId) => _dashboardManager.TryRemove(new(dashboardId, CurrentInitiator));

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
        public async Task<IActionResult> GetPanel(Guid dashboardId)
        {
            if (_dashboardManager.TryGetValue(dashboardId, out var dashboard))
            {
                var newPanel = new Panel(dashboard);

                dashboard.TryAddPanel(newPanel);

                var vm = new PanelViewModel(newPanel, dashboard.Id);

                return PartialView("_Panel", await vm.InitPanelData());
            }

            return await EditDashboard(dashboardId);
        }
    }
}