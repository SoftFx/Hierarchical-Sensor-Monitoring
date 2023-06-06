﻿using HSMCommon.Extensions;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies.Infrastructure;
using HSMServer.Core.MonitoringHistoryProcessor.Factory;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.Authentication.History;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SensorStatus = HSMSensorDataObjects.SensorStatus;
using TimeInterval = HSMServer.Model.TimeInterval;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : BaseController
    {
        private const int LatestHistoryCount = -100;
        internal const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;

        private static readonly JsonResult _emptyJsonResult = new(new EmptyResult());
        private static readonly EmptyResult _emptyResult = new();

        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IFolderManager _folderManager;
        private readonly TreeViewModel _treeViewModel;
        private readonly IUserManager _userManager;
        private readonly TelegramBot _telegramBot;


        public HomeController(ITreeValuesCache treeValuesCache, IFolderManager folderManager,
            TreeViewModel treeViewModel, IUserManager userManager, NotificationsCenter notifications)
        {
            _telegramBot = notifications.TelegramBot;
            _treeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
            _folderManager = folderManager;
            _userManager = userManager;
        }


        public IActionResult Index()
        {
            return View(_treeViewModel);
        }

        [HttpPost]
        public IActionResult SelectNode([FromQuery(Name = "Selected")] string selectedId)
        {
            BaseNodeViewModel viewModel = null;

            if (!string.IsNullOrEmpty(selectedId))
            {
                var decodedId = SensorPathHelper.DecodeGuid(selectedId);

                if (_folderManager.TryGetValue(decodedId, out var folder))
                    viewModel = folder;
                else if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                    viewModel = node;
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    viewModel = sensor;
            }

            return PartialView("_NodeDataPanel", viewModel);
        }

        [HttpPost]
        public IActionResult RefreshTree()
        {
            return PartialView("_Tree", _treeViewModel.GetUserTree(CurrentUser));
        }

        [HttpGet]
        public IActionResult ApplyFilter(UserFilterViewModel viewModel)
        {
            CurrentUser.TreeFilter = viewModel.ToFilter();
            _userManager.UpdateUser(CurrentUser);

            return Redirect("Index");
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
        public ActionResult RemoveNode([FromBody] string[] ids)
        {
            var model = new MultiActionToastViewModel();
            
            foreach (var id in ids)
            {
                var decodedId = SensorPathHelper.DecodeGuid(id);

                if (_folderManager.TryGetValue(decodedId, out var folder))
                {
                    model.AddRemoveFolderError(folder.Name);
                }
                else if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                {
                    if (!CurrentUser.IsManager(node.RootProduct.Id))
                    {
                        model.AddRoleError(node.Name, "remove");
                        continue;
                    }
                    
                    _treeValuesCache.RemoveProduct(node.Id);
                    model.AddItem(node);
                }
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                {
                    if (!CurrentUser.IsManager(sensor.RootProduct.Id))
                    {
                        model.AddRoleError(sensor.FullPath, "remove");
                        continue;
                    }
                   
                    _treeValuesCache.RemoveSensor(sensor.Id);
                    model.AddItem(sensor);
                }
            }

            return Json(model.BuildResponse("Removed"));
        }

        [HttpGet]
        public IActionResult GetEditAlertsPartialView() => PartialView("_AlertsModal", new EditAlertsViewModel());
        
        [HttpPost]
        public async Task<IActionResult> EditAlerts(EditAlertsViewModel model)
        {
            if (ModelState[nameof(model.SensorRestorePolicy)]?.Errors.Count > 0 || ModelState[nameof(model.ExpectedUpdateInterval)]?.Errors.Count > 0)
                return BadRequest(ModelState);
            
            model.Upload();
            
            var toastViewModel = new MultiActionToastViewModel();
            var isExpectedFromParent = model.ExpectedUpdateInterval?.TimeInterval is TimeInterval.FromParent;
            var isRestoreFromParent = model.SensorRestorePolicy?.TimeInterval is TimeInterval.FromParent;

            foreach (var id in model.SelectedNodes)
            {
                if (_folderManager.TryGetValue(id, out var folder))
                {
                    if (!CurrentUser.IsFolderManager(folder.Id))
                    {
                        toastViewModel.AddRoleError(folder.Name, "edit");
                        continue;
                    }
                    
                    var update = new FolderUpdate
                    {
                        Id = id,
                        RestoreInterval = !isRestoreFromParent ? model.SensorRestorePolicy?.ResaveCustomTicks(model.SensorRestorePolicy) : null,
                        ExpectedUpdateInterval = !isExpectedFromParent ? model.ExpectedUpdateInterval?.ResaveCustomTicks(model.ExpectedUpdateInterval) : null
                    };

                    if (isRestoreFromParent)
                        toastViewModel.AddCantChangeIntervalError(folder.Name, "Folder", "Sensitivity", TimeInterval.FromParent);

                    if (isExpectedFromParent)
                        toastViewModel.AddCantChangeIntervalError(folder.Name, "Folder", "Time to live", TimeInterval.FromParent);
                    
                    if (!isExpectedFromParent || !isRestoreFromParent)
                    {
                        toastViewModel.AddItem(folder);
                        await _folderManager.TryUpdate(update);
                    }
                }
                else if (_treeViewModel.Nodes.TryGetValue(id, out var product))
                {
                    if (!CurrentUser.IsManager(product.RootProduct.Id))
                    {
                        toastViewModel.AddRoleError(product.Name, "edit");
                        continue;
                    }
                    
                    var hasParent = product.Parent is not null || product.FolderId is not null;
                    var restoreUpdate = hasParent || !isRestoreFromParent;
                    var expectedUpdate = hasParent || !isExpectedFromParent;
                    
                    var isProduct = product.RootProduct?.Id == product.Id;
                    
                    var update = new ProductUpdate
                    {
                        Id = product.Id,
                        RestoreInterval = restoreUpdate ? model.SensorRestorePolicy?.ToModel((product.Parent as FolderModel)?.SensorRestorePolicy) : null,
                        ExpectedUpdateInterval = expectedUpdate ? model.ExpectedUpdateInterval?.ToModel((product.Parent as FolderModel)?.ExpectedUpdateInterval) : null
                    };
                    
                    if (!restoreUpdate)
                        toastViewModel.AddCantChangeIntervalError(product.Name, !isProduct ? "Node" : "Product", "Sensitivity", TimeInterval.FromParent);

                    if (!expectedUpdate)
                        toastViewModel.AddCantChangeIntervalError(product.Name, !isProduct ? "Node" : "Product", "Time to live", TimeInterval.FromParent);
                    
                    if (restoreUpdate || expectedUpdate)
                    {
                        toastViewModel.AddItem(product);
                        _treeValuesCache.UpdateProduct(update);
                    }
                }
                else if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
                {
                    if (!CurrentUser.IsManager(sensor.RootProduct.Id))
                    {
                        toastViewModel.AddRoleError(sensor.FullPath, "edit");
                        continue;
                    }
                    
                    var update = new SensorUpdate
                    {
                        Id = sensor.Id,
                        ExpectedUpdateInterval = model.ExpectedUpdateInterval?.ToModel(),
                        RestoreInterval = model.SensorRestorePolicy?.ToModel(),
                    };
                    
                    toastViewModel.AddItem(sensor);
                    _treeValuesCache.UpdateSensor(update);
                }
            }

            return Json(toastViewModel.BuildResponse("Edited"));
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
        public IActionResult IgnoreNotifications(string selectedId, NotificationsTarget target, bool isOffTimeModal, long? chat)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            IgnoreNotificationsViewModel viewModel = null;

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                viewModel = new IgnoreNotificationsViewModel(node, target, isOffTimeModal);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                viewModel = new IgnoreNotificationsViewModel(sensor, target, isOffTimeModal);
            else if (_folderManager.TryGetValue(decodedId, out var folder))
                viewModel = new IgnoreNotificationsViewModel(folder, target, isOffTimeModal);

            viewModel.Chat = chat;

            return PartialView("_IgnoreNotificationsModal", viewModel);
        }

        [HttpPost]
        public void EnableNotifications(string selectedId, NotificationsTarget target, long? chat) =>
            GetHandler(target)(SensorPathHelper.DecodeGuid(selectedId), (s, g) => s.Enable(g, chat));

        [HttpPost]
        public void IgnoreNotifications(IgnoreNotificationsViewModel model) =>
            GetHandler(model.NotificationsTarget)(SensorPathHelper.DecodeGuid(model.EncodedId), (s, g) => s.Ignore(g, model.EndOfIgnorePeriod, model.Chat));

        [HttpPost]
        public string GetNodePath([FromQuery] string selectedId, [FromQuery] bool isFullPath = false)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return isFullPath ? node.FullPath : node.Path;
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                return isFullPath ? sensor.FullPath : sensor.Path;

            return string.Empty;
        }

        [HttpPost]
        public void EnableGrafana(string selectedId) => ChangeSensorsIntegration(selectedId, Integration.Grafana);

        [HttpPost]
        public void DisableGrafana(string selectedId) => ChangeSensorsIntegration(selectedId, 0);

        private void ChangeSensorsIntegration(string selectedNode, Integration integration)
        {
            foreach (var sensorId in GetNodeSensors(SensorPathHelper.DecodeGuid(selectedNode)))
            {
                var update = new SensorUpdate()
                {
                    Id = sensorId,
                    Integration = integration,
                };

                _treeValuesCache.UpdateSensor(update);
            }
        }

        private Action<Guid, Action<ClientNotifications, Guid>> GetHandler(NotificationsTarget actionType) => actionType switch
        {
            NotificationsTarget.Groups => UpdateGroupNotificationSettings,
            NotificationsTarget.Accounts => UpdateUserNotificationSettings
        };

        private void UpdateUserNotificationSettings(Guid selectedNode, Action<ClientNotifications, Guid> updateSettings)
        {
            var user = _userManager[CurrentUser.Id];
            foreach (var sensorId in GetNodeSensors(selectedNode))
            {
                updateSettings?.Invoke(user.Notifications, sensorId);
            }

            _userManager.UpdateUser(user);
        }

        private void UpdateGroupNotificationSettings(Guid selectedNode, Action<ClientNotifications, Guid> updateSettings)
        {
            var updatedProducts = new HashSet<ProductNodeViewModel>();

            foreach (var sensorId in GetNodeSensors(selectedNode))
                if (_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor) && sensor.RootProduct != null)
                {
                    updateSettings?.Invoke(sensor.RootProduct.Notifications, sensorId);

                    updatedProducts.Add(sensor.RootProduct);
                }

            foreach (var product in updatedProducts)
                _treeViewModel.UpdateProductNotificationSettings(product);
        }

        private List<Guid> GetNodeSensors(Guid id) => _treeViewModel.GetAllNodeSensors(id);

        #region Update

        [HttpPost]
        public ActionResult UpdateSelectedNode([FromQuery(Name = "Selected")] string selectedId)
        {
            if (string.IsNullOrEmpty(selectedId))
                return Json(string.Empty);

            var decodedId = SensorPathHelper.DecodeGuid(selectedId);
            var updatedSensorsData = new List<object>();

            // TODO: implement update selected folder tree item
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

        [HttpGet]
        public ActionResult GetGeneralInfo(string selectedId)
        {
            var id = Guid.Parse(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(id, out var node))
                return PartialView("_GeneralInfo", new ProductInfoViewModel(node));

            if (_folderManager[id] is not null)
                return PartialView("_GeneralInfo", new FolderInfoViewModel(_folderManager[id]));

            if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
                return PartialView("_GeneralInfo", new SensorInfoViewModel(sensor));

            return _emptyResult;
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

            var enumerator =
                _treeValuesCache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(model.EncodedId), model.FromUtc, model.ToUtc, model.Count);
            var viewModel = await new HistoryValuesViewModel(model.EncodedId, model.Type, enumerator,
                GetLocalLastValue(model.EncodedId, model.FromUtc, model.ToUtc)).Initialize();

            _userManager[CurrentUser.Id].Pagination = viewModel;

            return GetHistoryTable(viewModel);
        }

        [HttpGet]
        public IActionResult GetPreviousPage()
        {
            return GetHistoryTable(_userManager[CurrentUser.Id].Pagination?.ToPreviousPage());
        }

        [HttpGet]
        public async Task<IActionResult> GetNextPage()
        {
            return GetHistoryTable(await (_userManager[CurrentUser.Id].Pagination?.ToNextPage()));
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

            var values = await GetSensorValues(encodedId, from.ToUtcKind(), to.ToUtcKind(), MaxHistoryCount);

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
        public async Task<IActionResult> GetFile([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            var (_, path) = GetSensorProductAndPath(encodedId);

            var value = await GetFileByReceivingTimeOrDefault(encodedId, dateTime);

            if (value is null)
                return _emptyResult;

            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(value.Value, fileName.GetContentType(), fileName);
        }

        [HttpPost]
        public async Task<IActionResult> GetFileStream([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            var (_, path) = GetSensorProductAndPath(encodedId);

            var value = await GetFileByReceivingTimeOrDefault(encodedId, dateTime);

            if (value is null)
                return _emptyResult;

            var fileContentsStream = new MemoryStream(value.Value);
            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(fileContentsStream, fileName.GetContentType(), fileName);
        }

        [HttpGet]
        public ActionResult<FileValue> GetFileInfo([FromQuery(Name = "Selected")] string encodedId)
        {
            var value = GetFileSensorValue(encodedId);

            return value is null ? _emptyResult : value;
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentFilesView([FromQuery] string fileId)
        {
            var viewModel = await GetFileHistory(fileId);
            _userManager[CurrentUser.Id].Pagination = viewModel;

            return GetFileTable(viewModel);
        }

        private PartialViewResult GetFileTable(HistoryValuesViewModel viewModel) =>
            PartialView("_FileAccordions", viewModel);

        private FileValue GetFileSensorValue(string encodedId) =>
            _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(encodedId)).LastValue as FileValue;

        private async Task<FileValue> GetFileByReceivingTimeOrDefault(string encodedId, long ticks = default) =>
            (ticks == default ? GetFileSensorValue(encodedId) : (await GetFileHistory(encodedId)).Pages[0].Cast<FileValue>().FirstOrDefault(file => file.ReceivingTime.Ticks == ticks)).DecompressContent();

        private Task<HistoryValuesViewModel> GetFileHistory(string encodedId)
        {
            var enumerator = _treeValuesCache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), DateTime.MinValue, DateTime.MaxValue, -20);
            return new HistoryValuesViewModel(encodedId, 6, enumerator, GetLocalLastValue(encodedId, DateTime.MinValue, DateTime.MaxValue)).Initialize();
        }

        #endregion

        #region Sensor info

        [HttpPost]
        public bool IsMetaInfoValid(NodeInfoBaseViewModel _) => ModelState.IsValid;

        [HttpGet]
        public IActionResult GetSensorInfo([FromQuery(Name = "Id")] string encodedId)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var sensor))
                return _emptyResult;

            return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));
        }

        [HttpPost]
        public IActionResult UpdateSensorInfo(SensorInfoViewModel newModel)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(newModel.EncodedId), out var sensor))
                return _emptyResult;

            if (!ModelState.IsValid)
                return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));

            var update = new SensorUpdate
            {
                Id = sensor.Id,
                Description = newModel.Description ?? string.Empty,
                ExpectedUpdateInterval = newModel.ExpectedUpdateInterval.ToModel(),
                RestoreInterval = newModel.SensorRestorePolicy.ToModel(),
                DataPolicies = newModel.DataAlerts?[sensor.Type].Select(a => a.ToUpdate()).ToList() ?? new(),
            };

            _treeValuesCache.UpdateSensor(update);

            return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));
        }


        public IActionResult AddDataPolicy(SensorType type, Guid sensorId)
        {
            DataAlertViewModelBase viewModel = type switch
            {
                SensorType.Integer => new SingleDataAlertViewModel<IntegerValue, int>(sensorId),
                SensorType.Double => new SingleDataAlertViewModel<DoubleValue, double>(sensorId),
                SensorType.IntegerBar => new BarDataAlertViewModel<IntegerBarValue, int>(sensorId),
                SensorType.DoubleBar => new BarDataAlertViewModel<DoubleBarValue, double>(sensorId),
                _ => null,
            };

            return PartialView("_DataAlert", viewModel);
        }

        [HttpPost]
        public void SendTestMessage(DataAlertViewModel alert)
        {
            if (!_treeViewModel.Sensors.TryGetValue(alert.EntityId, out var sensor) ||
                !_treeViewModel.Nodes.TryGetValue(sensor.RootProduct.Id, out var product))
                return;

            var template = CommentBuilder.GetTemplateString(alert.Comment);
            var comment = string.Format(template, product.Name, sensor.Path, sensor.Name,
                alert.Operation.GetDisplayName(), alert.Value, SensorStatus.Ok, DateTime.UtcNow, "value comment", 0, 0, 0, 0, 0);
            var testMessage = $"↕️ [{product.Name}]{sensor.Path} = {comment}";

            var notifications = product.Notifications;
            foreach (var (chat, _) in notifications.Telegram.Chats)
                if (notifications.IsSensorEnabled(sensor.Id) && !notifications.IsSensorIgnored(sensor.Id, chat))
                    _telegramBot.SendTestMessage(chat, testMessage);
        }


        [HttpGet]
        public IActionResult GetSensorEditModal(Guid sensorId)
        {
            _treeViewModel.Sensors.TryGetValue(sensorId, out var sensorNodeViewModel);
            var isAccessKeyExist = GetKeyOrDefaultWithPermissions(sensorNodeViewModel?.RootProduct.Id ?? Guid.Empty, KeyPermissions.CanSendSensorData) is not null;

            if (!isAccessKeyExist)
                ModelState.AddModelError(nameof(EditSensorStatusViewModal.RootProductId), EditSensorStatusViewModal.AccessKeyValidationErrorMessage);

            return PartialView("_EditSensorStatusModal", new EditSensorStatusViewModal(sensorNodeViewModel, isAccessKeyExist));
        }

        [HttpPost]
        public IActionResult UpdateSensorStatus(EditSensorStatusViewModal modal)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var key = GetKeyOrDefaultWithPermissions(modal.RootProductId, KeyPermissions.CanSendSensorData)?.Id;

            if (key is null)
            {
                ModelState.AddModelError(nameof(EditSensorStatusViewModal.RootProductId), EditSensorStatusViewModal.AccessKeyValidationErrorMessage);
                return BadRequest(ModelState);
            }

            var sensor = _treeValuesCache.GetSensor(modal.SensorId);
            var comment = $"User: {CurrentUser.Name}. Reason: {modal.Reason}";

            var sensorValue = ApiConverters.CreateNewSensorValue(sensor.Type);

            if (sensorValue is null)
                return BadRequest();

            sensorValue.Comment = comment;
            sensorValue.Path = sensor.Path;
            sensorValue.Status = (SensorStatus)modal.NewStatus;

            return Ok(new
            {
                Sensor = sensorValue,
                Key = key
            });
        }

        #endregion

        [HttpGet]
        public IActionResult GetProductInfo([FromQuery(Name = "Id")] string encodedId)
        {
            if (!_treeViewModel.Nodes.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var product))
                return _emptyResult;

            return PartialView("_MetaInfo", new ProductInfoViewModel(product));
        }

        [HttpPost]
        public IActionResult UpdateProductInfo(ProductInfoViewModel newModel)
        {
            if (!_treeViewModel.Nodes.TryGetValue(SensorPathHelper.DecodeGuid(newModel.EncodedId), out var product))
                return _emptyResult;

            if (!ModelState.IsValid)
                return PartialView("_MetaInfo", new ProductInfoViewModel(product));

            var update = new ProductUpdate
            {
                Id = product.Id,
                ExpectedUpdateInterval = newModel.ExpectedUpdateInterval.ToModel((product.Parent as FolderModel)?.ExpectedUpdateInterval),
                RestoreInterval = newModel.SensorRestorePolicy.ToModel((product.Parent as FolderModel)?.SensorRestorePolicy),
                Description = newModel.Description ?? string.Empty
            };

            _treeValuesCache.UpdateProduct(update);

            return PartialView("_MetaInfo", new ProductInfoViewModel(product.RecalculateCharacteristics()));
        }

        [HttpGet]
        public IActionResult GetFolderInfo(string id)
        {
            return _folderManager.TryGetValue(SensorPathHelper.DecodeGuid(id), out var folder)
                ? PartialView("_MetaInfo", new FolderInfoViewModel(folder))
                : _emptyResult;
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFolderInfo(FolderInfoViewModel newModel)
        {
            if (!ModelState.IsValid)
                return PartialView("_MetaInfo", new FolderInfoViewModel(_folderManager[Guid.Parse(newModel.EncodedId)]));

            var update = new FolderUpdate
            {
                Id = SensorPathHelper.DecodeGuid(newModel.EncodedId),
                Description = newModel.Description ?? string.Empty,
                ExpectedUpdateInterval = newModel.ExpectedUpdateInterval.ResaveCustomTicks(newModel.ExpectedUpdateInterval),
                RestoreInterval = newModel.SensorRestorePolicy.ResaveCustomTicks(newModel.SensorRestorePolicy),
            };

            return await _folderManager.TryUpdate(update)
                ? PartialView("_MetaInfo", new FolderInfoViewModel(_folderManager[update.Id]))
                : _emptyResult;
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

        private AccessKeyModel GetKeyOrDefaultWithPermissions(Guid productId, KeyPermissions permissions) =>
            _treeValuesCache.GetProduct(productId).AccessKeys.Values.FirstOrDefault(x => x.IsValid(permissions, out _));
    }
}