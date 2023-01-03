﻿using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.Authentication.History;
using HSMServer.Model.TreeViewModels;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        private const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;

        private static readonly JsonResult _emptyJsonResult = new(new EmptyResult());
        private static readonly EmptyResult _emptyResult = new();

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
            _treeViewModel.RecalculateNodesCharacteristics();

            return View(_treeViewModel);
        }

        [HttpPost]
        public IActionResult SelectNode([FromQuery(Name = "Selected")] string selectedId)
        {
            NodeViewModel viewModel = null;

            if (!string.IsNullOrEmpty(selectedId))
            {
                var decodedId = SensorPathHelper.DecodeGuid(selectedId);

                if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                    viewModel = node;
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    viewModel = sensor;
            }

            return PartialView("_NodeDataPanel", viewModel);
        }

        [HttpPost]
        public IActionResult RefreshTree()
        {
            _treeViewModel.RecalculateNodesCharacteristics();

            return PartialView("_Tree", _treeViewModel.GetUserTree(HttpContext.User as User));
        }

        [HttpPost]
        public IActionResult ApplyFilter(UserFilterViewModel viewModel)
        {
            var user = HttpContext.User as User;
            user.TreeFilter = viewModel.ToFilter();
            _userManager.UpdateUser(user);

            return View("Index", _treeViewModel);
        }

        [HttpPost]
        public void ChangeSensorState([FromQuery(Name = "Selected")] string selectedId, [FromQuery(Name = "Block")] bool isBlocked)
        {
            var sensorUpdate = new SensorUpdate()
            {
                Id = SensorPathHelper.DecodeGuid(selectedId),
                State = isBlocked ? SensorState.Blocked : SensorState.Available,
            };

            _treeValuesCache.UpdateSensor(sensorUpdate);
        }

        [HttpPost]
        public void RemoveNode([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                _treeValuesCache.RemoveSensorsData(node.Id);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                _treeValuesCache.RemoveSensorData(sensor.Id);
        }

        [HttpPost]
        public void EnableNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            void EnableSensors(UserNotificationSettings settings, Guid sensorId) =>
                settings.EnabledSensors.Add(sensorId);

            UpdateUserNotificationSettings(selectedId, EnableSensors);
        }

        [HttpPost]
        public void DisableNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            void DisableSensors(UserNotificationSettings settings, Guid sensorId)
            {
                settings.EnabledSensors.Remove(sensorId);
                settings.IgnoredSensors.TryRemove(sensorId, out _);
            }

            UpdateUserNotificationSettings(selectedId, DisableSensors);
        }

        [HttpGet]
        public IActionResult IgnoreNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);
            IgnoreNotificationsViewModel viewModel = null;

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                viewModel = new IgnoreNotificationsViewModel(node);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                viewModel = new IgnoreNotificationsViewModel(sensor);

            return PartialView("_IgnoreNotificationsModal", viewModel);
        }

        [HttpPost]
        public void IgnoreNotifications(IgnoreNotificationsViewModel model)
        {
            void IgnoreSensors(UserNotificationSettings settings, Guid sensorId)
            {
                if (settings.IsSensorEnabled(sensorId))
                    settings.IgnoredSensors.TryAdd(sensorId, model.EndOfIgnorePeriod);
            }

            UpdateUserNotificationSettings(model.EncodedId, IgnoreSensors);
        }

        [HttpPost]
        public void RemoveIgnoringNotifications([FromQuery(Name = "Selected")] string selectedId)
        {
            void RemoveIgnoredSensors(UserNotificationSettings settings, Guid sensorId) =>
                settings.IgnoredSensors.TryRemove(sensorId, out _);

            UpdateUserNotificationSettings(selectedId, RemoveIgnoredSensors);
        }

        [HttpPost]
        public string GetPath([FromQuery(Name = "Selected")] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return node.Path;
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                return sensor.Path;

            return string.Empty;
        }

        private void UpdateUserNotificationSettings(string selectedNode, Action<UserNotificationSettings, Guid> updateSettings)
        {
            var sensors = GetNodeSensors(selectedNode);
            var user = _userManager.GetCopyUser((HttpContext.User as User).Id);

            foreach (var sensorId in sensors)
                updateSettings.Invoke(user.Notifications, sensorId);

            _userManager.UpdateUser(user);
        }

        private List<Guid> GetNodeSensors(string encodedId) =>
            _treeViewModel.GetNodeAllSensors(SensorPathHelper.DecodeGuid(encodedId));

        #region Update

        [HttpPost]
        public ActionResult UpdateSelectedNode([FromQuery(Name = "Selected")] string selectedId)
        {
            if (string.IsNullOrEmpty(selectedId))
                return Json(string.Empty);

            var decodedId = SensorPathHelper.DecodeGuid(selectedId);
            var updatedSensorsData = new List<object>();

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
            {
                foreach (var (_, childNode) in node.Nodes)
                    updatedSensorsData.Add(new UpdatedNodeDataViewModel(childNode));

                foreach (var (_, sensor) in node.Sensors)
                    updatedSensorsData.Add(new UpdatedSensorDataViewModel(sensor));
            }
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                updatedSensorsData.Add(new UpdatedSensorDataViewModel(sensor));

            return Json(updatedSensorsData);
        }

        #endregion

        #region SensorsHistory

        [HttpPost]
        public async Task<IActionResult> History([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return null;

            var enumerator = _treeValuesCache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(model.EncodedId),
                model.From.ToUniversalTime(), model.To.ToUniversalTime(), MaxHistoryCount);

            var viewModel = await new HistoryValuesViewModel(model.EncodedId, model.Type, enumerator, GetLocalLastValue(model.EncodedId)).Initialize();

            _userManager.GetUser((HttpContext.User as User).Id).Pagination = viewModel;

            return GetHistoryTable(viewModel);
        }

        [HttpPost]
        public Task<IActionResult> HistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type)
        {
            return History(new GetSensorHistoryModel()
            {
                EncodedId = encodedId,
                Type = type
            });
        }

        [HttpGet]
        public IActionResult GetPreviousPage()
        {
            return GetHistoryTable(_userManager.GetUser((HttpContext.User as User).Id).Pagination?.ToPreviousPage());
        }

        [HttpGet]
        public async Task<IActionResult> GetNextPage()
        {
            return GetHistoryTable(await (_userManager.GetUser((HttpContext.User as User).Id).Pagination?.ToNextPage()));
        }

        [HttpPost]
        public async Task<JsonResult> RawHistory([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyJsonResult;

            var values = await GetSensorValues(model.EncodedId, model.From, model.To);

            var localValue = GetLocalLastValue(model.EncodedId);
            if (localValue is not null)
                values.Add(localValue);

            return new(HistoryProcessorFactory.BuildProcessor(model.Type).ProcessingAndCompression(values).Select(v => (object)v));
        }

        [HttpPost]
        public async Task<JsonResult> RawHistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type)
        {
            var values = await GetAllSensorValues(encodedId);

            return new(values.Select(v => (object)v));
        }


        public async Task<FileResult> ExportHistory([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type,
            [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            DateTime fromUTC = from.ToUniversalTime();
            DateTime toUTC = to.ToUniversalTime();

            var (productName, path) = GetSensorProductAndPath(encodedId);
            string fileName = $"{productName}_{path.Replace('/', '_')}_from_{fromUTC:s}_to{toUTC:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            var values = await GetSensorValues(encodedId, fromUTC, toUTC);

            return GetExportHistory(values, type, fileName);
        }

        public async Task<FileResult> ExportHistoryAll([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type)
        {
            var (productName, path) = GetSensorProductAndPath(encodedId);
            string fileName = $"{productName}_{path.Replace('/', '_')}_all_{DateTime.Now.ToUniversalTime():s}.csv";

            var values = await GetAllSensorValues(encodedId);

            return GetExportHistory(values, type, fileName);
        }


        private PartialViewResult GetHistoryTable(HistoryValuesViewModel viewModel) =>
            PartialView("_SensorValuesTable", viewModel);

        private FileResult GetExportHistory(List<BaseValue> values, int type, string fileName)
        {
            var csv = HistoryProcessorFactory.BuildProcessor(type).GetCsvHistory(values);
            var content = Encoding.UTF8.GetBytes(csv);

            return File(content, fileName.GetContentType(), fileName);
        }

        private ValueTask<List<BaseValue>> GetSensorValues(string encodedId, DateTime from, DateTime to)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new(new List<BaseValue>());

            return _treeValuesCache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), from.ToUniversalTime(), to.ToUniversalTime(), MaxHistoryCount).Flatten();
        }

        private async Task<List<BaseValue>> GetAllSensorValues(string encodedId)
        {
            var from = DateTime.MinValue;
            var to = DateTime.MaxValue;

            return await GetSensorValues(encodedId, from, to);
        }

        #endregion

        #region File

        [HttpGet]
        public IActionResult GetFile([FromQuery(Name = "Selected")] string encodedId)
        {
            var value = GetFileSensorValue(encodedId);
            if (value == null)
                return _emptyResult;

            var (_, path) = GetSensorProductAndPath(encodedId);

            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(value.Value, fileName.GetContentType(), fileName);
        }

        [HttpPost]
        public IActionResult GetFileStream([FromQuery(Name = "Selected")] string encodedId)
        {
            var value = GetFileSensorValue(encodedId);
            if (value == null)
                return _emptyResult;

            var (_, path) = GetSensorProductAndPath(encodedId);

            var fileContentsStream = new MemoryStream(value.Value);
            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(fileContentsStream, fileName.GetContentType(), fileName);
        }

        private FileValue GetFileSensorValue(string encodedId) =>
            _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(encodedId)).LastValue as FileValue;

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
                Description = updatedModel.Description ?? string.Empty,
                Unit = updatedModel.Unit ?? string.Empty,
                ExpectedUpdateInterval = updatedModel.ExpectedUpdateInterval.ToModel(),
            };

            _treeValuesCache.UpdateSensor(sensorUpdate);

            return PartialView("_SensorMetaInfo", new SensorInfoViewModel(sensor).Update(sensorUpdate));
        }

        #endregion

        [HttpGet]
        public IActionResult GetProductInfo([FromQuery(Name = "Id")] string encodedId)
        {
            if (!_treeViewModel.Nodes.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var product))
                return _emptyResult;

            return PartialView("_ProductMetaInfo", new ProductInfoViewModel(product));
        }

        [HttpPost]
        public IActionResult UpdateProductInfo(ProductInfoViewModel updatedModel)
        {
            if (!_treeViewModel.Nodes.TryGetValue(SensorPathHelper.DecodeGuid(updatedModel.EncodedId), out var product))
                return _emptyResult;

            var productUpdate = new ProductUpdate
            {
                Id = product.Id,
                ExpectedUpdateInterval = updatedModel.ExpectedUpdateInterval.ToModel(),
            };

            _treeValuesCache.UpdateProduct(productUpdate);

            return PartialView("_ProductMetaInfo", new ProductInfoViewModel(product).Update(productUpdate));
        }

        private (string productName, string path) GetSensorProductAndPath(string encodedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(encodedId);

            _treeViewModel.Sensors.TryGetValue(decodedId, out var sensor);

            return (sensor?.Product, sensor?.Path);
        }

        private BarBaseValue GetLocalLastValue(string encodedId)
        {
            var sensor = _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(encodedId));

            return sensor is IBarSensor barSensor ? barSensor.LocalLastValue : null;
        }
    }
}
