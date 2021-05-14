using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;
        private readonly Logger _logger;
        public HomeController(IMonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
        }

        public IActionResult Index()
        {
            var result = _monitoringCore.GetSensorsTree(HttpContext.User as User);

            return View( new TreeViewModel(result));
        }
    }
}
