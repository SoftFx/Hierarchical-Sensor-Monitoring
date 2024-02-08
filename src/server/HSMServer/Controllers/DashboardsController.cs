using HSMServer.Authentication;
using HSMServer.Dashboards;
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
        private readonly IDashboardManager _dashboards;


        public DashboardsController(IDashboardManager dashboardManager, IUserManager userManager, TreeViewModel _) : base(userManager)
        {
            _dashboards = dashboardManager;
        }


        [HttpGet("Dashboards")]
        public IActionResult Index() => View(_dashboards.GetValues().Select(d => new DashboardViewModel(d).AttachUser(_userManager)).ToList());


        #region Dashboards

        [HttpGet]
        public async Task<IActionResult> CreateDashboard()
        {
            await _dashboards.TryAdd(DashboardViewModel.ToDashboardAdd(CurrentUser), out var dashboard);

            return RedirectToAction(nameof(EditDashboard), new { dashboardId = dashboard.Id, isModify = true });
        }

        [HttpGet("Dashboards/{dashboardId:guid}")]
        public async Task<IActionResult> EditDashboard(Guid dashboardId, bool isModify = false)
        {
            if (TryGetBoard(dashboardId, out var dashboard))
            {
                var vm = new DashboardViewModel(dashboard, isModify);

                return View(nameof(EditDashboard), await vm.InitDashboardData());
            }

            return Redirect("Dashboards");
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

            if (TryGetBoard(dashboardId, out var dashboard))
            {
                isReload = dashboard.DataPeriod != editDashboard.FromPeriod;

                dashboard.Update(editDashboard.ToUpdate());

                foreach (var (id, settings) in editDashboard.Panels)
                {
                    if (dashboard.Panels.TryGetValue(id, out var panel))
                        panel.Update(settings.ToUpdate(panel.Id));
                }
            }

            await _dashboards.TryUpdate(dashboard);

            return Ok(new
            {
                reload = isReload,
            });
        }

        [HttpDelete("Dashboards/{dashboardId:guid}")]
        public Task RemoveDashboard(Guid dashboardId) => _dashboards.TryRemove(new(dashboardId, CurrentInitiator));

        #endregion

        #region Panels

        [HttpGet]
        public async Task<IActionResult> GetPanel(Guid dashboardId)
        {
            if (TryGetBoard(dashboardId, out var dashboard))
            {
                var newPanel = new Panel(dashboard);

                if (dashboard.TryAddPanel(newPanel))
                {
                    var vm = new PanelViewModel(newPanel, dashboard.Id);

                    return PartialView("_Panel", await vm.InitPanelData());
                }
            }

            return await EditDashboard(dashboardId);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public async Task<IActionResult> AddDashboardPanel(Guid dashboardId, Guid panelId)
        {
            if (TryGetPanel(dashboardId, panelId, out var panel))
            {
                var vm = new PanelViewModel(panel, dashboardId);

                return View("AddDashboardPanel", await vm.InitPanelData());
            }

            return Redirect("Dashboards");
        }

        [HttpDelete("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult RemovePanel(Guid dashboardId, Guid panelId)
        {
            return TryGetBoard(dashboardId, out var dashboard) && dashboard.TryRemovePanel(panelId) ? Ok() : NotFound();
        }

        [HttpPost("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult SaveDashboardPanel(Guid dashboardId, Guid panelId, PanelViewModel model)
        {
            if (string.IsNullOrEmpty(model.Name) || string.IsNullOrWhiteSpace(model.Name))
                return BadRequest("Invalid Name");

            if (model.Name.Length > 30)
                return BadRequest("Name length is grater than 30 characters");

            if (model.Description?.Length > 250)
                return BadRequest("Description length is greater than 100 characters");

            if (TryGetPanel(dashboardId, panelId, out var panel))
                panel.NotifyUpdate(new PanelUpdate(panel.Id)
                {
                    Name = model.Name,
                    Description = model.Description ?? string.Empty,
                    ShowProduct = model.ShowProduct,
                    IsAggregateValues = model.AggregateValues,
                });

            return Ok(dashboardId);
        }

        [HttpPut("Dashboards/{dashboardId:guid}/Relayout")]
        public IActionResult Relayout(Guid dashboardId, [FromQuery] int width)
        {
            return TryGetBoard(dashboardId, out var dashboard) && dashboard.AutofitPanels(width) ? Ok("Successfully relayout") : BadRequest("Couldn't relayout");
        }

        [HttpPut("Dashboards/{dashboardId:guid}/{panelId:guid}")]
        public IActionResult UpdateLegendDisplay([FromQuery] bool showlegend, Guid dashboardId, Guid panelId)
        {
            if (TryGetPanel(dashboardId, panelId, out var panel))
            {
                panel.NotifyUpdate(new PanelUpdate(panel.Id)
                {
                    ShowLegend = showlegend,
                });

                return Ok("Successfully updated");
            }

            return BadRequest("Couldn't update panel");
        }

        #endregion

        #region Sources

        [HttpGet("Dashboards/{dashboardId:guid}/SourceUpdate/{panelId:guid}/{sourceId:guid}")]
        public IActionResult Source(Guid dashboardId, Guid panelId, Guid sourceId)
        {
            return TryGetSource(dashboardId, panelId, sourceId, out var source) ? Json(source.Source.GetSourceUpdates()) : _emptyResult;
        }

        [HttpGet("Dashboards/{dashboardId:guid}/{panelId:guid}/{sensorId:guid}")]
        public async Task<IActionResult> GetSource(Guid sensorId, Guid dashboardId, Guid panelId, bool showProduct)
        {
            var error = string.Empty;

            if (TryGetPanel(dashboardId, panelId, out var panel) && panel.TryAddSource(sensorId, out var datasource, out error))
            {
                var response = await datasource.Source.Initialize();

                return Json(new DatasourceViewModel(response, datasource, showProduct));
            }

            return Json(new
            {
                error
            });
        }

        [HttpPut("Dashboards/{dashboardId:guid}/{panelId:guid}/{sourceId:guid}")]
        public async Task<IActionResult> UpdateSource([FromBody] PanelSourceUpdate update, Guid dashboardId, Guid panelId, Guid sourceId)
        {
            if (TryGetPanel(dashboardId, panelId, out var panel) && panel.Sources.TryGetValue(sourceId, out var source))
            {
                var oldProperty = source.Property;

                source.NotifyUpdate(update with { AggregateValues = panel.AggregateValues });

                if (source.Property != oldProperty)
                {
                    var response = await source.Source.Initialize();
                    return Json(new DatasourceViewModel(response, source, panel.ShowProduct));
                }

                return Ok();
            }

            return NotFound("No such source");
        }

        [HttpDelete("Dashboards/{dashboardId:guid}/{panelId:guid}/{sensorId:guid}")]
        public IActionResult DeleteSource(Guid dashboardId, Guid panelId, Guid sensorId)
        {
            return TryGetPanel(dashboardId, panelId, out var panel) && panel.TryRemoveSource(sensorId) ? Ok() : NotFound("No source found to delete");
        }

        [HttpPost]
        public IActionResult GetSourceSettings([FromBody] DatasourceViewModel datasource) => PartialView("_SourceSettings", datasource);

        #endregion

        #region Templates

        public IActionResult AddTemplate() => PartialView("_TemplateSettings", new TemplateViewModel());

        [HttpPost("Dashboards/{dashboardId:guid}/{panelId:guid}/ApplyTemplate")]
        public IActionResult ApplyTemplate(Guid dashboardId, Guid panelId, TemplateViewModel template)
        {
            if (TryGetPanel(dashboardId, panelId, out var panel))
            {
                if (!panel.Subscriptions.TryGetValue(template.Id, out var subscription))
                    panel.TryAddSubscription(out subscription);

                subscription.NotifyUpdate(template.ToUpdate());

                return Ok(subscription.Id);
            }

            return NotFound("No such panel");
        }

        [HttpPost("Dashboards/{dashboardId:guid}/{panelId:guid}/DeleteTemplate")]
        public IActionResult ApplyTemplate(Guid dashboardId, Guid panelId, Guid templateId)
        {
            if (TryGetPanel(dashboardId, panelId, out var panel))
                panel.TryRemoveSubscription(templateId);

            return Ok();
        }

        #endregion

        private bool TryGetBoard(Guid id, out Dashboard board) => _dashboards.TryGetValue(id, out board);

        private bool TryGetPanel(Guid boardId, Guid id, out Panel panel)
        {
            panel = null;

            return TryGetBoard(boardId, out var board) && board.Panels.TryGetValue(id, out panel);
        }

        private bool TryGetSource(Guid boardId, Guid panelId, Guid sourceId, out PanelDatasource source)
        {
            source = null;

            return TryGetBoard(boardId, out var board) && board.Panels.TryGetValue(panelId, out var panel) && panel.Sources.TryGetValue(sourceId, out source);
        }
    }
}