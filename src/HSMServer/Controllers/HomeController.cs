using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMServer.Authentication;
using HSMServer.HtmlHelpers;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringHistoryProcessor;
using HSMServer.MonitoringHistoryProcessor.Factory;
using HSMServer.MonitoringHistoryProcessor.Processor;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;
        private readonly ITreeViewManager _treeManager;
        private readonly IUserManager _userManager;
        private readonly IHistoryProcessorFactory _historyProcessorFactory;
        private readonly ILogger<HomeController> _logger;
        public HomeController(IMonitoringCore monitoringCore, ITreeViewManager treeManager, IUserManager userManager,
            IHistoryProcessorFactory factory, ILogger<HomeController> logger)
        {
            _monitoringCore = monitoringCore;
            _treeManager = treeManager;
            _userManager = userManager;
            _historyProcessorFactory = factory;
            _logger = logger;
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
        public HtmlString UpdateTree([FromBody]List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);
            var model = oldModel.Update(sensors);

            return ViewHelper.CreateTree(model);
        }

        [HttpPost]
        public HtmlString UpdateInvisibleLists([FromQuery(Name = "Selected")] string selectedList,
            [FromBody] List<SensorData> sensors)
        {
            var user = HttpContext.User as User;
            var oldModel = _treeManager.GetTreeViewModel(user);

            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
                model = oldModel.Update(sensors);

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
                model = oldModel.Update(sensors);

            int index = selectedList.IndexOf('_');
            var path = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var formattedPath = SensorPathHelper.Decode(path);

            var node = model.GetNode(formattedPath);
            List<SensorDataViewModel> result = new List<SensorDataViewModel>();
            if (node?.Sensors != null)
                foreach(var sensor in node.Sensors)
                {
                    if (sensor.TransactionType != TransactionType.Add)
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
                foreach(var sensor in node.Sensors)
                {
                    if (sensor.TransactionType == TransactionType.Add)
                    {
                        result.Append(ListHelper.CreateSensor(formattedPath, sensor));
                        sensor.TransactionType = TransactionType.Update;
                    }
                }

            return new HtmlString(result.ToString());
        }

        [HttpPost]
        public HtmlString History([FromBody] GetSensorHistoryModel model)
        {
            var path = SensorPathHelper.Decode(model.Path);
            int index = path.IndexOf('/');
            model.Product = path.Substring(0, index);
            model.Path = path.Substring(index + 1, path.Length - index - 1);

            var result = _monitoringCore.GetSensorHistory(HttpContext.User as User, model);

            return new HtmlString(TableHelper.CreateHistoryTable(result));
        }

        [HttpPost]
        public JsonResult RawHistory([FromBody] GetSensorHistoryModel model)
        {
            var path = SensorPathHelper.Decode(model.Path);
            int index = path.IndexOf('/');
            model.Product = path.Substring(0, index);
            model.Path = path.Substring(index + 1, path.Length - index - 1);

            var commonHistory = _monitoringCore.GetSensorHistory(HttpContext.User as User, model);
            //var selected = commonHistory.Select(h => h.TypedData).ToList();

            return new JsonResult(commonHistory);
        }

        #region SensorsHistory

        [HttpPost]
        public HtmlString HistoryHour([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddHours(-1 * 1);
            return GetHistory(product, path, type, from, to, PeriodType.Hour);
        }

        [HttpPost]
        public HtmlString HistoryDay([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddDays(-1 * 1);
            return GetHistory(product, path, type, from, to, PeriodType.Day);
        }

        [HttpPost]
        public HtmlString HistoryThreeDays([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddDays(-1 * 3);
            return GetHistory(product, path, type, from, to, PeriodType.ThreeDays);
        }

        [HttpPost]
        public HtmlString HistoryWeek([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddDays(-1 * 7);
            return GetHistory(product, path, type, from, to, PeriodType.Week);
        }

        [HttpPost]
        public HtmlString HistoryMonth([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddMonths(-1 * 1);
            return GetHistory(product, path, type, from, to, PeriodType.Month);
        }

        [HttpPost]
        public HtmlString HistoryAll([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            var result = _monitoringCore.GetAllSensorHistory(HttpContext.User as User, product, path);

            return new HtmlString(TableHelper.CreateHistoryTable(result));
        }

        private HtmlString GetHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            _logger.LogInformation($"GetHistory for {product}/{path} from {from:u} to {to:u} started at {DateTime.Now:u}");
            List<SensorHistoryData> unprocessedData =
                _monitoringCore.GetSensorHistory(User as User, product, path, from, to);

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type, periodType);
            var processedData = processor.ProcessHistory(unprocessedData);
            _logger.LogInformation($"GetHistory for {product}/{path} from {from:u} to {to:u} finished at {DateTime.Now:u}");
            return new HtmlString(TableHelper.CreateHistoryTable(processedData));
        }

        [HttpPost]
        public JsonResult RawHistoryHour([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddHours(-1 * 1);
            return GetRawHistory(product, path, type, from, to, PeriodType.Hour);
        }

        [HttpPost]
        public JsonResult RawHistoryDay([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddDays(-1 * 1);
            return GetRawHistory(product, path, type, from, to, PeriodType.Day);
        }

        [HttpPost]
        public JsonResult RawHistoryThreeDays([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddDays(-1 * 3);
            return GetRawHistory(product, path, type, from, to, PeriodType.ThreeDays);
        }

        [HttpPost]
        public JsonResult RawHistoryWeek([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddDays(-1 * 7);
            return GetRawHistory(product, path, type, from, to, PeriodType.Week);
        }

        [HttpPost]
        public JsonResult RawHistoryMonth([FromQuery(Name = "Path")] string encodedPath, [FromQuery(Name = "Type")] int type)
        {
            ParseProductAndPath(encodedPath, out string product, out string path);
            DateTime to = DateTime.Now;
            DateTime from = to.AddMonths(-1 * 1);
            return GetRawHistory(product, path, type, from, to, PeriodType.Month);
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
            _logger.LogInformation($"GetRawHistory for {product}/{path} from {from:u} to {to:u} started at {DateTime.Now:u}");
            List<SensorHistoryData> unprocessedData =
                _monitoringCore.GetSensorHistory(User as User, product, path, from, to);

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type, periodType);
            var processedData = processor.ProcessHistory(unprocessedData);
            _logger.LogInformation($"GetRawHistory for {product}/{path} from {from:u} to {to:u} finished at {DateTime.Now:u}");
            return new JsonResult(processedData);
        }

        #endregion

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

        private void ParseProductAndPath(string encodedPath, out string product, out string path)
        {
            var decodedPath = SensorPathHelper.Decode(encodedPath);
            int index = decodedPath.IndexOf('/');
            product = decodedPath.Substring(0, index);
            path = decodedPath.Substring(index + 1, decodedPath.Length - index - 1);
        }
    }
}
