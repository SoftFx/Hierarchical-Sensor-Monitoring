using HSMServer.Authentication;
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
        private readonly IDashboardManager _dashboardManager;


        public DashboardsController(IDashboardManager dashboardManager, IUserManager userManager) : base(userManager)
        {
            _dashboardManager = dashboardManager;
        }


        public IActionResult Index() => View(_dashboardManager.GetValues().Select(d => new DashboardViewModel(d)).ToList());

        [HttpPost]
        public async Task<IActionResult> AddDashboard(DashboardViewModel newDashboard)
        {
            if (!ModelState.IsValid)
                return View(nameof(EditDashboard), newDashboard);

            await _dashboardManager.TryAdd(newDashboard.ToDashboardAdd(CurrentUser), out var dashboard);

            return RedirectToAction(nameof(EditDashboard), new { dashboardId = dashboard.Id });
        }

        [HttpGet]
        public IActionResult EditDashboard(Guid? dashboardId) =>
            dashboardId.HasValue && _dashboardManager.TryGetValue(dashboardId.Value, out var dashboard)
                ? View(nameof(EditDashboard), new DashboardViewModel(dashboard))
                : View(nameof(EditDashboard));

        [HttpPost]
        public void EditDashboard(DashboardViewModel dashboard)
        {

        }
    }
}
