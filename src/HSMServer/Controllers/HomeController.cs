using HSMCommon.Model.SensorsData;
using HSMServer.Authentication;
using HSMServer.HtmlHelpers;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using NLog;
using System.Collections.Generic;

namespace HSMServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;
        private readonly ITreeViewManager _treeManager;
        private readonly Logger _logger;

        public HomeController(IMonitoringCore monitoringCore, ITreeViewManager treeManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
            _treeManager = treeManager;
        }

        //public IActionResult Index()
        //{
        //    var result = _monitoringCore.GetSensorsTree(HttpContext.User as User);
        //    var tree = new TreeViewModel(result);
        //    var user = HttpContext.User as User;

        //    _treeManager.AddOrCreate(user, tree);

        //    return View(tree);
        //}
        public IActionResult Main()
        {
            return View();
        }

        [HttpPost]
        public HtmlString Update([FromBody]List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);

            return ViewHelper.CreateTreeWithLists(oldModel);
        }
    }
}
