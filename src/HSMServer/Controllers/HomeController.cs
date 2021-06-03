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
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using System.Linq;
using HSMServer.DataLayer.Model;
using HSMServer.Constants;
using HSMServer.Model.Validators;
using FluentValidation.Results;

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
            string product = model.Product.Replace('-', ' ');
            string path = model.Path.Replace('_', '/');
            var fileContents =
                Encoding.ASCII.GetBytes(_monitoringCore.GetFileSensorValue(HttpContext.User as User, product, path));
            var fileContentsStream = new MemoryStream(fileContents);
            var extension = _monitoringCore.GetFileSensorValueExtension(HttpContext.User as User, product, path);
            var fileName = $"{model.Path}.{extension}";
            return File(fileContentsStream, GetFileTypeByExtension(fileName), fileName);
        }

        [HttpPost]
        public IActionResult DownloadFile([FromBody] GetFileSensorModel model)
        {
            string product = model.Product.Replace('-', ' ');
            string path = model.Path.Replace('_', '/');
            var fileContents =
                Encoding.ASCII.GetBytes(_monitoringCore.GetFileSensorValue(HttpContext.User as User, product, path));
            var fileContentsStream = new MemoryStream(fileContents);
            var extension = _monitoringCore.GetFileSensorValueExtension(HttpContext.User as User, product, path);
            var fileName = $"{model.Path}.{extension}";
            //return File(fileContents, System.Net.Mime.MediaTypeNames.Application.Octet, fileName);
            return File(fileContentsStream, GetFileTypeByExtension(fileName), fileName);
            //return File(fileContentsStream, fileName);
        }
        public IActionResult Products()
        {
            //var products = _monitoringCore.GetProducts(HttpContext.User as User);
            var products = _monitoringCore.GetAllProducts();

            return View(products.Select(x => new ProductViewModel(x))?.ToList());
        }

        public void CreateProduct([FromQuery(Name = "Product")] string productName)
        {
            Product product = new Product();
            product.Name = productName;

            ProductValidator validator = new ProductValidator(_monitoringCore);
            var results = validator.Validate(product);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = GetErrorString(results.Errors);
                return;
            }

            TempData.Remove(TextConstants.TempDataErrorText);
            _monitoringCore.AddProduct(HttpContext.User as User, productName,
                out Product newProduct, out string error);
        }

        public void RemoveProduct([FromQuery(Name="Product")]string productName)       
        {
            _monitoringCore.RemoveProduct(HttpContext.User as User, productName,
                out Product product, out string error);
        }

        private string GetFileTypeByExtension(string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = System.Net.Mime.MediaTypeNames.Application.Octet;
            }

            return contentType;
        }

        private string GetErrorString(List<ValidationFailure> errors)
        {
            StringBuilder result = new StringBuilder();

            foreach(var error in errors)
            {
                result.Append(error.ErrorMessage + "\n");
            }

            return result.ToString();
        }
    }
}
