using HSMServer.Authentication;
using HSMServer.TablePages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The rows of a table sensor for the web UI (#1847): its chart and the download of a range. Signed in by cookie;
    /// access, limits and answers are <see cref="TablePageService"/>'s. Both actions only read, so neither takes an
    /// antiforgery token, like the other chart reads of the UI (<c>SensorHistoryController.ChartHistory</c>,
    /// <c>NodeChartHistory</c>).
    /// </summary>
    [Authorize]
    public sealed class TableSensorsController : BaseController
    {
        private readonly TablePageService _pages;


        public TableSensorsController(IUserManager userManager, TablePageService pages) : base(userManager)
        {
            _pages = pages;
        }


        /// <summary>POST /TableSensors/Chart: the chart of a range of one table sensor.</summary>
        [HttpPost]
        public Task<IActionResult> Chart([FromBody] TableChartRequest request) =>
            _pages.ChartAsync(CurrentUser, request, HttpContext.RequestAborted);

        /// <summary>GET /TableSensors/Download?id=&amp;from=&amp;to=: the rows of a range as CSV.</summary>
        [HttpGet]
        public Task<IActionResult> Download([FromQuery] string id, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
            _pages.DownloadAsync(HttpContext, CurrentUser, id, from, to);
    }
}
