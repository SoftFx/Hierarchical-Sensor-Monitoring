using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
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
using System.Collections.Concurrent;
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

        private static readonly JsonResult _emptyJsonResult = new(new EmptyResult());
        private static readonly EmptyResult _emptyResult = new EmptyResult();

        private readonly ITreeValuesCache _treeValuesCache;
        private readonly TreeViewModel _treeViewModel;
        private readonly IUserManager _userManager;


        public HomeController(ITreeValuesCache treeValuesCache, TreeViewModel treeViewModel, IUserManager userManager)
        {
            _treeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
            _userManager = userManager;
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
        public IActionResult ApplyFilter(UserFilterViewModel viewModel)
        {
            var user = HttpContext.User as User;
            user.TreeFilter = viewModel.ToFilter();
            _userManager.UpdateUser(user);

            _treeViewModel.UpdateNodesCharacteristics(HttpContext.User as User);
            return View("Index", _treeViewModel);
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

        [HttpPost]
        public void EnableNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            void EnableSensors(NotificationSettings settings, Guid sensorId) =>
                settings.EnabledSensors.Add(sensorId);

            UpdateUserEnabledSensors(selectedId, true, EnableSensors);
        }

        [HttpPost]
        public void DisableNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            void DisableSensors(NotificationSettings settings, Guid sensorId)
            {
                settings.EnabledSensors.Remove(sensorId);
                settings.IgnoredSensors.TryRemove(sensorId, out _);
            }

            UpdateUserEnabledSensors(selectedId, false, DisableSensors);
        }

        [HttpGet]
        public IActionResult IgnoreNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.Decode(selectedId);
            IgnoreNotificationsViewModel viewModel = null;

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                viewModel = new IgnoreNotificationsViewModel(node);
            else if (_treeViewModel.Sensors.TryGetValue(Guid.Parse(decodedId), out var sensor))
                viewModel = new IgnoreNotificationsViewModel(sensor);

            return PartialView("_IgnoreNotificationsModal", viewModel);
        }

        [HttpPost]
        public void IgnoreNotifications(IgnoreNotificationsViewModel model)
        {
            void IgnoreSensors(ConcurrentDictionary<Guid, DateTime> ignoredSensors, Guid sensorId) =>
                ignoredSensors.TryAdd(sensorId, model.EndOfIgnorePeriod);

            UpdateUserIgnoredSensors(model.EncodedId, IgnoreSensors);
        }

        [HttpPost]
        public void RemoveIgnoringNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            void RemoveIgnoredSensors(ConcurrentDictionary<Guid, DateTime> ignoredSensors, Guid sensorId) =>
                ignoredSensors.TryRemove(sensorId, out _);

            UpdateUserIgnoredSensors(selectedId, RemoveIgnoredSensors);
        }

        [HttpPost]
        public string GetPath([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.Decode(selectedId);

            //if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                //return node.Path;
            /*else*/ if (_treeViewModel.Sensors.TryGetValue(Guid.Parse(decodedId), out var sensor))
                return sensor.Path;

            return string.Empty;
        }

        private void UpdateUserEnabledSensors(string selectedNode, bool notificationsUpdatedStatus,
            Action<NotificationSettings, Guid> updateSettings)
        {
            var sensors = GetNodeSensors(selectedNode);
            var user = _userManager.GetCopyUser((HttpContext.User as User).Id);

            foreach (var sensorId in sensors)
            {
                updateSettings.Invoke(user.Notifications, sensorId);

                if (_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor))
                    sensor.UpdateNotificationsStatus(notificationsUpdatedStatus);
            }

            _userManager.UpdateUser(user);
        }

        private void UpdateUserIgnoredSensors(string selectedNode, Action<ConcurrentDictionary<Guid, DateTime>, Guid> updateAction)
        {
            var sensors = GetNodeSensors(selectedNode);
            var user = _userManager.GetCopyUser((HttpContext.User as User).Id);

            foreach (var sensorId in sensors)
                updateAction.Invoke(user.Notifications.IgnoredSensors, sensorId);

            _userManager.UpdateUser(user);
        }

        private List<Guid> GetNodeSensors(string encodedId) =>
            _treeViewModel.GetNodeAllSensors(SensorPathHelper.Decode(encodedId));

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
            if (model == null)
                return null;

            var values = GetSensorValues(model.EncodedId, DEFAULT_REQUESTED_COUNT);

            return new HtmlString(TableHelper.CreateHistoryTable(GetProcessedValues(values, model.Type), model.Type, model.EncodedId));
        }

        [HttpPost]
        public HtmlString History([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return null;

            var values = GetSensorValues(model.EncodedId, model.From, model.To);

            return new HtmlString(TableHelper.CreateHistoryTable(GetProcessedValues(values, model.Type), model.Type, model.EncodedId));
        }

        [HttpPost]
        public HtmlString HistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type) =>
            new(TableHelper.CreateHistoryTable(GetAllSensorValues(encodedId), type, encodedId));


        [HttpPost]
        public JsonResult RawHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyJsonResult;

            var values = GetSensorValues(model.EncodedId, DEFAULT_REQUESTED_COUNT);

            return GetJsonProcessedValues(values, model.Type);
        }

        [HttpPost]
        public JsonResult RawHistory([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyJsonResult;

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


        private List<BaseValue> GetSensorValues(string encodedId, int count)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new();

            return _treeValuesCache.GetSensorValues(SensorPathHelper.DecodeGuid(encodedId), count);
        }

        private List<BaseValue> GetSensorValues(string encodedId, DateTime from, DateTime to)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new();

            return _treeValuesCache.GetSensorValues(SensorPathHelper.DecodeGuid(encodedId), from.ToUniversalTime(), to.ToUniversalTime());
        }

        private List<BaseValue> GetAllSensorValues(string encodedId)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new();

            var from = DateTime.MinValue;
            var to = DateTime.MaxValue;

            var values = _treeValuesCache.GetSensorValues(SensorPathHelper.DecodeGuid(encodedId), from, to);

            return values.OrderBy(v => v.Time).ThenBy(v => v.ReceivingTime).ToList();
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
        public IActionResult GetSensorInfo([FromQuery(Name = "Id")] string encodedId)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var sensor))
                return _emptyResult;

            return PartialView("_SensorMetaInfo", new SensorInfoViewModel(sensor));
        }

        [HttpPost]
        public IActionResult UpdateSensorInfo(SensorInfoViewModel updatedModel)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(updatedModel.EncodedId), out var sensor))
                return _emptyResult;

            var sensorUpdate = new SensorUpdate
            {
                Id = sensor.Id,
                Description = updatedModel.Description,
                Unit = updatedModel.Unit,
                ExpectedUpdateInterval = updatedModel.ExpectedUpdateInterval.ToModel(),
            };

            _treeValuesCache.UpdateSensor(sensorUpdate);

            return PartialView("_SensorMetaInfo", new SensorInfoViewModel(sensor).Update(sensorUpdate));
        }

        #endregion

        private (string productName, string path) GetSensorProductAndPath(string encodedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(encodedId);

            _treeViewModel.Sensors.TryGetValue(decodedId, out var sensor);

            return (sensor?.Product, sensor?.Path);
        }
    }
}
