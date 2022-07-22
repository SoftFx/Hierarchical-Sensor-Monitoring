using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringHistoryProcessor;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
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
using System.Linq;
using System.Text;


namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        private const int DEFAULT_REQUESTED_COUNT = 40;

        private readonly ITreeValuesCache _treeValuesCache;
        private readonly TreeViewModel _treeViewModel;


        public HomeController(ITreeValuesCache treeValuesCache, TreeViewModel treeViewModel)
        {
            _treeValuesCache = treeValuesCache;
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
                _treeValuesCache.RemoveSensorsData(node.Id);
            else if (_treeViewModel.Sensors.TryGetValue(Guid.Parse(decodedId), out var sensor))
                _treeValuesCache.RemoveSensorData(sensor.Id);
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
            var values = GetSensorValues(model.EncodedId, DEFAULT_REQUESTED_COUNT);

            return new HtmlString(TableHelper.CreateHistoryTable(GetProcessedValues(values, model.Type), model.Type, model.EncodedId));
        }

        [HttpPost]
        public HtmlString History([FromBody] GetSensorHistoryModel model)
        {
            var values = GetSensorValues(model.EncodedId, model.From, model.To);

            return new HtmlString(TableHelper.CreateHistoryTable(GetProcessedValues(values, model.Type), model.Type, model.EncodedId));
        }

        [HttpPost]
        public HtmlString HistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type) =>
            new(TableHelper.CreateHistoryTable(GetAllSensorValues(encodedId), type, encodedId));


        [HttpPost]
        public JsonResult RawHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            var values = GetSensorValues(model.EncodedId, DEFAULT_REQUESTED_COUNT);

            return GetJsonProcessedValues(values, model.Type);
        }

        [HttpPost]
        public JsonResult RawHistory([FromBody] GetSensorHistoryModel model)
        {
            var values = GetSensorValues(model.EncodedId, model.From, model.To);

            return GetJsonProcessedValues(values, model.Type);
        }

        [HttpPost]
        public JsonResult RawHistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type) =>
            new(GetAllSensorValues(encodedId).Select(v => (object)v));


        public FileResult ExportHistory([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type,
            [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            DateTime fromUTC = from.ToUniversalTime();
            DateTime toUTC = to.ToUniversalTime();

            var (productName, path) = GetSensorProductAndPath(encodedId);
            string fileName = $"{productName}_{path.Replace('/', '_')}_from_{fromUTC:s}_to{toUTC:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            var values = GetSensorValues(encodedId, fromUTC, toUTC);

            return GetExportHistory(values, type, fileName);
        }

        public FileResult ExportHistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type)
        {
            var (productName, path) = GetSensorProductAndPath(encodedId);
            string fileName = $"{productName}_{path.Replace('/', '_')}_all_{DateTime.Now.ToUniversalTime():s}.csv";

            return GetExportHistory(GetAllSensorValues(encodedId), type, fileName);
        }

        private FileResult GetExportHistory(List<BaseValue> values, int type, string fileName)
        {
            var csv = HistoryProcessorFactory.BuildProcessor(type).GetCsvHistory(values);
            var content = Encoding.UTF8.GetBytes(csv);

            return File(content, GetFileTypeByExtension(fileName), fileName);
        }


        private List<BaseValue> GetSensorValues(string encodedId, int count) =>
            _treeValuesCache.GetSensorValues(SensorPathHelper.DecodeGuid(encodedId), count);

        private List<BaseValue> GetSensorValues(string encodedId, DateTime from, DateTime to) =>
            _treeValuesCache.GetSensorValues(SensorPathHelper.DecodeGuid(encodedId), from.ToUniversalTime(), to.ToUniversalTime());

        private List<BaseValue> GetAllSensorValues(string encodedId)
        {
            var from = DateTime.MinValue;
            var to = DateTime.MaxValue;

            return _treeValuesCache.GetSensorValues(SensorPathHelper.DecodeGuid(encodedId), from, to);
        }

        private static List<BaseValue> GetProcessedValues(List<BaseValue> values, int type) =>
            HistoryProcessorFactory.BuildProcessor(type).ProcessHistory(values);

        private static JsonResult GetJsonProcessedValues(List<BaseValue> values, int type) =>
            new(GetProcessedValues(values, type).Select(v => (object)v));

        #endregion

        #region File

        [HttpGet]
        public FileResult GetFile([FromQuery(Name = "Selected")] string encodedId)
        {
            var value = GetFileSensorValue(encodedId);
            var (_, path) = GetSensorProductAndPath(encodedId);

            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(value.Value, GetFileTypeByExtension(fileName), fileName);
        }

        [HttpPost]
        public IActionResult GetFileStream([FromQuery(Name = "Selected")] string encodedId)
        {
            var value = GetFileSensorValue(encodedId);
            var (_, path) = GetSensorProductAndPath(encodedId);

            var fileContentsStream = new MemoryStream(value.Value);
            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(fileContentsStream, GetFileTypeByExtension(fileName), fileName);
        }

        private FileValue GetFileSensorValue(string encodedId) =>
            _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(encodedId)).LastValue as FileValue;

        private static string GetFileTypeByExtension(string fileName)
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

            TimeSpan.TryParse(viewModel.ExpectedUpdateInterval, out var interval);

            _treeValuesCache.UpdateSensor(
                new SensorUpdate
                {
                    Id = sensor.Id,
                    Description = viewModel.Description,
                    ExpectedUpdateInterval = interval,
                    Unit = viewModel.Unit
                });
        }

        #endregion

        private (string productName, string path) GetSensorProductAndPath(string encodedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(encodedId);

            _treeViewModel.Sensors.TryGetValue(decodedId, out var sensor);

            return (sensor?.Product, sensor?.Path);
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
