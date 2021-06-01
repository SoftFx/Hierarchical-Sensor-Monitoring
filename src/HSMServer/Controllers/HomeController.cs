using System;
using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMServer.Authentication;
using HSMServer.HtmlHelpers;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using NLog;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using HSMServer.DataLayer.Model;
using HSMServer.Constants;

namespace HSMServer.Controllers
{
    [Authorize]
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
            var result = _monitoringCore.GetSensorsTree(user);
            var tree = new TreeViewModel(result);

            _treeManager.AddOrCreate(user, tree);

            return View(tree);
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


        [HttpPost]
        public HtmlString History([FromBody]GetSensorHistoryModel model)
        {
            model.Product = model.Product.Replace('-', ' ');
            model.Path = model.Path?.Replace('_', '/').Replace('-', ' ');
            var result = _monitoringCore.GetSensorHistory(HttpContext.User as User, model);

            return new HtmlString(ListHelper.CreateHistoryList(result));
        }

        [HttpPost]
        public IActionResult ViewFile([FromBody] GetFileSensorModel model)
        {
            var fullPath = model.Path?.Replace('_', '/').Replace('-', ' ');
            int ind = fullPath.IndexOf('/');
            string product = fullPath.Substring(0, ind);
            string path = fullPath.Substring(ind + 1, fullPath.Length - ind - 1);
            var fileContents =
                Encoding.ASCII.GetBytes(_monitoringCore.GetFileSensorValue(HttpContext.User as User, product, path));
            var extension = _monitoringCore.GetFileSensorValueExtension(HttpContext.User as User, product, path);
            var fileName = $"{model.Product} {model.Path}.{extension}";
            var fileType = GetFileTypeByExtension(fileName.Replace(".",""));
            return File(fileContents, fileType, fileName);
        }

        [HttpPost]
        public IActionResult DownloadFile([FromBody] GetFileSensorModel model)
        {
            throw new NotImplementedException();
        }

        private string GetFileTypeByExtension(string extension)
        {
            switch (extension)
            {
                case "pdf":
                    return "application/pdf";
                case "html":
                    return "html";
                case "txt":
                    return "text/plain";
                default:
                    return "text/plain";
            }
        }

        public IActionResult Products()
        {
            var products = _monitoringCore.GetProductsList(HttpContext.User as User);

            return View(products.Select(x => new ProductViewModel(x))?.ToList());
        }

        public IActionResult AddProduct(string productName)
        {
            _monitoringCore.AddProduct(HttpContext.User as User, productName, 
                out Product product, out string error);

            return RedirectToAction(ViewConstants.ProductsAction);
        }

        public IActionResult RemoveProduct([FromQuery(Name="Product")]string productName)
        {
            //_monitoringCore.RemoveProduct(HttpContext.User as User, productName,
               // out Product product, out string error);

            return RedirectToAction(ViewConstants.ProductsAction);
        }
    }
}
