using HSMSensorDataObjects;
using HSMServer.Core.Authentication;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.MonitoringHistoryProcessor;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;
using HSMServer.Core.Products;
using HSMServer.Helpers;
using HSMServer.HtmlHelpers;
using HSMServer.Model;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        private const int DEFAULT_REQUESTED_COUNT = 40;
        private readonly ISensorsInterface _sensorsInterface;
        private readonly ITreeViewManager _treeManager;
        private readonly IUserManager _userManager;
        private readonly IProductManager _productManager;
        private readonly IHistoryProcessorFactory _historyProcessorFactory;

        private readonly Logger _logger;

        public HomeController(ISensorsInterface sensorsInterface, ITreeViewManager treeManager,
            IUserManager userManager, IHistoryProcessorFactory factory, IProductManager productManager,
            ILogger<HomeController> logger)
        {
            _sensorsInterface = sensorsInterface;
            _treeManager = treeManager;
            _userManager = userManager;
            _productManager = productManager;
            _historyProcessorFactory = factory;

            _logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger(); ;

        }

        public IActionResult Index()
        {
            var user = HttpContext.User as User ?? _userManager.GetUserByUserName(HttpContext.User.Identity?.Name);
            var tree = _treeManager.GetTreeViewModel(user);
            if (tree == null)
            {
                var result = _sensorsInterface.GetSensorsTree(user);
                tree = new TreeViewModel(result);
                _treeManager.AddOrCreate(user, tree);
            }

            return View(tree);
        }

        [HttpPost]
        public void RemoveNode([FromQuery(Name = "Selected")] string encodedPath)
        {
            if (encodedPath.Contains("sensor_"))
            {
                encodedPath = encodedPath.Substring("sensor_".Length);
            }

            var decodedPath = SensorPathHelper.Decode(encodedPath);
            var user = HttpContext.User as User ?? _userManager.GetUserByUserName(HttpContext.User.Identity?.Name);

            string path = string.Empty;
            string product = string.Empty;
            string sensor = string.Empty;
            if (decodedPath.Contains('/'))
            {
                //remove node
                ParseProductAndPath(encodedPath, out product, out path);
                var model = _treeManager.GetTreeViewModel(user);
                var node = model.GetNode(decodedPath);

                var paths = new List<string>();
                if (node == null) //remove single sensor
                {
                    ParseProductPathAndSensor(encodedPath, out product, out path, out sensor);
                    node = string.IsNullOrEmpty(path) ?
                        model.GetNode(product) : model.GetNode($"{product}/{path}");

                    if (node != null)
                    {
                        if (string.IsNullOrEmpty(path))
                            paths.Add(sensor);
                        else 
                            paths.Add($"{path}/{sensor}");
                    }
                }
                else //remove sensors
                    GetSensorsPaths(node, paths);

                _sensorsInterface.RemoveSensors(product, paths);
            }

            else
            {
                //remove product
                var productEntity = _productManager.GetProductByName(decodedPath);
                if (productEntity == null) return;

                _sensorsInterface.HideProduct(productEntity, out var error);
            }
        }

        private void GetSensorsPaths(NodeViewModel node, List<string> paths)
        {
            var path = node.Path.Substring(node.Path.IndexOf('/') + 1);

            if (node.Sensors != null && node.Sensors.Count > 0)
                foreach (var sensor in node.Sensors)
                    paths.Add($"{path}/{sensor.Name}");

            if (node.Nodes != null && node.Nodes.Count > 0)
                foreach (var child in node.Nodes)
                    GetSensorsPaths(child, paths);
        }

        #region Update

        [HttpPost]
        public void SortByName()
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            var model = oldModel.SortByName();

            _treeManager.AddOrCreate(user, model);
        }

        [HttpPost]
        public void SortByTime()
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            var model = oldModel.SortByTime();

            _treeManager.AddOrCreate(user, model);
        }

        [HttpPost]

        public HtmlString UpdateTree([FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            if (oldModel == null)
                return new HtmlString("");

            if (sensors != null && sensors.Count > 0)
            foreach(var sensor in sensors)
            {
                _logger.Info($"UpdateTree: Product={sensor.Product} Path={sensor.Path}" +
                    $"Type={sensor.TransactionType}");
            }

            var model = oldModel;

            StringBuilder str = new StringBuilder();
            str.Append("Old Tree\n");
            int i = 0;
            foreach (var node in model.Nodes)
            {
                PrintTree(node, str, "", i == model.Nodes.Count - 1);
                str.Append("-----------\n");
                i++;
            }

            _logger.Info(str.ToString());

            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                        sensor.TransactionType = TransactionType.Update;
                }

                model = oldModel.Update(sensors);
            }
            else 
                oldModel.UpdateNodeCharacteristics();

            str = new StringBuilder();
            str.Append("Updated Tree\n");
            i = 0;
            foreach (var node in model.Nodes)
            {
                PrintTree(node, str, "", i == model.Nodes.Count - 1);
                str.Append("-----------\n");
                i++;
            }

            _logger.Info(str.ToString());

            try
            {
                return ViewHelper.UpdateTree(model.Clone());
            }
            catch(Exception ex)
            {
                _logger.Error(ex.Message);
                _logger.Error(ex.StackTrace);

                return HtmlString.Empty;
            }
        }

        private static void PrintTree(NodeViewModel node, StringBuilder str,
            string indent, bool isLast)
        {
            str.Append(indent + "+- " + $"node: {node.Name} path: {node.Path}\n");
            indent += isLast ? "\t" : "|\t";

            int i = 0;
            if (node.Nodes != null && node.Nodes.Count > 0)
                foreach (var child in node.Nodes)
                {
                    PrintTree(child, str, indent, i == node.Nodes.Count - 1);
                    i++;
                }


            if (node.Sensors != null && node.Sensors.Count > 0)
                foreach (var sensor in node.Sensors)
                    str.Append(indent + "--" + $"sensor: {sensor.Name}\n");
        }

        [HttpPost]
        public HtmlString UpdateInvisibleLists([FromQuery(Name = "Selected")] string selectedList,
            [FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            if (oldModel == null)
                return new HtmlString("");

            var model = oldModel; 
            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                        sensor.TransactionType = TransactionType.Update;
                }
                    

                model = oldModel.Update(sensors);
            }
               
            return ViewHelper.CreateNotSelectedLists(selectedList, model.Clone());
        }

        [HttpPost]
        public ActionResult UpdateSelectedList([FromQuery(Name = "Selected")] string selectedList,
            [FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            if (oldModel == null)
                return Json("");

            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                        sensor.TransactionType = TransactionType.Update;
                }
                    

                model = oldModel.Update(sensors);
            }

            int index = selectedList.IndexOf('_');
            var path = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var formattedPath = SensorPathHelper.Decode(path);
            //var nodePath = formattedPath.Substring(0, formattedPath.LastIndexOf('/'));
            var nodePath = formattedPath;

            var node = model.Clone().GetNode(nodePath);
            List<SensorDataViewModel> result = new List<SensorDataViewModel>();
            if (node?.Sensors != null)

                foreach (var sensor in node.Sensors)
                {
                    //if (sensor.TransactionType != TransactionType.Add)
                    result.Add(new SensorDataViewModel(selectedList, sensor));
                }

            return Json(result);
        }

        [HttpPost]
        public HtmlString AddNewSensors([FromQuery(Name = "Selected")] string selectedList,
            [FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            if (oldModel == null)
                return new HtmlString("");


            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
                model = oldModel.Update(sensors);

            int index = selectedList.IndexOf('_');
            var formattedPath = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var path = SensorPathHelper.Decode(formattedPath);

            var node = model.Clone().GetNode(path);
            StringBuilder result = new StringBuilder();
            if (node?.Sensors != null)

                foreach (var sensor in node.Sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                    {
                        sensor.TransactionType = TransactionType.Update;
                        result.Append(ListHelper.CreateSensor(path, sensor));
                    }
                }

            //if (sensors != null && sensors.Count > 0)
            //{
            //    var addedSensors = sensors.Where(s => s.TransactionType == TransactionType.Add).ToList();

            //    foreach (var sensor in addedSensors)
            //    {
            //        sensor.TransactionType = TransactionType.Update;
            //    }
                    

            //    model = model.Update(addedSensors);
            //}

            return new HtmlString(result.ToString());
        }

        [HttpPost]
        public List<string> RemoveSensors([FromBody] List<SensorData> sensors)
        {
            var ids = new List<string>();

            if (sensors != null && sensors.Count > 0)
                foreach(var sensor in sensors)
                {
                    if (sensor.TransactionType == TransactionType.Delete)
                        ids.Add(SensorPathHelper.Encode($"{sensor.Product}/{sensor.Path}"));
                }

            return ids;

        }
        #endregion

        #region SensorsHistory

        [HttpPost]
        public HtmlString HistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            List<SensorHistoryData> unprocessedData = _sensorsInterface.GetSensorHistory(HttpContext.User as User,
                product, path, DEFAULT_REQUESTED_COUNT);
            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)model.Type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new HtmlString(TableHelper.CreateHistoryTable(processedData, model.Path));
        }

        [HttpPost]
        public HtmlString History([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            return GetHistory(product, path, model.Type, model.From, model.To,
                GetPeriodType(model.From, model.To));
        }


        [HttpPost]
        public HtmlString HistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            var result = _sensorsInterface.GetAllSensorHistory(HttpContext.User as User, product, path);

            return new HtmlString(TableHelper.CreateHistoryTable(result, encodedPath));
        }

        private HtmlString GetHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            List<SensorHistoryData> unprocessedData =
                _sensorsInterface.GetSensorHistory(User as User, product, path, from.ToUniversalTime(),
                    to.ToUniversalTime());

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new HtmlString(TableHelper.CreateHistoryTable(processedData, SensorPathHelper.Encode($"{product}/{path}")));
        }

        [HttpPost]
        public JsonResult RawHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            List<SensorHistoryData> unprocessedData = _sensorsInterface.GetSensorHistory(HttpContext.User as User,
                product, path, DEFAULT_REQUESTED_COUNT);
            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)model.Type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new JsonResult(processedData);
        }

        [HttpPost]
        public JsonResult RawHistory([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            return GetRawHistory(product, path, model.Type, model.From, model.To,
                GetPeriodType(model.From, model.To));
        }

        [HttpPost]
        public JsonResult RawHistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            var result = _sensorsInterface.GetAllSensorHistory(HttpContext.User as User, product, path);

            return new JsonResult(result);
        }

        private JsonResult GetRawHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            List<SensorHistoryData> unprocessedData =
                _sensorsInterface.GetSensorHistory(User as User, product, path, from.ToUniversalTime(),
                    to.ToUniversalTime());

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new JsonResult(processedData);
        }

        public FileResult ExportHistory([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type,
            [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime fromUTC = from.ToUniversalTime();
            DateTime toUTC = to.ToUniversalTime();
            List<SensorHistoryData> historyList = _sensorsInterface.GetSensorHistory(User as User, product, path,
                fromUTC, toUTC);
            string fileName = $"{product}_{path.Replace('/', '_')}_from_{fromUTC:s}_to{toUTC:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");
            return GetExportHistory(historyList, type, GetPeriodType(fromUTC, toUTC), fileName);
        }
        
        public FileResult ExportHistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            List<SensorHistoryData> historyList = _sensorsInterface.GetAllSensorHistory(User as User,
                product, path);
            string fileName = $"{product}_{path.Replace('/', '_')}_all_{DateTime.Now.ToUniversalTime():s}.csv";
            return GetExportHistory(historyList, type, PeriodType.All, fileName);
        }

        private FileResult GetExportHistory(List<SensorHistoryData> dataList,
            int type, PeriodType periodType, string fileName)
        {
            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type);
            string csv = processor.GetCsvHistory(dataList);
            byte[] fileContents = Encoding.UTF8.GetBytes(csv);
            return File(fileContents, GetFileTypeByExtension(fileName), fileName);
        }
        #endregion

        #region File
        [HttpGet]
        public FileResult GetFile([FromQuery(Name = "Selected")] string selectedSensor)
        {
            var path = SensorPathHelper.Decode(selectedSensor);
            int index = path.IndexOf('/');
            var product = path.Substring(0, index);
            path = path.Substring(index + 1, path.Length - index - 1);

            var fileContents = _sensorsInterface.GetFileSensorValueBytes(HttpContext.User as User, product, path);

            var extension = _sensorsInterface.GetFileSensorValueExtension(HttpContext.User as User, product, path);
            var fileName = $"{path.Replace('/', '_')}.{extension}";

            return File(fileContents, GetFileTypeByExtension(fileName), fileName);
        }

        [HttpPost]
        public IActionResult GetFileStream([FromQuery(Name = "Selected")] string selectedSensor)
        {
            
            var path = SensorPathHelper.Decode(selectedSensor);
            int index = path.IndexOf('/');
            var product = path.Substring(0, index);
            path = path.Substring(index + 1, path.Length - index - 1);

            var fileContents = _sensorsInterface.GetFileSensorValueBytes(HttpContext.User as User, product, path);
            var fileContentsStream = new MemoryStream(fileContents);
            var extension = _sensorsInterface.GetFileSensorValueExtension(HttpContext.User as User, product, path);
            var fileName = $"{path.Replace('/', '_')}.{extension}";

            return File(fileContentsStream, GetFileTypeByExtension(fileName), fileName);
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
        #endregion

        private void ParseProductAndPath(string encodedPath, out string product, out string path)
        {
            var decodedPath = SensorPathHelper.Decode(encodedPath);
            int index = decodedPath.IndexOf('/');
            product = decodedPath.Substring(0, index);
            path = decodedPath.Substring(index + 1, decodedPath.Length - index - 1);
        }

        private void ParseProductPathAndSensor(string encodedPath, out string product,
            out string path, out string sensor)
        {
            var decodedPath = SensorPathHelper.Decode(encodedPath);
            int index = decodedPath.IndexOf('/');
            product = decodedPath.Substring(0, index);

            var withoutProduct = decodedPath.Substring(product.Length + 1);
            sensor = withoutProduct.Substring(withoutProduct.LastIndexOf('/') + 1);

            if (withoutProduct.Contains('/'))
                path = withoutProduct.Substring(0, withoutProduct.Length - sensor.Length - 1);
            else
                path = string.Empty;
        }

        private PeriodType GetPeriodType(DateTime from, DateTime to)
        {
            var difference = to - from;
            if (difference.Days > 29)
                return PeriodType.Month;

            if (difference.Days > 6)
                return PeriodType.Week;

            if (difference.Days > 2)
                return PeriodType.ThreeDays;

            if (difference.TotalHours > 1)
                return PeriodType.Day;
            return PeriodType.Hour;
        }
    }
}
