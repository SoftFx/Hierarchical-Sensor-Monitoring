using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using HSMServer.Model.MultiToastViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.TreeViewModels;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TimeInterval = HSMServer.Model.TimeInterval;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : BaseController
    {
        private readonly ITelegramChatsManager _telegramChatsManager;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IJournalService _journalService;
        private readonly IFolderManager _folderManager;
        private readonly TreeViewModel _treeViewModel;


        public HomeController(ITreeValuesCache treeValuesCache, IFolderManager folderManager, TreeViewModel treeViewModel,
                              IUserManager userManager, IJournalService journalService, ITelegramChatsManager telegramChatsManager) : base(userManager)
        {
            _treeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
            _folderManager = folderManager;
            _journalService = journalService;
            _telegramChatsManager = telegramChatsManager;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("/Home/Index")]
        public IActionResult HomeIndex() => Redirect("/Home");

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("/")]
        [Route("/Home")]
        [Route("/Home/{sensorId:guid}")]
        public IActionResult Index(Guid sensorId)
        {
            if (_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor))
            {
                var parent = sensor.Parent;

                while (parent is ProductNodeViewModel node)
                {
                    CurrentUser.Tree.AddOpenedNode(parent.Id);
                    parent = node.Parent;
                }
            }

            return View();
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
                ? PartialView("~/Views/Tree/_TreeNode.cshtml", CurrentUser.Tree.LoadNode(node))
                : NotFound();

        [HttpPut]
        public void RemoveRenderingNode([FromBody] RemoveNodesRequestModel request) => CurrentUser.Tree.RemoveOpenedNode(request.NodeIds);

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
        public IActionResult RefreshTree(string searchParameter) =>
            PartialView("~/Views/Tree/_Tree.cshtml", CurrentUser.Tree.GetUserTree(searchParameter));

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
                    _treeValuesCache.UpdateMutedSensorState(sensorId, CurrentInitiator, newMutingPeriod);
            }
            else
            {
                if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    _treeValuesCache.UpdateMutedSensorState(sensor.Id, CurrentInitiator, newMutingPeriod);
            }
        }

        [HttpPost]
        public void RemoveMutedStateToSensor([FromQuery] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out _))
            {
                foreach (var sensorId in GetNodeSensors(decodedId))
                    _treeValuesCache.UpdateMutedSensorState(sensorId, CurrentInitiator);
            }
            else
            {
                if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    _treeValuesCache.UpdateMutedSensorState(sensor.Id, CurrentInitiator);
            }
        }

        [HttpPost]
        public ActionResult RemoveNode([FromBody] string[] ids)
        {
            var model = new MultiActionsToastViewModel();

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

                    _treeValuesCache.RemoveProduct(node.Id, InitiatorInfo.AsUser(CurrentUser.Name));
                    model.AddItem(node);
                }
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                {
                    if (!CurrentUser.IsManager(sensor.RootProduct.Id))
                    {
                        model.AddRoleError(sensor.FullPath, "remove");
                        continue;
                    }

                    _treeValuesCache.RemoveSensor(sensor.Id, CurrentInitiator);
                    model.AddItem(sensor);
                }
            }

            return Json(model.BuildResponse("Removed"));
        }

        [HttpGet]
        public IActionResult GetEditAlertsPartialView() => PartialView("~/Views/Tree/_MultiEditModal.cshtml", new EditAlertsViewModel());

        [HttpPost]
        public async Task<IActionResult> EditAlerts(EditAlertsViewModel model)
        {
            if (ModelState[nameof(model.ExpectedUpdateInterval)]?.Errors.Count > 0)
                return BadRequest(ModelState);

            model.Upload();

            var toastViewModel = new MultiActionsToastViewModel();
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
                        Initiator = CurrentInitiator,
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
                        toastViewModel.AddCantChangeIntervalError(product.Name, !isProduct ? "Node" : "Product", "Time to live",
                            TimeInterval.FromParent);
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
                        Initiator = CurrentInitiator
                    };

                    toastViewModel.AddItem(sensor);
                    _treeValuesCache.TryUpdateSensor(update, out _);
                }
            }

            return Json(toastViewModel.BuildResponse("Edited"));
        }

        [HttpPost]
        public void ClearHistoryNode([FromQuery] string selectedId)
        {
            ClearHistoryRequest GetRequest(Guid id) => new(id, CurrentInitiator);

            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                _treeValuesCache.ClearNodeHistory(GetRequest(node.Id));
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                _treeValuesCache.ClearSensorHistory(GetRequest(sensor.Id));
        }

        [HttpGet]
        public IActionResult MuteSensors(string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            IgnoreNotificationsViewModel viewModel = null;

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                viewModel = new IgnoreNotificationsViewModel(node);
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                viewModel = new IgnoreNotificationsViewModel(sensor);
            else if (_folderManager.TryGetValue(decodedId, out var folder))
                viewModel = new IgnoreNotificationsViewModel(folder);

            return PartialView("~/Views/Tree/_IgnoreNotificationsModal.cshtml", viewModel);
        }

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
                    Initiator = CurrentInitiator
                };

                _treeValuesCache.TryUpdateSensor(update, out _);
            }
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

            return icons is not null ? PartialView("~/Views/Home/Alerts/_AlertIconsList.cshtml", new AlertIconsViewModel(icons, true)) : _emptyResult;
        }

        #endregion

        #region File

        [HttpGet]
        public async Task<IActionResult> GetFile([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            var path = GetSensorPath(encodedId);

            var value = await GetFileByReceivingTimeOrDefault(encodedId, dateTime);

            var fileName = $"{path.Replace('/', '_')}.{value.Extension ?? FileExtensions.DefaultFileExtension}";

            return File(value.Value ?? Array.Empty<byte>(), fileName.GetContentType(), fileName);
        }

        [HttpPost]
        public async Task<IActionResult> GetFileStream([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            var path = GetSensorPath(encodedId);

            var value = await GetFileByReceivingTimeOrDefault(encodedId, dateTime);

            if (value?.Value is null)
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
            (ticks == default
                ? GetFileSensorValue(encodedId)
                : (await GetFileHistory(encodedId)).Pages[0].Cast<FileValue>().FirstOrDefault(file => file.ReceivingTime.Ticks == ticks))
            .DecompressContent();

        private async Task<HistoryTableViewModel> GetFileHistory(string encodedId)
        {
            var request = new GetSensorHistoryRequest()
            {
                EncodedId = encodedId,
                Count = -20,
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


        [HttpGet]
        public string RefreshHistoryInfo(Guid id)
        {
            void UpdateStats(ProductNodeViewModel vm, NodeHistoryInfo nodeInfo)
            {
                foreach (var (sensorId, sensorInfo) in nodeInfo.SensorsInfo)
                    if (_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor))
                        sensor.HistoryStatistic.Update(sensorInfo);

                foreach (var (subnodeId, subnodeInfo) in nodeInfo.SubnodesInfo)
                    if (_treeViewModel.Nodes.TryGetValue(subnodeId, out var node))
                        UpdateStats(node, subnodeInfo);

                vm.HistoryStatistic.RecalculateSubTreeStats(vm);
            }

            if (_treeViewModel.Sensors.TryGetValue(id, out var sensor))
            {
                sensor.HistoryStatistic.Update(_treeValuesCache.GetSensorHistoryInfo(sensor.Id));

                return sensor.HistoryStatistic.DisplayInfo;
            }

            if (_treeViewModel.Nodes.TryGetValue(id, out var node))
            {
                UpdateStats(node, _treeValuesCache.GetNodeHistoryInfo(node.Id));

                return node.HistoryStatistic.DisplayInfo;
            }

            return "Unknown";
        }

        [HttpGet]
        public FileResult SaveHistoryInfo(Guid id)
        {
            string nodePath = null;
            var content = new StringBuilder(1 << 10);

            content.AppendLine("PATH;COUNT;SIZE_bytes;%VALUES");

            if (_treeViewModel.Nodes.TryGetValue(id, out var node))
            {
                nodePath = node.FullPath;

                void BuildContent(ProductNodeViewModel model)
                {
                    foreach (var (_, sensor) in model.Sensors)
                        content.AppendLine(sensor.HistoryStatistic.ToCsvFormat(sensor.FullPath));

                    foreach (var (_, subNode) in model.Nodes)
                        BuildContent(subNode);
                }

                BuildContent(node);
            }

            var fileName = $"{nodePath.Replace('/', '_')}_DBstats.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            return File(Encoding.UTF8.GetBytes(content.ToString()), fileName.GetContentType(), fileName);
        }

        [HttpPost]
        public IActionResult UpdateSensorInfo(SensorInfoViewModel newModel)
        {
            if (!_treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(newModel.EncodedId), out var sensor))
                return _emptyResult;

            if (!ModelState.IsValid)
                return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));

            var availableChats = sensor.GetAvailableChats(_telegramChatsManager);

            var ttl = newModel.DataAlerts.TryGetValue(TimeToLiveAlertViewModel.AlertKey, out var alerts) && alerts.Count > 0 ? alerts[0] : null;
            var policyUpdates = newModel.DataAlerts.TryGetValue((byte)sensor.Type, out var list)
                ? list.Select(a => a.ToUpdate(availableChats)).ToList() : [];

            var update = new SensorUpdate
            {
                Id = sensor.Id,
                Description = newModel.Description ?? string.Empty,
                TTL = ttl?.Conditions[0].TimeToLive.ToModel() ?? TimeIntervalModel.None,
                TTLPolicy = ttl?.ToTimeToLiveUpdate(CurrentInitiator, availableChats),
                KeepHistory = newModel.SavedHistoryPeriod.ToModel(),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(),
                Policies = policyUpdates,
                SelectedUnit = newModel.SelectedUnit,
                AggregateValues = newModel.AggregateValues,
                Statistics = newModel.GetOptions(),
                Initiator = CurrentInitiator
            };

            _treeValuesCache.TryUpdateSensor(update, out _);

            return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));
        }


        public IActionResult AddDataPolicy(byte type, Guid entityId)
        {
            if (!TryGetSelectedNode(entityId, out var entity))
                return _emptyResult;

            DataAlertViewModelBase viewModel = type switch
            {
                (byte)SensorType.File => new FileDataAlertViewModel(entity),
                (byte)SensorType.String => new StringDataAlertViewModel(entity),
                (byte)SensorType.Boolean => new DataAlertViewModel<BooleanValue>(entity),
                (byte)SensorType.Version => new SingleDataAlertViewModel<VersionValue>(entity),
                (byte)SensorType.TimeSpan => new SingleDataAlertViewModel<TimeSpanValue>(entity),
                (byte)SensorType.Integer => new NumericDataAlertViewModel<IntegerValue>(entity),
                (byte)SensorType.Double => new NumericDataAlertViewModel<DoubleValue>(entity),
                (byte)SensorType.IntegerBar => new BarDataAlertViewModel<IntegerBarValue>(entity),
                (byte)SensorType.DoubleBar => new BarDataAlertViewModel<DoubleBarValue>(entity),
                TimeToLiveAlertViewModel.AlertKey => new TimeToLiveAlertViewModel(entity),
                _ => null,
            };

            return PartialView("~/Views/Home/Alerts/_DataAlert.cshtml", viewModel);
        }

        public IActionResult AddAlertCondition(Guid sensorId) => _treeViewModel.Sensors.TryGetValue(sensorId, out var sensor)
            ? PartialView("~/Views/Home/Alerts/_ConditionBlock.cshtml", BuildAlertCondition(sensor))
            : _emptyResult;

        public IActionResult AddAlertAction(Guid entityId) => TryGetSelectedNode(entityId, out var entity)
            ? PartialView("~/Views/Home/Alerts/_ActionBlock.cshtml", new ActionViewModel(false, entity))
            : _emptyResult;

        public IActionResult GetOperation(Guid sensorId, AlertProperty property)
        {
            if (!_treeViewModel.Sensors.TryGetValue(sensorId, out var sensor))
                return _emptyResult;

            var condition = BuildAlertCondition(sensor);

            return property switch
            {
                AlertProperty.NewSensorData => PartialView("~/Views/Home/Alerts/ConditionOperations/_NewDataOperation.cshtml"),

                AlertProperty.TimeToLive or AlertProperty.ConfirmationPeriod =>
                    PartialView("~/Views/Home/Alerts/ConditionOperations/_IntervalOperation.cshtml", condition.GetIntervalOperations(property)),

                _ => PartialView("~/Views/Home/Alerts/ConditionOperations/_SimpleOperation.cshtml", condition.GetOperations(property)),
            };
        }

        public IActionResult IsTargetVisible(PolicyOperation operation) => Json(operation.IsTargetVisible());

        private static ConditionViewModel BuildAlertCondition(SensorNodeViewModel sensor) =>
            sensor.Type switch
            {
                SensorType.File => new FileConditionViewModel(false),
                SensorType.String => new StringConditionViewModel(false),
                SensorType.Boolean => new CommonConditionViewModel(false),
                SensorType.Version => new SingleConditionViewModel(false),
                SensorType.TimeSpan => new SingleConditionViewModel(false),
                SensorType.Integer => new NumericConditionViewModel(false),
                SensorType.Double => new NumericConditionViewModel(false),
                SensorType.IntegerBar => new BarConditionViewModel(false),
                SensorType.DoubleBar => new BarConditionViewModel(false),
                _ => null,
            };

        private bool TryGetSelectedNode(Guid entityId, out NodeViewModel entity)
        {
            entity = null;

            if (_treeViewModel.Sensors.TryGetValue(entityId, out var sensor))
                entity = sensor;
            if (_treeViewModel.Nodes.TryGetValue(entityId, out var node))
                entity = node;

            return entity is not null;
        }


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
            var isAccessKeyExist =
                GetKeyOrDefaultWithPermissions(sensorNodeViewModel?.RootProduct.Id ?? Guid.Empty, KeyPermissions.CanSendSensorData) is not null;

            if (!isAccessKeyExist)
                ModelState.AddModelError(nameof(EditSensorStatusViewModal.RootProductId), EditSensorStatusViewModal.AccessKeyValidationErrorMessage);

            return PartialView("~/Views/Tree/_EditSensorStatusModal.cshtml", new EditSensorStatusViewModal(sensorNodeViewModel, isAccessKeyExist));
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
            var comment = modal.Comment;
            var updateRequest = new UpdateSensorValueRequestModel
            {
                Id = sensor.Id,
                Status = modal.NewStatus.ToCore(),
                Comment = comment,
                Value = modal.NewValue,
                ChangeLast = modal.ChangeLast,

                Initiator = CurrentInitiator,
            };

            _treeValuesCache.UpdateSensorValue(updateRequest);
            _treeViewModel.Sensors.TryGetValue(updateRequest.Id, out var sensorNodeViewModel);
            sensorNodeViewModel?.Update(sensor);

            return Ok();
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

            var availableChats = product.GetAvailableChats(_telegramChatsManager);
            var ttl = newModel.DataAlerts.TryGetValue(TimeToLiveAlertViewModel.AlertKey, out var alerts) && alerts.Count > 0 ? alerts[0] : null;

            var update = new ProductUpdate
            {
                Id = product.Id,
                TTL = ttl?.Conditions[0].TimeToLive.ToModel(product.TTL) ?? TimeIntervalModel.None,
                TTLPolicy = ttl?.ToTimeToLiveUpdate(CurrentInitiator, availableChats),

                KeepHistory = newModel.SavedHistoryPeriod.ToModel(product.KeepHistory),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(product.SelfDestroy),
                Description = newModel.Description ?? string.Empty,
                Initiator = CurrentInitiator
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
                Initiator = CurrentInitiator,
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