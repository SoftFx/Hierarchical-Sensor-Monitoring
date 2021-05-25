using HSMCommon.Model.SensorsData;
using HSMServer.Authentication;
using HSMServer.HtmlHelpers;
using HSMServer.Model;
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
        private readonly IUserManager _userManager;
        private readonly Logger _logger;

        public HomeController(IMonitoringCore monitoringCore, ITreeViewManager treeManager, IUserManager userManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
            _treeManager = treeManager;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            var user = HttpContext.User as User ?? _userManager.GetUserByUserName(HttpContext.User.Identity?.Name);
            var result = _monitoringCore.GetSensorsTree(user as User);
            var tree = new TreeViewModel(result);

            _treeManager.AddOrCreate(user, tree);

            return View(tree);
        }

        public IActionResult Main()
        {
            return View(new LoginModel());
        }

        [HttpPost]
        public HtmlString Update([FromBody]List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            var newModel = new TreeViewModel(sensors);
            var model = oldModel.Update(newModel);

            return ViewHelper.CreateTreeWithLists(model);
        }
    }
}
