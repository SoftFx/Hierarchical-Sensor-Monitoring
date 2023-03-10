using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.Authentication.History;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        private const int LatestHistoryCount = -100;
        internal const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;

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
        public void SetMutedStateToSensorFromModal(IgnoreNotificationsViewModel model)
        {
            var decodedId = SensorPathHelper.DecodeGuid(model.EncodedId);
            var newMutingPeriod = model.EndOfIgnorePeriod;
            
            if (_treeViewModel.Nodes.TryGetValue(decodedId, out _))
            {
                foreach (var sensorId in GetNodeSensors(decodedId))
                    _treeValuesCache.UpdateMutedSensorState(sensorId, newMutingPeriod);
            }
            else
            {
                if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    _treeValuesCache.UpdateMutedSensorState(sensor.Id, newMutingPeriod);
            }
            
            UpdateUserNotificationSettings(decodedId, (s, g) => s.Ignore(g, model.EndOfIgnorePeriod));
            UpdateGroupNotificationSettings(decodedId, (s, g) => s.Ignore(g, model.EndOfIgnorePeriod));
        }

        [HttpPost]
        public void RemoveMutedStateToSensor([FromQuery] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out _))
            {
                foreach (var sensorId in GetNodeSensors(decodedId))
                    _treeValuesCache.UpdateMutedSensorState(sensorId);
            }
            else
            {
                if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    _treeValuesCache.UpdateMutedSensorState(sensor.Id);
            }

            UpdateUserNotificationSettings(decodedId, (s, g) => s.RemoveIgnore(g));
            UpdateGroupNotificationSettings(decodedId, (s, g) => s.RemoveIgnore(g));
        }

        [HttpPost]
        public void RemoveNode([FromQuery] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                _treeValuesCache.RemoveNode(node.Id);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                _treeValuesCache.RemoveSensor(sensor.Id);
        }

        [HttpPost]
        public void ClearHistoryNode([FromQuery] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                _treeValuesCache.ClearNodeHistory(node.Id);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                _treeValuesCache.ClearSensorHistory(sensor.Id);
        }

        [HttpGet]
        public IActionResult IgnoreNotifications([FromQuery] string selectedId, [FromQuery] NotificationsTarget target, [FromQuery] bool isOffTimeModal)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            IgnoreNotificationsViewModel viewModel = null;

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                viewModel = new IgnoreNotificationsViewModel(node, target, isOffTimeModal);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                viewModel = new IgnoreNotificationsViewModel(sensor, target, isOffTimeModal);

            return PartialView("_IgnoreNotificationsModal", viewModel);
        }

        [HttpPost]
        public void EnableNotifications([FromQuery] string selectedId, [FromQuery] NotificationsTarget target) =>
            GetHandler(target)(SensorPathHelper.DecodeGuid(selectedId), (s, g) => s.Enable(g));
        
        [HttpPost]
        public void IgnoreNotifications(IgnoreNotificationsViewModel model) =>
            GetHandler(model.NotificationsTarget)(SensorPathHelper.DecodeGuid(model.EncodedId), (s, g) =>
            {
                if (model.IgnorePeriod.TimeInterval == Model.TimeInterval.Forever)
                    s.Disable(g);
                else
                    s.Ignore(g, model.EndOfIgnorePeriod);
            });

        [HttpPost]
        public string GetNodePath([FromQuery] string selectedId, [FromQuery] bool isFullPath = false)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return isFullPath ? $"{node.RootProduct.Name}{node.Path}" : node.Path;
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                return isFullPath ? $"{sensor.RootProduct.Name}{sensor.Path}" : sensor.Path;

            return string.Empty;
        }

        private Action<Guid, Action<NotificationSettings, Guid>> GetHandler(NotificationsTarget actionType) => actionType switch
        {
            NotificationsTarget.Groups => UpdateGroupNotificationSettings,
            NotificationsTarget.Accounts => UpdateUserNotificationSettings
        };

        private void UpdateUserNotificationSettings(Guid selectedNode, Action<NotificationSettings, Guid> updateSettings)
        {
            var user = _userManager.GetUser((HttpContext.User as User).Id);
            foreach (var sensorId in GetNodeSensors(selectedNode))
            {
                updateSettings?.Invoke(user.Notifications, sensorId);
            }

            _userManager.UpdateUser(user);
        }

        private void UpdateGroupNotificationSettings(Guid selectedNode, Action<NotificationSettings, Guid> updateSettings)
        {
            ProductNodeViewModel rootProduct = null;
            if (_treeViewModel.Nodes.TryGetValue(selectedNode, out var node))
                rootProduct = node.RootProduct;
            else if (_treeViewModel.Sensors.TryGetValue(selectedNode, out var sensor))
                rootProduct = sensor.RootProduct;

            if (rootProduct is null)
                return;

            foreach (var sensorId in GetNodeSensors(selectedNode))
            {
                updateSettings?.Invoke(rootProduct.Notifications, sensorId);
            }
            
            _treeValuesCache.UpdateProduct(_treeValuesCache.GetProduct(rootProduct.Id));
        }

        private List<Guid> GetNodeSensors(Guid id) => _treeViewModel.GetNodeAllSensors(id);

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
        public Task<IActionResult> HistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return Task.FromResult(_emptyResult as IActionResult);

            return History(SpecifyLatestHistoryModel(model));
        }

        [HttpPost]
        public async Task<IActionResult> History([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyResult;

            var enumerator = _treeValuesCache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(model.EncodedId), model.FromUtc, model.ToUtc, model.Count);
            var viewModel = await new HistoryValuesViewModel(model.EncodedId, model.Type, enumerator, GetLocalLastValue(model.EncodedId, model.FromUtc, model.ToUtc)).Initialize();

            _userManager.GetUser((HttpContext.User as User).Id).Pagination = viewModel;

            return GetHistoryTable(viewModel);
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
        public Task<JsonResult> RawHistoryLatest([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return Task.FromResult(_emptyJsonResult);

            return RawHistory(SpecifyLatestHistoryModel(model));
        }

        [HttpPost]
        public async Task<JsonResult> RawHistory([FromBody] GetSensorHistoryModel model)
        {
            if (model == null)
                return _emptyJsonResult;

            var values = await GetSensorValues(model.EncodedId, model.FromUtc, model.ToUtc, model.Count);

            var localValue = GetLocalLastValue(model.EncodedId, model.FromUtc, model.ToUtc);
            if (localValue is not null)
                values.Add(localValue);

            return new(HistoryProcessorFactory.BuildProcessor(model.Type).ProcessingAndCompression(values, model.BarsCount).Select(v => (object)v));
        }


        public async Task<FileResult> ExportHistory([FromQuery(Name = "EncodedId")] string encodedId, [FromQuery(Name = "Type")] int type,
            [FromQuery(Name = "From")] DateTime from, [FromQuery(Name = "To")] DateTime to)
        {
            var (productName, path) = GetSensorProductAndPath(encodedId);
            string fileName = $"{productName}_{path.Replace('/', '_')}_from_{from:s}_to{to:s}.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            var values = await GetSensorValues(encodedId, from.ToUtc(), to.ToUtc(), MaxHistoryCount);

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

        private ValueTask<List<BaseValue>> GetSensorValues(string encodedId, DateTime from, DateTime to, int count)
        {
            if (string.IsNullOrEmpty(encodedId))
                return new(new List<BaseValue>());

            return _treeValuesCache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), from, to, count).Flatten();
        }

        private GetSensorHistoryModel SpecifyLatestHistoryModel(GetSensorHistoryModel model)
        {
            _treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(model.EncodedId), out var sensor);

            model.From = DateTime.MinValue;
            model.To = sensor?.LastValue?.ReceivingTime ?? DateTime.MinValue;
            model.Count = LatestHistoryCount;

            return model;
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

            return PartialView("_SensorMetaInfo", new SensorInfoViewModel(sensor));
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
                Description = updatedModel.Description
            };

            _treeValuesCache.UpdateProduct(productUpdate);

            return PartialView("_ProductMetaInfo", new ProductInfoViewModel(product));
        }

        private (string productName, string path) GetSensorProductAndPath(string encodedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(encodedId);

            _treeViewModel.Sensors.TryGetValue(decodedId, out var sensor);

            return (sensor?.RootProduct.Name, sensor?.Path);
        }

        private BarBaseValue GetLocalLastValue(string encodedId, DateTime from, DateTime to)
        {
            var sensor = _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(encodedId));

            var localValue = sensor is IBarSensor barSensor ? barSensor.LocalLastValue : null;

            return localValue?.ReceivingTime >= from && localValue?.ReceivingTime <= to ? localValue : null;
        }
    }
}
