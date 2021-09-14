using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringHistoryProcessor;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Helpers;
using HSMServer.HtmlHelpers;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
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
            var model = oldModel;
            if (sensors != null && sensors.Count > 0)
            {
                foreach (var sensor in sensors)
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
                    sensor.TransactionType = TransactionType.Update;

                model = oldModel.Update(sensors);
            }

            int index = selectedList.IndexOf('_');
            var path = selectedList.Substring(index + 1, selectedList.Length - index - 1);
            var formattedPath = SensorPathHelper.Decode(path);

            var node = model.GetNode(formattedPath);
            List<SensorDataViewModel> result = new List<SensorDataViewModel>();
            if (node?.Sensors != null)
                foreach(var sensor in node.Sensors)
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
                foreach(var sensor in node.Sensors)
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

        //[HttpPost]
        //public HtmlString History([FromBody] GetSensorHistoryModel model)
        //{
        //    var path = SensorPathHelper.Decode(model.Path);
        //    int index = path.IndexOf('/');
        //    model.Product = path.Substring(0, index);
        //    model.Path = path.Substring(index + 1, path.Length - index - 1);

        //    var result = _monitoringCore.GetSensorHistory(HttpContext.User as User, model);

        //    return new HtmlString(TableHelper.CreateHistoryTable(result));
        //}

        //[HttpPost]
        //public JsonResult RawHistory([FromBody] GetSensorHistoryModel model)
        //{
        //    var path = SensorPathHelper.Decode(model.Path);
        //    int index = path.IndexOf('/');
        //    model.Product = path.Substring(0, index);
        //    model.Path = path.Substring(index + 1, path.Length - index - 1);

        //    var commonHistory = _monitoringCore.GetSensorHistory(HttpContext.User as User, model);
        //    //var selected = commonHistory.Select(h => h.TypedData).ToList();

        //    return new JsonResult(commonHistory);
        //}

        #region SensorsHistory

        [HttpPost]
        public HtmlString History([FromBody]GetSensorHistoryModel model)
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

            return new HtmlString(TableHelper.CreateHistoryTable(result));
        }

        private HtmlString GetHistory(string product, string path, int type, DateTime from, DateTime to, PeriodType periodType)
        {
            List<SensorHistoryData> unprocessedData =
                _monitoringCore.GetSensorHistory(User as User, product, path, from.ToUniversalTime(),
                    to.ToUniversalTime());

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type, periodType);
            var processedData = processor.ProcessHistory(unprocessedData);
            return new HtmlString(TableHelper.CreateHistoryTable(processedData));
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

            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type, periodType);
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
            IHistoryProcessor processor = _historyProcessorFactory.CreateProcessor((SensorType)type, periodType);
            string csv = processor.GetCsvHistory(dataList);
            byte[] fileContents = Encoding.UTF8.GetBytes(csv);
            return File(fileContents, GetFileTypeByExtension(fileName), fileName);
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
