using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
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
using HSMServer.DTOs.Sensors;
using TimeInterval = HSMServer.Model.TimeInterval;
using HSMServer.Core.DataLayer;
using HSMServer.Core.SensorsUpdatesQueue;

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
        private readonly IDatabaseCore _database;


        public HomeController(ITreeValuesCache treeValuesCache, IFolderManager folderManager, TreeViewModel treeViewModel,
                              IUserManager userManager, IJournalService journalService, ITelegramChatsManager telegramChatsManager, 
                              IDatabaseCore database) : base(userManager)
        {
            _treeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
            _folderManager = folderManager;
            _journalService = journalService;
            _telegramChatsManager = telegramChatsManager;
            _database = database;
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

        [HttpPost]
        public IActionResult AddToRenderingTree([FromBody] params Guid[] ids)
        {
            CurrentUser.Tree.AddOpenedNodes(ids);
            return Ok();
        }

        [HttpGet]
        public IActionResult GetNode(string id, bool isSearchRefresh = false) =>
            _treeViewModel.Nodes.TryGetValue(id.ToGuid(), out var node)
                ? PartialView("~/Views/Tree/_TreeNode.cshtml", CurrentUser.Tree.LoadNode(node, isSearchRefresh))
                : NotFound();

        [HttpPut]
        public void RemoveRenderingNode([FromBody] RemoveNodesRequestModel request) => CurrentUser.Tree.RemoveOpenedNode(request);

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
        public IActionResult RefreshTree(SearchPattern searchPattern) =>
            PartialView("~/Views/Tree/_Tree.cshtml", CurrentUser.Tree.GetUserTree(searchPattern));

        [HttpGet]
        public IActionResult ApplyFilter(UserFilterViewModel viewModel)
        {
            CurrentUser.TreeFilter = viewModel.ToFilter();
            _userManager.UpdateUser(CurrentUser);

            return Redirect("Index");
        }

        [HttpPost]
        public async ValueTask SetMutedStateToSensorFromModal(IgnoreNotificationsViewModel model)
        {
            var newMutingPeriod = model.EndOfIgnorePeriod;

            foreach (var id in model.Ids)
            {
                var decodedId = SensorPathHelper.DecodeGuid(id);
                if (_treeViewModel.Nodes.TryGetValue(decodedId, out _))
                {
                    foreach (var sensorId in GetNodeSensors(decodedId))
                        await _treeValuesCache.UpdateMutedSensorStateAsync(sensorId, CurrentInitiator, newMutingPeriod);
                }
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    await _treeValuesCache.UpdateMutedSensorStateAsync(sensor.Id, CurrentInitiator, newMutingPeriod);
            }
        }

        [HttpPost]
        public async ValueTask RemoveMutedStateToSensor([FromBody] string[] ids)
        {
            foreach (var id in ids)
            {
                var decodedId = SensorPathHelper.DecodeGuid(id);

                if (_treeViewModel.Nodes.TryGetValue(decodedId, out _))
                {
                    foreach (var sensorId in GetNodeSensors(decodedId))
                        await _treeValuesCache.UpdateMutedSensorStateAsync(sensorId, CurrentInitiator);
                }
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    await _treeValuesCache.UpdateMutedSensorStateAsync(sensor.Id, CurrentInitiator);
            }
        }

        [HttpPost]
        public async Task<ActionResult> RemoveNode([FromBody] string[] ids)
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

                    await _treeValuesCache.RemoveProductAsync(node.Id, InitiatorInfo.AsUser(CurrentUser.Name));
                    model.AddItem(node);
                }
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                {
                    if (!CurrentUser.IsManager(sensor.RootProduct.Id))
                    {
                        model.AddRoleError(sensor.FullPath, "remove");
                        continue;
                    }

                    await _treeValuesCache.RemoveSensorAsync(sensor.Id, CurrentInitiator);
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
                    if (!CurrentUser.IsManager(product?.RootProduct?.Id ?? Guid.Empty))
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
                        await _treeValuesCache.UpdateProductAsync(update);
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
                    await _treeValuesCache.UpdateSensorAsync(update);
                }
            }

            return Json(toastViewModel.BuildResponse("Edited"));
        }

        [HttpPost]
        public async ValueTask ClearHistoryNode([FromQuery] string selectedId)
        {
            ClearHistoryRequest GetRequest(Guid id) => new(id, CurrentInitiator);

            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                await _treeValuesCache.ClearNodeHistoryAsync(GetRequest(node.Id));
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                await _treeValuesCache.ClearSensorHistoryAsync(GetRequest(sensor.Id));
        }

        [HttpPost]
        public IActionResult MuteSensors([FromBody] string[] ids)
        {
            var result = new List<BaseNodeViewModel>(ids.Length);

            foreach (string id in ids)
            {
                var decodedId = SensorPathHelper.DecodeGuid(id);

                if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                    result.Add(node);
                else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                    result.Add(sensor);
                else if (_folderManager.TryGetValue(decodedId, out var folder))
                    result.Add(folder);
            }

            return PartialView("~/Views/Tree/_IgnoreNotificationsModal.cshtml", new IgnoreNotificationsViewModel(result));
        }

        [HttpPost]
        public string GetNodePath([FromQuery] string selectedId, [FromQuery] bool isFullPath = true)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return isFullPath ? node.FullPath : node.Path;
            else if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                return isFullPath ? sensor.FullPath : sensor.Path;

            return string.Empty;
        }

        [HttpPost]
        public ValueTask EnableGrafana(string selectedId) => ChangeSensorsIntegrationAsync(selectedId, Integration.Grafana);

        [HttpPost]
        public ValueTask DisableGrafana(string selectedId) => ChangeSensorsIntegrationAsync(selectedId, 0);

        private async ValueTask ChangeSensorsIntegrationAsync(string selectedNode, Integration integration)
        {
            foreach (var sensorId in GetNodeSensors(SensorPathHelper.DecodeGuid(selectedNode)))
            {
                var update = new SensorUpdate()
                {
                    Id = sensorId,
                    Integration = integration,
                    Initiator = CurrentInitiator
                };

                await _treeValuesCache.UpdateSensorAsync(update);
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

            // TODO: implement update selected folder tree item
            if (_treeViewModel.Nodes.TryGetValue(decodedId, out var node))
                return Json(new UpdatedNodeDataViewModel(node));

            if (_treeViewModel.Sensors.TryGetValue(decodedId, out var sensor))
                return Json(new UpdatedSensorDataViewModel(sensor, CurrentUser));

            return _emptyResult;
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
        public async ValueTask<IActionResult> UpdateSensorInfo(SensorInfoViewModel newModel)
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
                Initiator = CurrentInitiator,
                DisplayUnit = newModel.DisplayUnit
            };

            await _treeValuesCache.UpdateSensorAsync(update);

            return PartialView("_MetaInfo", new SensorInfoViewModel(sensor));
        }


        public IActionResult AddDataPolicy(byte type, Guid entityId)
        {
            TryGetSelectedNode(entityId, out var entity);

            DataAlertViewModelBase viewModel = DataAlertViewModel.BuildAlert(type, entity);

            return PartialView("~/Views/Home/Alerts/_DataAlert.cshtml", viewModel);
        }

        public IActionResult AddAlertCondition(byte type)
        {
            return PartialView("~/Views/Home/Alerts/_ConditionBlock.cshtml", BuildAlertCondition(type));
        }

        public IActionResult AddAlertAction(Guid entityId, bool isMain, bool isTtl)
        {
            HashSet<Guid> chats = [];
            if (TryGetSelectedNode(entityId, out var entity))
                entity.TryGetChats(out chats);

            return PartialView("~/Views/Home/Alerts/_ActionBlock.cshtml", new ActionViewModel(isMain, isTtl, chats) { Icon = ActionViewModel.DefaultIcon });
        }

        public IActionResult GetOperation(byte type, AlertProperty property)
        {
            var condition = BuildAlertCondition(type);

            return property switch
            {
                AlertProperty.NewSensorData => PartialView("~/Views/Home/Alerts/ConditionOperations/_NewDataOperation.cshtml"),

                AlertProperty.TimeToLive or AlertProperty.ConfirmationPeriod =>
                    PartialView("~/Views/Home/Alerts/ConditionOperations/_IntervalOperation.cshtml", condition.GetIntervalOperations(property)),

                _ => PartialView("~/Views/Home/Alerts/ConditionOperations/_SimpleOperation.cshtml", condition.GetOperations(property)),
            };
        }

        public IActionResult IsTargetVisible(PolicyOperation operation) => Json(operation.IsTargetVisible());

        private static ConditionViewModel BuildAlertCondition(byte type) =>
            type switch
            {
                (byte)SensorType.File => new FileConditionViewModel(false),
                (byte)SensorType.String => new StringConditionViewModel(false),
                (byte)SensorType.Boolean => new CommonConditionViewModel(false),
                (byte)SensorType.Version => new VersionConditionViewModel(false),
                (byte)SensorType.TimeSpan => new SingleConditionViewModel(false),
                (byte)SensorType.Integer => new NumericConditionViewModel(false),
                (byte)SensorType.Double => new NumericConditionViewModel(false),
                (byte)SensorType.Rate => new NumericConditionViewModel(false),
                (byte)SensorType.IntegerBar => new BarConditionViewModel(false),
                (byte)SensorType.DoubleBar => new BarConditionViewModel(false),
                (byte)SensorType.Enum => new NumericConditionViewModel(false),
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

            var message = alert.BuildToastMessage(sensorModel);

            return Json(MarkdownHelper.ToHtml(message));
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
        public async ValueTask<IActionResult> UpdateSensorStatus(EditSensorStatusViewModal modal)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var key = GetKeyOrDefaultWithPermissions(modal.RootProductId, KeyPermissions.CanSendSensorData)?.Id;

            if (key is null)
            {
                ModelState.AddModelError(nameof(EditSensorStatusViewModal.RootProductId), EditSensorStatusViewModal.AccessKeyValidationErrorMessage);
                return BadRequest(ModelState);
            }

            var comment = modal.Comment;
            var updateRequest = new UpdateSensorValueRequestModel (modal.SensorId, modal.Path)
            {
                Id = modal.SensorId,
                Status = modal.NewStatus.ToCore(),
                Comment = comment,
                Value = modal.NewValue,
                ChangeLast = modal.ChangeLast,

                Initiator = CurrentInitiator,
            };

            await _treeValuesCache.UpdateSensorValueAsync(updateRequest);


            return Ok();
        }

        [HttpPost("Home/{sensorId:guid}")]
        public async Task<IActionResult> UpdateSensorTableSettings(Guid sensorId, [FromBody] TableSettingsUpdateDto tableSettingsUpdateDto)
        {
            var sensor = _treeValuesCache.GetSensor(sensorId);

            if (sensor is null)
                return BadRequest("No sensor found");

            if (tableSettingsUpdateDto.MaxCommentHideSize is not null &&
                tableSettingsUpdateDto.MaxCommentHideSize < 0)
                return BadRequest("Can't use negative numbers");


            var update = new SensorUpdate
            {
                Id = sensor.Id,
                IsHideEnabled = tableSettingsUpdateDto.IsHideEnabled,
                MaxCommentHideSize = tableSettingsUpdateDto.MaxCommentHideSize,
                Initiator = CurrentInitiator
            };

            await _treeValuesCache.UpdateSensorAsync(update);

            return Ok("Successfully updated");
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
        public async Task<IActionResult> UpdateProductInfo(ProductInfoViewModel newModel)
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
                DefaultChats = newModel.DefaultChats?.ToUpdate(product, _telegramChatsManager, _folderManager),
                KeepHistory = newModel.SavedHistoryPeriod.ToModel(product.KeepHistory),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(product.SelfDestroy),
                Description = newModel.Description ?? string.Empty,
                Initiator = CurrentInitiator
            };

            await _treeValuesCache.UpdateProductAsync(update);

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
                DefaultChats = newModel.DefaultChats,
                Initiator = CurrentInitiator,
            };

            return await _folderManager.TryUpdate(update)
                ? PartialView("_MetaInfo", new FolderInfoViewModel(_folderManager[update.Id]))
                : _emptyResult;
        }

        [HttpGet]
        public JsonResult Compact()
        {
            if (_database.IsCompactRunning)
                return Json(JsonSerializer.Serialize(new { Status = "Error", Error = "Compact already running" }));

            _database.Compact();

            return Json(JsonSerializer.Serialize(new { Status = "Ok", Result = Math.Round(_database.TotalDbSize / (double)(1 << 20), 2, MidpointRounding.AwayFromZero) }));
        }

        private string GetSensorPath(string encodedId)
        {
            _treeViewModel.Sensors.TryGetValue(SensorPathHelper.DecodeGuid(encodedId), out var sensor);

            return sensor?.Path;
        }

        private AccessKeyModel GetKeyOrDefaultWithPermissions(Guid productId, KeyPermissions permissions)
        {
            if (_treeValuesCache.TryGetProduct(productId, out var product))
            {
                return product.AccessKeys.Values
                    .FirstOrDefault(x => x.IsValid(permissions, out _));
            }

            return null;
        }
    }
}