﻿using HSMSensorDataObjects;
using HSMServer.Core.Authentication;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringHistoryProcessor;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;
using HSMServer.Core.MonitoringServerCore;
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
using System.Linq;
using System.Text;


namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        private const int DEFAULT_REQUESTED_COUNT = 40;
        private readonly IMonitoringCore _monitoringCore;
        private readonly ITreeViewManager _treeManager;
        private readonly IUserManager _userManager;
        private readonly IProductManager _productManager;
        private readonly IHistoryProcessorFactory _historyProcessorFactory;
        public HomeController(IMonitoringCore monitoringCore, ITreeViewManager treeManager, IUserManager userManager,
                IHistoryProcessorFactory factory, IProductManager productManager)
        {
            _monitoringCore = monitoringCore;
            _treeManager = treeManager;
            _userManager = userManager;
            _productManager = productManager;
            _historyProcessorFactory = factory;
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
        public void RemoveNode([FromQuery(Name = "Selected")] string encodedPath)
        {
            var decodedPath = SensorPathHelper.Decode(encodedPath);
            var user = HttpContext.User as User ?? _userManager.GetUserByUserName(HttpContext.User.Identity?.Name);

            string path = string.Empty;
            string product = string.Empty;
            if (decodedPath.Contains('/'))
                ParseProductAndPath(encodedPath, out product, out path);
            else
                product = decodedPath;

            if (string.IsNullOrEmpty(path))
            {
                var productEntity = _productManager.GetProductByName(product);
                if (productEntity == null) return;

                _monitoringCore.RemoveProduct(productEntity, out var error);
            }
            else
            {
                var model = _treeManager.GetTreeViewModel(user);
                var node = model.GetNode(decodedPath);

                var paths = new List<string>();
                GetSensorsPaths(node, paths);

                _monitoringCore.RemoveSensors(product, paths);
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

        public HtmlString UpdateTree([FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
                    if (sensor.TransactionType == TransactionType.Add)
                    sensor.TransactionType = TransactionType.Update;

                model = oldModel.Update(sensors);
            }

            return ViewHelper.UpdateTree(model);
        }

        [HttpPost]
        public HtmlString UpdateInvisibleLists([FromQuery(Name = "Selected")] string selectedList,
            [FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);

            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
                    if (sensor.TransactionType == TransactionType.Add)
                        sensor.TransactionType = TransactionType.Update;

                model = oldModel.Update(sensors);
            }
               
            return ViewHelper.CreateNotSelectedLists(selectedList, model);
        }

        [HttpPost]
        public ActionResult UpdateSelectedList([FromQuery(Name = "Selected")] string selectedList,
            [FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);

            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
                    if (sensor.TransactionType == TransactionType.Add)
                        sensor.TransactionType = TransactionType.Update;

                model = oldModel.Update(sensors);
            }

            int index = selectedList.IndexOf('_');
            var path = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var formattedPath = SensorPathHelper.Decode(path);

            var node = model.GetNode(formattedPath);
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

            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
                model = oldModel.Update(sensors);

            int index = selectedList.IndexOf('_');
            var formattedPath = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var path = SensorPathHelper.Decode(formattedPath);

            var node = model.GetNode(path);
            StringBuilder result = new StringBuilder();
            if (node.Sensors != null)

                foreach (var sensor in node.Sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                    {
                        sensor.TransactionType = TransactionType.Update;
                        result.Append(ListHelper.CreateSensor(path, sensor));
                    }
                }

            if (sensors != null && sensors.Count > 0)
            {
                var addedSensors = sensors.Where(s => s.TransactionType == TransactionType.Add).ToList();

                foreach (var sensor in addedSensors)
                    sensor.TransactionType = TransactionType.Update;

                model = model.Update(addedSensors);
            }

            return new HtmlString(result.ToString());
        }
        #endregion

        #region SensorsHistory

        [HttpPost]
        public HtmlString HistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            List<SensorHistoryData> unprocessedData = _monitoringCore.GetSensorHistory(HttpContext.User as User,
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
            var result = _monitoringCore.GetAllSensorHistory(HttpContext.User as User, product, path);

            return new HtmlString(TableHelper.CreateHistoryTable(result, encodedPath));
        }

        private HtmlString GetHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            List<SensorHistoryData> unprocessedData =
                _monitoringCore.GetSensorHistory(User as User, product, path, from.ToUniversalTime(),
                    to.ToUniversalTime());

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new HtmlString(TableHelper.CreateHistoryTable(processedData, SensorPathHelper.Encode($"{product}/{path}")));
        }

        [HttpPost]
        public JsonResult RawHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            ParseProductAndPath(model.Path, out string product, out string path);
            List<SensorHistoryData> unprocessedData = _monitoringCore.GetSensorHistory(HttpContext.User as User,
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
            var result = _monitoringCore.GetAllSensorHistory(HttpContext.User as User, product, path);

            return new JsonResult(result);
        }

        private JsonResult GetRawHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            List<SensorHistoryData> unprocessedData =
                _monitoringCore.GetSensorHistory(User as User, product, path, from.ToUniversalTime(),
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
            List<SensorHistoryData> historyList = _monitoringCore.GetSensorHistory(User as User, product, path,
                fromUTC, toUTC);
            string fileName = $"{product}_{path.Replace('/', '_')}_from_{fromUTC:s}_to{toUTC:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");
            return GetExportHistory(historyList, type, GetPeriodType(fromUTC, toUTC), fileName);
        }
        
        public FileResult ExportHistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            List<SensorHistoryData> historyList = _monitoringCore.GetAllSensorHistory(User as User,
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

            var fileContents = _monitoringCore.GetFileSensorValueBytes(HttpContext.User as User, product, path);

            var extension = _monitoringCore.GetFileSensorValueExtension(HttpContext.User as User, product, path);
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

            var fileContents = _monitoringCore.GetFileSensorValueBytes(HttpContext.User as User, product, path);
            var fileContentsStream = new MemoryStream(fileContents);
            var extension = _monitoringCore.GetFileSensorValueExtension(HttpContext.User as User, product, path);
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
