using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.TreeViewModels;
using HSMServer.Model.ViewModel;
using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TimeInterval = HSMServer.Model.TimeInterval;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : BaseController
    {
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IFolderManager _folderManager;
        private readonly TreeViewModel _treeViewModel;
        private readonly TelegramBot _telegramBot;
        private readonly IJournalService _journalService;


        public HomeController(ITreeValuesCache treeValuesCache, IFolderManager folderManager, TreeViewModel treeViewModel,
                              IUserManager userManager, NotificationsCenter notifications, IJournalService journalService) : base(userManager)
        {
            _telegramBot = notifications.TelegramBot;
            _treeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
            _folderManager = folderManager;
            _journalService = journalService;
        }

        public IActionResult Index()
        {
            return View(_treeViewModel);
        }

        [HttpPost]
        public async Task<PartialViewResult> SelectNode(string selectedId)
        {
            BaseNodeViewModel viewModel = null;

            if (!string.IsNullOrEmpty(selectedId))
            {
                var id = selectedId.ToGuid();

                if (_folderManager.TryGetValue(id, out var folder))
                {
                    viewModel = folder;
                    StoredUser.SelectedNode.ConnectFolder(folder);
                    CurrentUser.Tree.AddOpenedNode(id);
                }
                else if (_treeViewModel.Nodes.TryGetValue(id, out var node))
                {
                    viewModel = node;
                    StoredUser.SelectedNode.ConnectNode(node);
                    CurrentUser.Tree.AddOpenedNode(id);
                }
                else if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
                {
                    viewModel = sensor;
                    StoredUser.History.ConnectSensor(_treeValuesCache.GetSensor(id));
                }
            }

            await StoredUser.Journal.ConnectJournal(viewModel, _journalService);

            return PartialView("_NodeDataPanel", viewModel);
        }

        [HttpGet]
        public IActionResult GetNode(string id) =>
            _treeViewModel.Nodes.TryGetValue(id.ToGuid(), out var node)
                ? PartialView("_TreeNode", CurrentUser.Tree.LoadNode(node))
                : NotFound();

        [HttpPut]
        public void RemoveRenderingNode(Guid nodeId) => CurrentUser.Tree.RemoveOpenedNode(nodeId);

        [HttpGet]
        public IActionResult GetGrid(ChildrenPageRequest pageRequest)
        {
            var model = StoredUser.SelectedNode.GetNextPage(pageRequest);

            return model.IsPageValid ? PartialView("_GridAccordion", model) : _emptyResult;
        }

        [HttpGet]
        public IActionResult GetList(ChildrenPageRequest pageRequest)
        {
            var model = StoredUser.SelectedNode.GetNextPage(pageRequest);

            return model.IsPageValid ? PartialView("_ListAccordion", model) : _emptyResult;
        }

        [HttpGet]
        public IActionResult RefreshTree()
        {
            return PartialView("_Tree", CurrentUser.Tree.GetUserTree());
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
                    _treeValuesCache.UpdateMutedSensorState(sensorId, newMutingPeriod, CurrentUser.Name);
            }
            else
            {
                if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    _treeValuesCache.UpdateMutedSensorState(sensor.Id, newMutingPeriod, CurrentUser.Name);
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
                    _treeValuesCache.UpdateMutedSensorState(sensorId, initiator: CurrentUser.Name);
            }
            else
            {
                if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    _treeValuesCache.UpdateMutedSensorState(sensor.Id, initiator: CurrentUser.Name);
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

                    _treeValuesCache.RemoveSensor(sensor.Id, CurrentUser.Name);
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
            if (ModelState[nameof(model.ExpectedUpdateInterval)]?.Errors.Count > 0)
                return BadRequest(ModelState);

            model.Upload();

            var toastViewModel = new MultiActionToastViewModel();
            var isExpectedFromParent = model.ExpectedUpdateInterval?.TimeInterval is TimeInterval.FromParent;

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
                        TTL = !isExpectedFromParent ? model.ExpectedUpdateInterval : null,
                        Initiator = CurrentUser.Name
                    };

                    if (isExpectedFromParent)
                        toastViewModel.AddCantChangeIntervalError(folder.Name, "Folder", "Time to live", TimeInterval.FromParent);
                    else
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
                    var expectedUpdate = hasParent || !isExpectedFromParent;

                    var isProduct = product.RootProduct?.Id == product.Id;

                    var update = new ProductUpdate
                    {
                        Id = product.Id,
                        TTL = expectedUpdate ? model.ExpectedUpdateInterval?.ToModel(product.TTL) : null
                    };

                    if (!expectedUpdate)
                        toastViewModel.AddCantChangeIntervalError(product.Name, !isProduct ? "Node" : "Product", "Time to live", TimeInterval.FromParent);
                    else
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
                        TTL = model.ExpectedUpdateInterval?.ToModel(),
                        Initiator = CurrentUser.Name
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
            ClearHistoryRequest GetRequest(Guid id) => new(id, CurrentUser.Name);

            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                _treeValuesCache.ClearNodeHistory(GetRequest(node.Id));
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                _treeValuesCache.ClearSensorHistory(GetRequest(sensor.Id));
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
                    Initiator = CurrentUser.Name
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
            foreach (var sensorId in GetNodeSensors(selectedNode))
            {
                updateSettings?.Invoke(StoredUser.Notifications, sensorId);
            }

            _userManager.UpdateUser(StoredUser);
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
                updatedSensorsData.Add(new UpdatedSensorDataViewModel(sensor, CurrentUser));

            return Json(updatedSensorsData);
        }

        [HttpGet]
        public ActionResult GetGeneralInfo(string selectedId)
        {
            var id = selectedId.ToGuid();

            if (_treeViewModel.Nodes.TryGetValue(id, out var node))
                return PartialView("_GeneralInfo", new ProductInfoViewModel(node));

            if (_folderManager[id] is not null)
                return PartialView("_GeneralInfo", new FolderInfoViewModel(_folderManager[id]));

            if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
                return PartialView("_GeneralInfo", new SensorInfoViewModel(sensor));

            return _emptyResult;
        }

        [HttpGet]
        public ActionResult GetAlertIcons(string selectedId)
        {
            var id = selectedId.ToGuid();
            ConcurrentDictionary<string, int> icons = null;

            if (_treeViewModel.Nodes.TryGetValue(id, out var node))
                icons = node.AlertIcons;
            else if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
                icons = sensor.AlertIcons;

            return icons is not null ? PartialView("~/Views/Home/Alerts/_AlertIconsList.cshtml", icons) : _emptyResult;
        }

        #endregion

        #region File

        [HttpGet]
        public async Task<IActionResult> GetFile([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            var path = GetSensorPath(encodedId);

            var value = await GetFileByReceivingTimeOrDefault(encodedId, dateTime);

            if (value is null)
                return _emptyResult;

            var fileName = $"{path.Replace('/', '_')}.{value.Extension}";

            return File(value.Value, fileName.GetContentType(), fileName);
        }

        [HttpPost]
        public async Task<IActionResult> GetFileStream([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            var path = GetSensorPath(encodedId);

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
            return GetFileTable(await GetFileHistory(fileId));
        }

        private PartialViewResult GetFileTable(HistoryTableViewModel viewModel) =>
            PartialView("_FileAccordions", viewModel);

        private FileValue GetFileSensorValue(string encodedId) =>
            _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(encodedId)).LastValue as FileValue;

        private async Task<FileValue> GetFileByReceivingTimeOrDefault(string encodedId, long ticks = default) =>
            (ticks == default ? GetFileSensorValue(encodedId) : (await GetFileHistory(encodedId)).Pages[0].Cast<FileValue>().FirstOrDefault(file => file.ReceivingTime.Ticks == ticks)).DecompressContent();

        private async Task<HistoryTableViewModel> GetFileHistory(string encodedId)
        {
            var request = new GetSensorHistoryModel()
            {
                EncodedId = encodedId,
                BarsCount = -20,
            };

            await StoredUser.History.Reload(_treeValuesCache, request);

            return StoredUser.History.Table;
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

            var ttl = newModel.DataAlerts.TryGetValue(TimeToLiveAlertViewModel.AlertKey, out var alerts) && alerts.Count > 0 ? alerts[0] : null;
            var policyUpdates = newModel.DataAlerts.TryGetValue((byte)sensor.Type, out var list) ? list.Select(a => a.ToUpdate()).ToList() : new();

            var update = new SensorUpdate
            {
                Id = sensor.Id,
                Description = newModel.Description ?? string.Empty,
                TTL = ttl?.Conditions[0].TimeToLive.ToModel() ?? TimeIntervalModel.None,
                TTLPolicy = ttl?.ToTimeToLiveUpdate(CurrentUser.Name),
                KeepHistory = newModel.SavedHistoryPeriod.ToModel(),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(),
                Policies = policyUpdates,
                Initiator = CurrentUser.Name
            };

            _treeValuesCache.UpdateSensor(update);

            return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));
        }


        public IActionResult AddDataPolicy(byte type, Guid sensorId)
        {
            NodeViewModel entity = null;
            if (_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor))
                entity = sensor;
            if (_treeViewModel.Nodes.TryGetValue(sensorId, out var node))
                entity = node;

            DataAlertViewModelBase viewModel = type switch
            {
                (byte)SensorType.File => new DataAlertViewModel<FileValue>(sensorId),
                (byte)SensorType.String => new DataAlertViewModel<StringValue>(sensorId),
                (byte)SensorType.Boolean => new DataAlertViewModel<BooleanValue>(sensorId),
                (byte)SensorType.Version => new DataAlertViewModel<VersionValue>(sensorId),
                (byte)SensorType.TimeSpan => new DataAlertViewModel<TimeSpanValue>(sensorId),
                (byte)SensorType.Integer => new SingleDataAlertViewModel<IntegerValue, int>(sensorId),
                (byte)SensorType.Double => new SingleDataAlertViewModel<DoubleValue, double>(sensorId),
                (byte)SensorType.IntegerBar => new BarDataAlertViewModel<IntegerBarValue, int>(sensorId),
                (byte)SensorType.DoubleBar => new BarDataAlertViewModel<DoubleBarValue, double>(sensorId),
                TimeToLiveAlertViewModel.AlertKey => new TimeToLiveAlertViewModel(entity),
                _ => null,
            };

            return PartialView("~/Views/Home/Alerts/_DataAlert.cshtml", viewModel);
        }

        public IActionResult AddAlertCondition(Guid sensorId)
        {
            if (!_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor))
                return _emptyResult;

            ConditionViewModel viewModel = sensor.Type switch
            {
                SensorType.File => new ConditionViewModel<FileValue>(false),
                SensorType.String => new ConditionViewModel<StringValue>(false),
                SensorType.Boolean => new ConditionViewModel<BooleanValue>(false),
                SensorType.Version => new ConditionViewModel<VersionValue>(false),
                SensorType.TimeSpan => new ConditionViewModel<TimeSpanValue>(false),
                SensorType.Integer => new SingleConditionViewModel<IntegerValue, int>(false),
                SensorType.Double => new SingleConditionViewModel<DoubleValue, double>(false),
                SensorType.IntegerBar => new BarConditionViewModel<IntegerBarValue, int>(false),
                SensorType.DoubleBar => new BarConditionViewModel<DoubleBarValue, double>(false),
                _ => null,
            };

            return PartialView("~/Views/Home/Alerts/_ConditionBlock.cshtml", viewModel);
        }

        public IActionResult AddAlertAction() =>
            PartialView("~/Views/Home/Alerts/_ActionBlock.cshtml", new ActionViewModel(false));


        [HttpPost]
        public IActionResult GetTestToastMessage(AlertMessageViewModel alert)
        {
            if (!_treeViewModel.Sensors.TryGetValue(alert.EntityId, out _))
                return _emptyResult;

            var sensorModel = _treeValuesCache.GetSensor(alert.EntityId);

            return Json(alert.BuildToastMessage(sensorModel));
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
            sensorValue.Status = modal.NewStatus.ToApi();

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

            var ttl = newModel.DataAlerts.TryGetValue(TimeToLiveAlertViewModel.AlertKey, out var alerts) && alerts.Count > 0 ? alerts[0] : null;

            var update = new ProductUpdate
            {
                Id = product.Id,
                TTL = ttl?.Conditions[0].TimeToLive.ToModel(product.TTL) ?? TimeIntervalModel.None,
                TTLPolicy = ttl?.ToTimeToLiveUpdate(CurrentUser.Name),

                KeepHistory = newModel.SavedHistoryPeriod.ToModel(product.KeepHistory),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(product.SelfDestroy),
                Description = newModel.Description ?? string.Empty,
                Initiator = CurrentUser.Name
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
                return PartialView("_MetaInfo", new FolderInfoViewModel(_folderManager[newModel.EncodedId.ToGuid()]));

            var update = new FolderUpdate
            {
                Id = SensorPathHelper.DecodeGuid(newModel.EncodedId),
                Description = newModel.Description ?? string.Empty,
                TTL = newModel.ExpectedUpdateInterval,
                KeepHistory = newModel.SavedHistoryPeriod,
                SelfDestroy = newModel.SelfDestroyPeriod,
                Initiator = CurrentUser.Name
            };

            return await _folderManager.TryUpdate(update)
                ? PartialView("_MetaInfo", new FolderInfoViewModel(_folderManager[update.Id]))
                : _emptyResult;
        }

        private string GetSensorPath(string encodedId)
        {
            _treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var sensor);

            return sensor?.Path;
        }

        private AccessKeyModel GetKeyOrDefaultWithPermissions(Guid productId, KeyPermissions permissions) =>
            _treeValuesCache.GetProduct(productId).AccessKeys.Values.FirstOrDefault(x => x.IsValid(permissions, out _));
    }
}