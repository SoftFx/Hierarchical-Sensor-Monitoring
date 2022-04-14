using HSMSensorDataObjects;
using HSMServer.Core.Authentication;
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
        private readonly Model.TreeViewModels.TreeViewModel _treeViewModel;


        public HomeController(ISensorsInterface sensorsInterface, ITreeViewManager treeManager,
            IUserManager userManager, IHistoryProcessorFactory factory, IProductManager productManager,
            Model.TreeViewModels.TreeViewModel treeViewModel)
        {
            _sensorsInterface = sensorsInterface;
            _treeManager = treeManager;
            _userManager = userManager;
            _productManager = productManager;
            _historyProcessorFactory = factory;
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index() => View(_treeViewModel);

        [HttpPost]
        public IActionResult SelectNode([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return PartialView("_TreeNodeSensors", node);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                return PartialView("_TreeNodeSensors", sensor);

            return PartialView("_TreeNodeSensors", null);
        }

        [HttpPost]
        public IActionResult RefreshTree() => PartialView("_Tree", _treeViewModel);

        [HttpPost]
        public void RemoveNode([FromQuery(Name = "Selected")] string encodedPath)
        {
            if (encodedPath.Contains("sensor_"))
            {
                encodedPath = encodedPath.Substring("sensor_".Length);
            }

            var decodedPath = SensorPathHelper.Decode(encodedPath);
            var user = HttpContext.User as User ?? _userManager.GetUserByUserName(HttpContext.User.Identity?.Name);

            if (decodedPath.Contains('/'))
            {
                //remove node
                ParseProductAndPath(encodedPath, out var product, out var path);
                var model = _treeManager.GetTreeViewModel(user).Clone();
                var node = model.GetNode(decodedPath);

                var paths = new List<string>(1 << 2);
                if (node == null) //remove single sensor
                {
                    ParseProductPathAndSensor(encodedPath, out product, out path, out var sensor);
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

                var productEntity = _productManager.GetProductByName(product);

                _sensorsInterface.RemoveSensors(product, productEntity.Key, paths);
            }

            else
            {
                //remove product
                var productEntity = _productManager.GetProductByName(decodedPath);
                if (productEntity == null)
                    return;

                _sensorsInterface.HideProduct(productEntity, out _);
            }
        }

        private void GetSensorsPaths(NodeViewModel node, List<string> paths)
        {
            var path = node.Path[(node.Path.IndexOf('/') + 1)..];

            if (node.Sensors != null && !node.Sensors.IsEmpty)
                foreach (var (name, _) in node.Sensors)
                    paths.Add($"{path}/{name}");

            if (node.Nodes != null && !node.Nodes.IsEmpty)
                foreach (var (_, child) in node.Nodes)
                    GetSensorsPaths(child, paths);
        }

        #region Update

        [HttpPost]
        public HtmlString UpdateTree([FromBody] List<SensorData> sensors)
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
            else
                oldModel.UpdateNodeCharacteristics();

            return ViewHelper.UpdateTree(model.Clone());
        }

        private static void PrintTree(NodeViewModel node, StringBuilder str,
            string indent, bool isLast)
        {
            str.Append($"{indent} +- node: {node.Name} path: {node.Path}\n");
            indent += isLast ? "\t" : "|\t";

            if (node.Nodes != null && node.Nodes.Count > 0)
            {
                int i = 0;

                foreach (var (_, child) in node.Nodes)
                {
                    PrintTree(child, str, indent, i == node.Nodes.Count - 1);
                    i++;
                }
            }


            if (node.Sensors != null && node.Sensors.Count > 0)
                foreach (var (name, _) in node.Sensors)
                    str.Append($"{indent}--sensor: {name}\n");
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

            if (selectedList == null) selectedList = string.Empty;

            int index = selectedList.IndexOf('_');
            var formattedPath = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            return ViewHelper.CreateNotSelectedLists(formattedPath, model.Clone());
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

            if (selectedList == null) selectedList = string.Empty;

            int index = selectedList.IndexOf('_');
            var path = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var formattedPath = SensorPathHelper.Decode(path);
            //var nodePath = formattedPath.Substring(0, formattedPath.LastIndexOf('/'));
            var nodePath = formattedPath;

            var node = model.Clone().GetNode(nodePath);

            var result = new List<SensorDataViewModel>(node?.Sensors?.Count ?? 0);
            if (node?.Sensors != null)
                foreach (var (_, sensor) in node.Sensors)
                    result.Add(new SensorDataViewModel(selectedList, sensor));

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

            if (selectedList == null) selectedList = string.Empty;

            int index = selectedList.IndexOf('_');
            var formattedPath = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var path = SensorPathHelper.Decode(formattedPath);

            var node = model.GetNode(path);
            var result = new StringBuilder(1 << 2);
            if (node?.Sensors != null)
                foreach (var (_, sensor) in node.Sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                    {
                        sensor.TransactionType = TransactionType.Update;
                        result.Append(ListHelper.CreateSensor(path, sensor));
                    }
                }

            return new HtmlString(result.ToString());
        }

        [HttpPost]
        public List<string> RemoveSensors([FromBody] List<SensorData> sensors)
        {
            var ids = new List<string>();

            if (sensors != null && sensors.Count > 0)
                foreach (var sensor in sensors)
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
            List<SensorHistoryData> unprocessedData = _sensorsInterface.GetSensorHistory(product, path, DEFAULT_REQUESTED_COUNT);
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
            var result = _sensorsInterface.GetAllSensorHistory(product, path);

            return new HtmlString(TableHelper.CreateHistoryTable(result, encodedPath));
        }

        private HtmlString GetHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            List<SensorHistoryData> unprocessedData =
                _sensorsInterface.GetSensorHistory(product, path, from.ToUniversalTime(), to.ToUniversalTime());

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new HtmlString(TableHelper.CreateHistoryTable(processedData, SensorPathHelper.Encode($"{product}/{path}")));
        }

        [HttpPost]
        public JsonResult RawHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            List<SensorHistoryData> unprocessedData = _sensorsInterface.GetSensorHistory(product, path, DEFAULT_REQUESTED_COUNT);
            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)model.Type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new JsonResult(processedData);
        }

        [HttpPost]
        public JsonResult RawHistory([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            return GetRawHistory(product, path, model.Type, model.From, model.To);
        }

        [HttpPost]
        public JsonResult RawHistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            var result = _sensorsInterface.GetAllSensorHistory(product, path);

            return new JsonResult(result);
        }

        private JsonResult GetRawHistory(string product, string path, int type, DateTime from, DateTime to)
        {
            List<SensorHistoryData> unprocessedData =
                _sensorsInterface.GetSensorHistory(product, path, from.ToUniversalTime(), to.ToUniversalTime());

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
            List<SensorHistoryData> historyList = _sensorsInterface.GetSensorHistory(product, path,
                fromUTC, toUTC);
            string fileName = $"{product}_{path.Replace('/', '_')}_from_{fromUTC:s}_to{toUTC:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");
            return GetExportHistory(historyList, type, fileName);
        }

        public FileResult ExportHistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            List<SensorHistoryData> historyList = _sensorsInterface.GetAllSensorHistory(product, path);
            string fileName = $"{product}_{path.Replace('/', '_')}_all_{DateTime.Now.ToUniversalTime():s}.csv";
            return GetExportHistory(historyList, type, fileName);
        }

        private FileResult GetExportHistory(List<SensorHistoryData> dataList,
            int type, string fileName)
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

            var (content, extension) = _sensorsInterface.GetFileSensorValueData(product, path);

            var fileName = $"{path.Replace('/', '_')}.{extension}";

            return File(content, GetFileTypeByExtension(fileName), fileName);
        }

        [HttpPost]
        public IActionResult GetFileStream([FromQuery(Name = "Selected")] string selectedSensor)
        {
            var path = SensorPathHelper.Decode(selectedSensor);
            int index = path.IndexOf('/');
            var product = path.Substring(0, index);
            path = path.Substring(index + 1, path.Length - index - 1);

            var (content, extension) = _sensorsInterface.GetFileSensorValueData(product, path);

            var fileContentsStream = new MemoryStream(content);
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

        #region Sensor info

        [HttpGet]
        public HtmlString GetSensorInfo([FromQuery(Name = "Path")] string encodedPath)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            var info = _sensorsInterface.GetSensorInfo(product, path);
            if (info == null)
                return new HtmlString(string.Empty);

            SensorInfoViewModel viewModel = new SensorInfoViewModel(info);
            return ViewHelper.CreateSensorInfoTable(viewModel);
        }

        [HttpPost]
        public void UpdateSensorInfo([FromBody] UpdateSensorInfoViewModel updateModel)
        {
            ParseProductAndPath(updateModel.EncodedPath, out var product, out var path);
            var info = _sensorsInterface.GetSensorInfo(product, path);
            SensorInfoViewModel viewModel = new SensorInfoViewModel(info);
            viewModel.Update(updateModel);
            var infoFromViewModel = CreateModelFromViewModel(viewModel);
            _sensorsInterface.UpdateSensorInfo(infoFromViewModel);
        }

        #endregion

        private void ParseProductAndPath(string encodedId, out string product, out string path)
        {
            var decodedId = SensorPathHelper.DecodeGuid(encodedId);

            _treeViewModel.Sensors.TryGetValue(decodedId, out var sensor);

            path = sensor?.Path;
            product = sensor?.Product;
        }

        private static void ParseProductPathAndSensor(string encodedPath, out string product,
            out string path, out string sensor)
        {
            var decodedPath = SensorPathHelper.Decode(encodedPath);
            product = decodedPath[..decodedPath.IndexOf('/')];

            var withoutProduct = decodedPath[(product.Length + 1)..];
            sensor = withoutProduct[(withoutProduct.LastIndexOf('/') + 1)..];

            if (withoutProduct.Contains('/'))
                path = withoutProduct.Substring(0, withoutProduct.Length - sensor.Length - 1);
            else
                path = string.Empty;
        }

        private static PeriodType GetPeriodType(DateTime from, DateTime to)
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

        private static SensorInfo CreateModelFromViewModel(SensorInfoViewModel viewModel)
        {
            var result = new SensorInfo
            {
                ProductName = viewModel.ProductName,
                ExpectedUpdateInterval = TimeSpan.Parse(viewModel.ExpectedUpdateInterval),
                Unit = viewModel.Unit,
                Path = viewModel.Path,
                Description = viewModel.Description
            };

            return result;
        }
    }
}
