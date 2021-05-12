using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.Controllers
{
    internal class HomeController : Controller
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
            return View(new ConnectionViewModel
            {
                Url = "https://localhost",//"https://hsm.dev.soft-fx.eu",
                Port = 44333,
            });
        }
        [HttpPost]
        public IActionResult Index(ConnectionViewModel model)
        {
            //var result = ApiConnector.GetTree(model.Url, model.Port);
            var result = _monitoringCore.GetSensorsTree(HttpContext.User as User);

            model.Tree = new TreeViewModel(result);

            return View(model);
        }

    }
}
