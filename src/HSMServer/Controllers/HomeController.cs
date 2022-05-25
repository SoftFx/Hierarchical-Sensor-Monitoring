using HSMSensorDataObjects;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.MonitoringHistoryProcessor;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;
using HSMServer.Helpers;
using HSMServer.HtmlHelpers;
using HSMServer.Model;
using HSMServer.Model.TreeViewModels;
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
        private readonly IHistoryProcessorFactory _historyProcessorFactory;
        private readonly TreeViewModel _treeViewModel;


        public HomeController(
            ISensorsInterface sensorsInterface,
            IHistoryProcessorFactory factory,
            TreeViewModel treeViewModel)
        {
            _sensorsInterface = sensorsInterface;
            _historyProcessorFactory = factory;
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index()
        {
            _treeViewModel.UpdateNodesCharacteristics(HttpContext.User as User);

            return View(_treeViewModel);
        }

        [HttpPost]
        public IActionResult SelectNode([FromQuery(Name = "Selected")] string selectedId)
        {
            if (string.IsNullOrEmpty(selectedId))
                return PartialView("_TreeNodeSensors", null);

            var decodedId = SensorPathHelper.Decode(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return PartialView("_TreeNodeSensors", node);
            else if (_treeViewModel.Sensors.TryGetValue(Guid.Parse(decodedId), out var sensor))
                return PartialView("_TreeNodeSensors", sensor);

            return PartialView("_TreeNodeSensors", null);
        }

        [HttpPost]
        public IActionResult RefreshTree()
        {
            _treeViewModel.UpdateNodesCharacteristics(HttpContext.User as User);

            return PartialView("_Tree", _treeViewModel);
        }

        [HttpPost]
        public void RemoveNode([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.Decode(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                _sensorsInterface.RemoveSensorsData(node.Id);
            else if (_treeViewModel.Sensors.TryGetValue(Guid.Parse(decodedId), out var sensor))
                _sensorsInterface.RemoveSensorData(sensor.Id);
        }

        #region Update

        [HttpPost]
        public ActionResult UpdateSelectedNode([FromQuery(Name = "Selected")] string selectedId)
        {
            if (string.IsNullOrEmpty(selectedId))
                return Json(string.Empty);

            var decodedId = SensorPathHelper.Decode(selectedId);
            var updatedSensorsData = new List<UpdatedSensorDataViewModel>();

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
            {
                foreach (var (_, sensor) in node.Sensors)
                    updatedSensorsData.Add(new UpdatedSensorDataViewModel(sensor));
            }
            else if (_treeViewModel.Sensors.TryGetValue(Guid.Parse(decodedId), out var sensor))
                updatedSensorsData.Add(new UpdatedSensorDataViewModel(sensor));

            return Json(updatedSensorsData);
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
            ParseProductAndPath(selectedSensor, out var product, out var path);

            var (content, extension) = _sensorsInterface.GetFileSensorValueData(product, path);

            var fileName = $"{path.Replace('/', '_')}.{extension}";

            return File(content, GetFileTypeByExtension(fileName), fileName);
        }

        [HttpPost]
        public IActionResult GetFileStream([FromQuery(Name = "Selected")] string selectedSensor)
        {
            ParseProductAndPath(selectedSensor, out var product, out var path);

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
        public HtmlString GetSensorInfo([FromQuery(Name = "Id")] string encodedId)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var sensor))
                return new HtmlString(string.Empty);

            return ViewHelper.CreateSensorInfoTable(new SensorInfoViewModel(sensor));
        }

        [HttpPost]
        public void UpdateSensorInfo([FromBody] UpdateSensorInfoViewModel updateModel)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(updateModel.EncodedId), out var sensor))
                return;

            var viewModel = new SensorInfoViewModel(sensor);
            viewModel.Update(updateModel);

            _sensorsInterface.UpdateSensor(
                new SensorUpdate
                {
                    Id = sensor.Id,
                    Description = viewModel.Description,
                    ExpectedUpdateInterval = viewModel.ExpectedUpdateInterval,
                    Unit = viewModel.Unit
                });
        }

        #endregion

        private void ParseProductAndPath(string encodedId, out string product, out string path)
        {
            var decodedId = SensorPathHelper.DecodeGuid(encodedId);

            _treeViewModel.Sensors.TryGetValue(decodedId, out var sensor);

            path = sensor?.Path;
            product = sensor?.Product;
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
    }
}
