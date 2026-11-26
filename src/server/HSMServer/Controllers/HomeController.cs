using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Attributes;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Extensions;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Schedule;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
using HSMServer.DTOs.Sensors;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.FileSensors;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Model.MultiToastViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.TreeViewModels;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TimeInterval = HSMServer.Model.TimeInterval;


namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : BaseController
    {
        private readonly IChatsManager _chatsManager;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IJournalService _journalService;
        private readonly IFolderManager _folderManager;
        // Every sensor, node or folder id from the client is resolved through this: it answers "not
        // found" for an entity of a product (or folder) the user has no role on. The controller holds no
        // reference to the global tree, so an action cannot look an id up past the check. An action that
        // changes what it addresses uses the Managed lookups: seeing a product is not enough to change it.
        private readonly UserTreeAccess _tree;
        private readonly IDatabaseCore _database;
        private readonly IAlertScheduleProvider _alertScheduleProvider;
        private readonly PolicyAdministrationService _policies;
        private readonly SensorMoveAdministrationService _mover;
        private readonly FileSensorPageService _files;


        public HomeController(ITreeValuesCache treeValuesCache, IFolderManager folderManager, UserTreeAccess tree,
                              IUserManager userManager, IJournalService journalService, IChatsManager chatsManager,
                              IDatabaseCore database, IAlertScheduleProvider provider,
                              PolicyAdministrationService policies,
                              SensorMoveAdministrationService mover,
                              FileSensorPageService files) : base(userManager)
        {
            _mover = mover;
            _files = files;
            _treeValuesCache = treeValuesCache;
            _tree = tree;
            _folderManager = folderManager;
            _journalService = journalService;
            _chatsManager = chatsManager;
            _database = database;
            _alertScheduleProvider = provider;
            _policies = policies;
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
            if (_tree.TryGetSensor(CurrentUser, sensorId, out var sensor))
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

                // A refused id is an unknown id: nothing is connected to the user's state, nothing is opened
                // in the user's tree, and the panel renders "No Data".
                if (_tree.TryGetFolder(CurrentUser, id, out var folder))
                {
                    viewModel = folder;
                    StoredUser.SelectedNode.ConnectFolder(folder, _tree.GetVisibleProducts(CurrentUser, folder));
                    CurrentUser.Tree.AddOpenedNode(id);
                }
                else if (_tree.TryGetNode(CurrentUser, id, out var node))
                {
                    viewModel = node;
                    var schedulesList = GetAlertSchedulesSelectList();
                    node.TTLAlerts.ForEach(a => a.Schedules = schedulesList);
                    foreach (var (_, alerts) in node.DataAlerts)
                        foreach (var alert in alerts)
                            alert.Schedules = schedulesList;
                    StoredUser.SelectedNode.ConnectNode(node);
                    StoredUser.SelectedNode.ShowChartTab = _tree.HasComparableChildGroup(node);
                    CurrentUser.Tree.AddOpenedNode(id);
                }
                else if (_tree.TryGetSensor(CurrentUser, id, out var sensor))
                {
                    viewModel = sensor;
                    var schedulesList = GetAlertSchedulesSelectList();
                    StoredUser.History.ConnectSensor(_treeValuesCache.GetSensor(id));
                    sensor.TTLAlerts.ForEach(a => a.Schedules = schedulesList);
                    foreach (var (key, alerts) in sensor.DataAlerts)
                        foreach (var alert in alerts)
                        {
                            alert.Schedules = schedulesList;
                        }
                }
            }

            var user = CurrentUser;
            await StoredUser.Journal.ConnectJournal(viewModel, _journalService, product => _tree.IsVisible(user, product));

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
            _tree.TryGetNode(CurrentUser, id.ToGuid(), out var node)
                ? PartialView("~/Views/Tree/_TreeNode.cshtml", CurrentUser.Tree.LoadNode(node, isSearchRefresh))
                : NotFound();

        [HttpPut]
        public void RemoveRenderingNode([FromBody] RemoveNodesRequestModel request) => CurrentUser.Tree.RemoveOpenedNode(request);

        [HttpGet]
        public IActionResult GetGrid(ChildrenPageRequest pageRequest)
        {
            if (!IsSelectedNodeVisible())
                return _emptyResult;

            var model = StoredUser.SelectedNode.GetNextPage(pageRequest);

            return model.IsPageValid ? PartialView("_GridAccordion", model) : _emptyResult;
        }

        [HttpGet]
        public IActionResult GetList(ChildrenPageRequest pageRequest)
        {
            if (!IsSelectedNodeVisible())
                return _emptyResult;

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
                if (_tree.TryGetManagedNode(CurrentUser, decodedId, out _))
                {
                    foreach (var sensorId in GetManagedSensors(decodedId))
                        await _treeValuesCache.UpdateMutedSensorStateAsync(sensorId, CurrentInitiator, newMutingPeriod);
                }
                else if (_tree.TryGetManagedSensor(CurrentUser, decodedId, out var sensor))
                    await _treeValuesCache.UpdateMutedSensorStateAsync(sensor.Id, CurrentInitiator, newMutingPeriod);
            }
        }

        [HttpPost]
        public async ValueTask RemoveMutedStateToSensor([FromBody] string[] ids)
        {
            foreach (var id in ids)
            {
                var decodedId = SensorPathHelper.DecodeGuid(id);

                if (_tree.TryGetManagedNode(CurrentUser, decodedId, out _))
                {
                    foreach (var sensorId in GetManagedSensors(decodedId))
                        await _treeValuesCache.UpdateMutedSensorStateAsync(sensorId, CurrentInitiator);
                }
                else if (_tree.TryGetManagedSensor(CurrentUser, decodedId, out var sensor))
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

                if (_tree.TryGetFolder(CurrentUser, decodedId, out var folder))
                {
                    model.AddRemoveFolderError(folder.Name);
                }
                else if (_tree.TryGetNode(CurrentUser, decodedId, out var node))
                {
                    if (!CurrentUser.IsManager(node.RootProduct.Id))
                    {
                        model.AddRoleError(node.Name, "remove");
                        continue;
                    }

                    await _treeValuesCache.RemoveProductAsync(node.Id, InitiatorInfo.AsUser(CurrentUser.Name));
                    model.AddItem(node);
                }
                else if (_tree.TryGetSensor(CurrentUser, decodedId, out var sensor))
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

        // Sensor move (#1722): the modal, the move itself and alias removal.
        // The decisions live in SensorMoveAdministrationService (the engine of
        // the REST and MCP surfaces); this controller only maps the web user
        // onto it through CookieSensorMoveAccess — Manager of the sensor's and
        // of the destination's root product, or admin (the RemoveNode rule, #1512).
        [HttpGet]
        public IActionResult GetMoveSensorsModal([FromQuery] string[] ids)
        {
            // Every selected sensor the user can see goes to the server, managed
            // or not: the move answers "no rights" for the ones the user may not
            // move instead of the modal silently dropping them. A sensor out of
            // the user's sight is not shown at all (#1722 review).
            var sensors = (ids ?? [])
                .Select(id => _treeValuesCache.GetSensor(SensorPathHelper.DecodeGuid(id)))
                .Where(s => s?.Root is not null && CurrentUser.IsProductAvailable(s.Root.Id))
                .ToList();

            if (sensors.Count == 0)
                return BadRequest("Select one or more sensors.");

            var first = sensors[0];

            return PartialView("~/Views/Tree/_MoveSensorsModal.cshtml", new MoveSensorsViewModel
            {
                SensorIds = sensors.Select(s => SensorPathHelper.EncodeGuid(s.Id)).ToList(),
                SensorNames = sensors.Select(s => s.DisplayName).ToList(),
                Products = _treeValuesCache.GetProducts()
                    .Where(p => CurrentUser.IsManager(p.Id))
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(p => (p.Id, p.DisplayName))
                    .ToList(),
                ProductId = first.Root.Id,
                Destination = sensors.Count == 1 ? first.Path : first.Parent?.Path ?? string.Empty,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveSensors([FromBody] MoveSensorsRequestViewModel model)
        {
            if (model is null)
                return BadRequest(new { error = "A move request is required." });

            model.SensorIds ??= [];

            var request = new SensorMoveRequestDto
            {
                Sensors = model.SensorIds.Select(SensorPathHelper.DecodeGuid).ToList(),
                Product = model.ProductId,
                Path = model.SensorIds.Count == 1 ? model.Destination ?? string.Empty : null,
                Node = model.SensorIds.Count == 1 ? null : model.Destination ?? string.Empty,
                OnConflict = model.Merge ? "merge" : "fail",
            };

            var result = await _mover.MoveSensorsAsync(request,
                new CookieSensorMoveAccess(CurrentUser, _treeValuesCache),
                new ChangeSetWriteOptions { DryRun = model.DryRun }, HttpContext.RequestAborted);

            if (!result.Success)
                return BadRequest(new { error = MoveFailureText(result.Failure) });

            return result.IsDryRun ? Json(result.Preview) : Json(result.Value);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSensorAlias(Guid sensorId, Guid productId, string path)
        {
            var result = await _mover.RemoveAliasAsync(sensorId, productId, path,
                new CookieSensorMoveAccess(CurrentUser, _treeValuesCache), HttpContext.RequestAborted);

            return result.Success ? Ok() : BadRequest(new { error = MoveFailureText(result.Failure) });
        }

        private static string MoveFailureText(PolicyWriteFailure failure) => failure.Outcome switch
        {
            PolicyWriteOutcome.Invalid or PolicyWriteOutcome.Validation when failure.Errors is { Count: > 0 } =>
                string.Join(" ", failure.Errors.SelectMany(e => e.Value)),
            PolicyWriteOutcome.Forbidden => "You need the Manager role on both the sensors' and the destination's product.",
            PolicyWriteOutcome.NotFound => failure.Message ?? "Not found.",
            _ => failure.Message ?? "The move could not be applied.",
        };

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
                if (_tree.TryGetFolder(CurrentUser, id, out var folder))
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
                        toastViewModel.AddCantChangeIntervalError(folder.Name, "Folder", "Inactivity Period", TimeInterval.FromParent);
                    else
                    {
                        toastViewModel.AddItem(folder);
                        await _folderManager.TryUpdate(update);
                    }
                }
                else if (_tree.TryGetNode(CurrentUser, id, out var product))
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
                        toastViewModel.AddCantChangeIntervalError(product.Name, !isProduct ? "Node" : "Product", "Inactivity Period", TimeInterval.FromParent);
                    else
                    {
                        toastViewModel.AddItem(product);
                        await _treeValuesCache.UpdateProductAsync(update);
                    }
                }
                else if (_tree.TryGetSensor(CurrentUser, id, out var sensor))
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

            // A table sensor's rows are not cleared (#1844): the core refuses that clear, and a node
            // clear leaves its table sensors alone.
            if (_tree.TryGetManagedNode(CurrentUser, decodedId, out var node))
                await _treeValuesCache.ClearNodeHistoryAsync(GetRequest(node.Id));
            else if (_tree.TryGetManagedSensor(CurrentUser, decodedId, out var sensor))
                await _treeValuesCache.ClearSensorHistoryAsync(GetRequest(sensor.Id));
        }

        // The mute dialog names what its save will mute, so it lists the items the user manages.
        [HttpPost]
        public IActionResult MuteSensors([FromBody] string[] ids)
        {
            var result = new List<BaseNodeViewModel>(ids.Length);

            foreach (string id in ids)
            {
                var decodedId = SensorPathHelper.DecodeGuid(id);

                if (_tree.TryGetManagedNode(CurrentUser, decodedId, out var node))
                    result.Add(node);
                else if (_tree.TryGetManagedSensor(CurrentUser, decodedId, out var sensor))
                    result.Add(sensor);
                else if (_tree.TryGetManagedFolder(CurrentUser, decodedId, out var folder))
                    result.Add(folder);
            }

            return PartialView("~/Views/Tree/_IgnoreNotificationsModal.cshtml", new IgnoreNotificationsViewModel(result));
        }

        [HttpPost]
        public string GetNodePath([FromQuery] string selectedId, [FromQuery] bool isFullPath = true)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_tree.TryGetNode(CurrentUser, decodedId, out var node))
                return isFullPath ? node.FullPath : node.Path;
            else if (_tree.TryGetSensor(CurrentUser, decodedId, out var sensor))
                return isFullPath ? sensor.FullPath : sensor.Path;

            return string.Empty;
        }

        [HttpGet]
        public IActionResult ExportNode([FromQuery] string selectedId)
        {
            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            if (_tree.TryGetNode(CurrentUser, decodedId, out var node))
            {
                var fileName = node.Name;

                using (var memoryStream = new MemoryStream())
                using (var writer = new StreamWriter(memoryStream, Encoding.UTF8))
                {

                    writer.WriteLine("sep=;");
                    writer.WriteLine("\"Path\";\"Type\";\"Discription\"");

                    AddNodeToStream(node, writer);

                    writer.Flush();

                    memoryStream.Position = 0;

                    return File(memoryStream.ToArray(), "text/csv", $"{fileName}.csv");
                }
            }

            return Ok();
        }
        private void AddNodeToStream(ProductNodeViewModel node, StreamWriter stream)
        {
            foreach (var item in node.Nodes.Values)
            {
                AddNodeToStream(item, stream);
            }

            foreach (var sensor in node.Sensors.Values)
            {
                stream.WriteLine($"\"{sensor.FullPath}\";\"{sensor.Type}\";\"{sensor.Description}\"");
            }

        }


        [HttpPost]
        public ValueTask EnableGrafana(string selectedId) => ChangeSensorsIntegrationAsync(selectedId, Integration.Grafana);

        [HttpPost]
        public ValueTask DisableGrafana(string selectedId) => ChangeSensorsIntegrationAsync(selectedId, 0);

        private async ValueTask ChangeSensorsIntegrationAsync(string selectedNode, Integration integration)
        {
            foreach (var sensorId in GetManagedSensors(SensorPathHelper.DecodeGuid(selectedNode)))
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

        // Sensors under a sensor, node or folder id from the client that the user may change; empty when
        // the user does not manage it.
        private List<Guid> GetManagedSensors(Guid id) => _tree.GetManagedSensorIds(CurrentUser, id);

        #region Update

        [HttpPost]
        public ActionResult UpdateSelectedNode([FromQuery(Name = "Selected")] string selectedId)
        {
            if (string.IsNullOrEmpty(selectedId))
                return Json(string.Empty);

            var decodedId = SensorPathHelper.DecodeGuid(selectedId);

            // TODO: implement update selected folder tree item
            if (_tree.TryGetNode(CurrentUser, decodedId, out var node))
                return Json(new UpdatedNodeDataViewModel(node));

            if (_tree.TryGetSensor(CurrentUser, decodedId, out var sensor))
                return Json(new UpdatedSensorDataViewModel(sensor, CurrentUser));

            return _emptyResult;
        }

        [HttpGet]
        public ActionResult GetGeneralInfo(string selectedId)
        {
            var id = selectedId.ToGuid();

            if (_tree.TryGetNode(CurrentUser, id, out var node))
                return PartialView("_GeneralInfo", new ProductInfoViewModel(node));

            if (_tree.TryGetFolder(CurrentUser, id, out var folder))
                return PartialView("_GeneralInfo", FolderInfo(folder));

            if (_tree.TryGetSensor(CurrentUser, id, out var sensor))
                return PartialView("_GeneralInfo", new SensorInfoViewModel(sensor));

            return _emptyResult;
        }

        [HttpGet]
        public ActionResult GetAlertIcons(string selectedId)
        {
            var id = selectedId.ToGuid();
            ConcurrentDictionary<string, int> icons = null;

            if (_tree.TryGetNode(CurrentUser, id, out var node))
                icons = node.AlertIcons;
            else if (_tree.TryGetSensor(CurrentUser, id, out var sensor))
                icons = sensor.AlertIcons;

            return icons is not null ? PartialView("~/Views/Home/Alerts/_AlertIconsList.cshtml", new AlertIconsViewModel(icons, true)) : _emptyResult;
        }

        #endregion

        #region File

        // The file actions answer an unknown sensor and a sensor of a product the user may not see the same way
        // (TryGetSensor, through the user's roles). The work is in FileSensorPageService; a large file value (#1886) is
        // never served from here.

        [HttpGet]
        public async Task<IActionResult> GetFile([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            if (!TryGetSensor(encodedId, out var sensor))
                return NotFound();

            var value = await _files.GetFileAsync(sensor.Id, dateTime);

            if (value is null || FileSensorPageService.IsLarge(value))
                return NotFound();

            var fileName = $"{sensor.Path.Replace('/', '_')}.{value.Extension ?? FileExtensions.DefaultFileExtension}";

            return File(value.Value ?? Array.Empty<byte>(), fileName.GetContentType(), fileName);
        }

        [HttpPost]
        public async Task<IActionResult> GetFileStream([FromQuery(Name = "Selected")] string encodedId, [FromQuery] long dateTime = default)
        {
            if (!TryGetSensor(encodedId, out var sensor))
                return NotFound();

            var value = await _files.GetFileAsync(sensor.Id, dateTime);

            if (value is null || FileSensorPageService.IsLarge(value))
                return NotFound();

            if (value.Value is null)
                return _emptyResult;

            var fileContentsStream = new MemoryStream(value.Value);
            var fileName = $"{sensor.Path.Replace('/', '_')}.{value.Extension}";

            return File(fileContentsStream, fileName.GetContentType(), fileName);
        }

        // The description of the last value, never its bytes (#1886).
        [HttpGet]
        public ActionResult<FileInfoDto> GetFileInfo([FromQuery(Name = "Selected")] string encodedId)
        {
            if (!TryGetSensor(encodedId, out var sensor))
                return NotFound();

            var info = _files.GetInfo(sensor.Id);

            return info is null ? _emptyResult : info;
        }

        // The first page of the list of file values under the last one.
        [HttpGet]
        public Task<IActionResult> GetRecentFilesView([FromQuery] string fileId) => GetFilesPage(fileId, null);

        // A page of the list: the values older than olderThan (ticks of a value's time), newest first.
        [HttpGet]
        public async Task<IActionResult> GetFilesPage([FromQuery] string fileId, [FromQuery] long? olderThan)
        {
            if (!TryGetSensor(fileId, out var sensor) || sensor.Type is not SensorType.File)
                return NotFound();

            return PartialView("_FileAccordions", await _files.GetPageAsync(sensor.Id, sensor.EncodedId, olderThan, CurrentUser));
        }

        // Who downloaded a large file of the sensor, for a Manager of the product or an admin, read when the row asks for it.
        [HttpGet]
        public IActionResult GetLargeFileDownloads([FromQuery] string fileId, [FromQuery] Guid file)
        {
            if (!TryGetSensor(fileId, out var sensor) || _files.GetDownloads(sensor.Id, file, CurrentUser) is not { } downloads)
                return NotFound();

            return PartialView("_LargeFileDownloads", downloads);
        }

        #endregion

        #region Sensor info

        [HttpPost]
        public bool IsMetaInfoValid(NodeInfoBaseViewModel _) => ModelState.IsValid;

        [HttpGet]
        public IActionResult GetSensorInfo([FromQuery(Name = "Id")] string encodedId)
        {
            if (!_tree.TryGetSensor(CurrentUser, SensorPathHelper.DecodeGuid(encodedId), out var sensor))
                return _emptyResult;

            var model = new SensorInfoViewModel(sensor);
            PopulateAlertSchedules(model);
            return PartialView("_MetaInfo", model);
        }


        [HttpGet]
        public string RefreshHistoryInfo(Guid id)
        {
            void UpdateStats(ProductNodeViewModel vm, NodeHistoryInfo nodeInfo)
            {
                foreach (var (sensorId, sensorInfo) in nodeInfo.SensorsInfo)
                    if (_tree.TryGetSensor(CurrentUser, sensorId, out var sensor))
                        sensor.HistoryStatistic.Update(sensorInfo);

                foreach (var (subnodeId, subnodeInfo) in nodeInfo.SubnodesInfo)
                    if (_tree.TryGetNode(CurrentUser, subnodeId, out var node))
                        UpdateStats(node, subnodeInfo);

                vm.HistoryStatistic.RecalculateSubTreeStats(vm);
            }

            if (_tree.TryGetSensor(CurrentUser, id, out var sensor))
            {
                sensor.HistoryStatistic.Update(_treeValuesCache.GetSensorHistoryInfo(sensor.Id));

                return sensor.HistoryStatistic.DisplayInfo;
            }

            if (_tree.TryGetNode(CurrentUser, id, out var node))
            {
                UpdateStats(node, _treeValuesCache.GetNodeHistoryInfo(node.Id));

                return node.HistoryStatistic.DisplayInfo;
            }

            return "Unknown";
        }

        [HttpGet]
        public IActionResult SaveHistoryInfo(Guid id)
        {
            // Unknown and not-visible ids answer the same 404 (the unchecked version threw on an unknown id).
            if (!_tree.TryGetNode(CurrentUser, id, out var node))
                return NotFound();

            var content = new StringBuilder(1 << 10);

            content.AppendLine("PATH;COUNT;SIZE_bytes;%VALUES");

            void BuildContent(ProductNodeViewModel model)
            {
                foreach (var (_, sensor) in model.Sensors)
                    content.AppendLine(sensor.HistoryStatistic.ToCsvFormat(sensor.FullPath));

                foreach (var (_, subNode) in model.Nodes)
                    BuildContent(subNode);
            }

            BuildContent(node);

            var fileName = $"{node.FullPath.Replace('/', '_')}_DBstats.csv";
            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            return File(Encoding.UTF8.GetBytes(content.ToString()), fileName.GetContentType(), fileName);
        }

        // The web form's write options (#1588): the request-scoped undo
        // group shared by every journaled write this post performs.
        private ChangeSetWriteOptions JournalWriteOptions() =>
            new() { ChangeSetId = ChangeSetHeaders.RequestGroupId(HttpContext) };

        [HttpPost]
        public async ValueTask<IActionResult> UpdateSensorInfo(SensorInfoViewModel newModel)
        {
            if (!_tree.TryGetManagedSensor(CurrentUser, SensorPathHelper.DecodeGuid(newModel.EncodedId), out var sensor))
                return _emptyResult;

            if (!ModelState.IsValid)
            {
                var invalidModel = new SensorInfoViewModel(sensor);
                PopulateAlertSchedules(invalidModel);
                return PartialView("_MetaInfo", invalidModel);
            }

            var availableChats = sensor.GetAvailableChats(_chatsManager);

            newModel.DataAlerts.TryGetValue(TimeToLiveAlertViewModel.AlertKey, out var ttlAlertList);
            var policyUpdates = newModel.DataAlerts.TryGetValue((byte)sensor.Type, out var list)
                ? list.Select(a => a.ToUpdate(availableChats)).ToList() : [];


            var ttlPolicies = ttlAlertList?.Select(t =>
            {
                var interval = t.Conditions is { Count: > 0 } ? t.Conditions[0].TimeToLive : null;
                var fromParent = interval?.TimeInterval.IsParent() ?? false;
                return t.ToTimeToLiveUpdate(CurrentInitiator, availableChats) with
                {
                    TTL = fromParent ? null : interval?.ToModel()?.Ticks
                };
            }).ToList() ?? [];

            // #1588 single write path: the policy lists ride the same
            // administration service (write gates, authorization seam, undo
            // journaling) the REST/MCP writes use; the settings stay a
            // settings-only cache update. The undo group of the service
            // writes is this post's request group.
            var policiesResult = await _policies.WriteSensorListsAsync(sensor.Id, policyUpdates, ttlPolicies,
                User, JournalWriteOptions(), new CookieUserAccess(CurrentUser, _treeValuesCache));

            if (!policiesResult.Success)
            {
                ModelState.AddModelError(string.Empty,
                    policiesResult.Failure.Message ?? "The policy lists could not be saved.");

                // Re-render the SUBMITTED form (#1588 review round 4): the
                // save is two steps now, and rebuilding from the live sensor
                // would silently discard the user's description/unit/period
                // edits along with the unsaved policy lists.
                PopulateAlertSchedules(newModel);

                return PartialView("_MetaInfo", newModel);
            }

            var update = new SensorUpdate
            {
                Id = sensor.Id,
                Description = newModel.Description ?? string.Empty,
                KeepHistory = newModel.SavedHistoryPeriod.ToModel(),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(),
                SelectedUnit = newModel.SelectedUnit,
                AggregateValues = newModel.AggregateValues,
                Statistics = newModel.GetOptions(),
                Initiator = CurrentInitiator,
                DisplayUnit = newModel.DisplayUnit
            };

            await _treeValuesCache.UpdateSensorAsync(update);

            var model = new SensorInfoViewModel(sensor);
            var schedulesList = GetAlertSchedulesSelectList();
            foreach (var (key, dataAlerts) in model.DataAlerts)
                foreach(var alert in dataAlerts)
                    alert.Schedules = schedulesList;

            return PartialView("_MetaInfo", model);
        }

        
        [HttpPost]
        public async ValueTask<IActionResult> UpdateSensorDisplayUnit(string encodedId, RateDisplayUnit displayUnit)
        {
            // An unknown id was a silent no-op answered with 200; an id the user does not manage answers the same.
            if (!_tree.TryGetManagedSensor(CurrentUser, SensorPathHelper.DecodeGuid(encodedId), out var sensor))
                return Ok();

            var update = new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = CurrentInitiator,
                DisplayUnit = displayUnit
            };

            await _treeValuesCache.UpdateSensorAsync(update);

            return Ok();
        }
        

        public IActionResult AddDataPolicy(byte type, Guid entityId, Guid? folderId = null)
        {
            // entityId may be a template id (Alert Template authoring) rather than a sensor
            // id — in that case sensor is null and BuildAlert produces a default block.
            _tree.TryGetSensor(CurrentUser, entityId, out var sensor);

            // A table sensor has no alerts (#1844); neither has any other type without an alert block.
            if (DataAlertViewModel.BuildAlert(type, sensor) is not { } viewModel)
                return BadRequest(NoAlertsFor(type));

            viewModel.Schedules = GetAlertSchedulesSelectList();

            if (sensor is null && folderId.HasValue && _tree.TryGetFolder(CurrentUser, folderId.Value, out var folder))
            {
                folder.TryGetChats(out var chats);
                foreach (var action in viewModel.Actions)
                    action.AvailableChats.UnionWith(chats);
            }

            return PartialView("~/Views/Home/Alerts/_DataAlert.cshtml", viewModel);
        }

        public IActionResult AddAlertCondition(byte type)
        {
            if (BuildAlertCondition(type) is not { } condition)
                return BadRequest(NoAlertsFor(type));

            return PartialView("~/Views/Home/Alerts/_ConditionBlock.cshtml", condition);
        }

        private static string NoAlertsFor(byte type) =>
            type == (byte)SensorType.Table ? "Table sensors have no alerts." : $"Sensors of type {type} have no alerts.";

        public IActionResult AddAlertAction(Guid entityId, bool isMain, bool isTtl, Guid? folderId = null)
        {
            HashSet<Guid> chats = [];
            if (_tree.TryGetSensor(CurrentUser, entityId, out var sensor))
                sensor.TryGetChats(out chats);
            else if (folderId.HasValue && _tree.TryGetFolder(CurrentUser, folderId.Value, out var folder))
                folder.TryGetChats(out chats);

            return PartialView("~/Views/Home/Alerts/_ActionBlock.cshtml", new ActionViewModel(isMain, isTtl, chats) { Icon = ActionViewModel.DefaultIcon });
        }

        public IActionResult GetOperation(byte type, AlertProperty property)
        {
            if (BuildAlertCondition(type) is not { } condition)
                return BadRequest(NoAlertsFor(type));

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
                // Any-type templates have no concrete sensor type — they route TTL/ConfirmationPeriod
                // alerts through AlertKey (byte.MaxValue), so build a TTL condition (#1249).
                TimeToLiveAlertViewModel.AlertKey => new TimeToLiveConditionViewModel(false),
                _ => null,
            };


        [HttpPost]
        public IActionResult GetTestToastMessage(AlertMessageViewModel alert)
        {
            if (!_tree.TryGetSensor(CurrentUser, alert.EntityId, out _))
                return _emptyResult;

            var sensorModel = _treeValuesCache.GetSensor(alert.EntityId);

            var message = alert.BuildToastMessage(sensorModel);

            return Json(MarkdownHelper.ToHtml(message));
        }


        [HttpGet]
        public IActionResult GetSensorEditModal(Guid sensorId)
        {
            // The form of a Manager's write ("Edit last value"). An unknown id and one the user does not
            // manage answer the same empty result.
            if (!_tree.TryGetManagedSensor(CurrentUser, sensorId, out var sensorNodeViewModel))
                return _emptyResult;

            var isAccessKeyExist =
                GetKeyOrDefaultWithPermissions(sensorNodeViewModel.RootProduct.Id, KeyPermissions.CanSendSensorData) is not null;

            if (!isAccessKeyExist)
                ModelState.AddModelError(nameof(EditSensorStatusViewModal.RootProductId), EditSensorStatusViewModal.AccessKeyValidationErrorMessage);

            return PartialView("~/Views/Tree/_EditSensorStatusModal.cshtml", new EditSensorStatusViewModal(sensorNodeViewModel, isAccessKeyExist));
        }

        [HttpPost]
        public async ValueTask<IActionResult> UpdateSensorStatus(EditSensorStatusViewModal modal)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // The sensor comes from the tree, not from the posted form: a sensor the user does not manage is
            // not found, and the product whose key allows the write is the sensor's own.
            if (!_tree.TryGetManagedSensor(CurrentUser, modal.SensorId, out var sensor))
                return NotFound();

            var key = GetKeyOrDefaultWithPermissions(sensor.RootProduct.Id, KeyPermissions.CanSendSensorData)?.Id;

            if (key is null)
            {
                ModelState.AddModelError(nameof(EditSensorStatusViewModal.RootProductId), EditSensorStatusViewModal.AccessKeyValidationErrorMessage);
                return BadRequest(ModelState);
            }

            // A table sensor has no values to edit (#1844).
            if (sensor.IsTableSensor)
                return BadRequest("Table sensors have no values to edit.");

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
            if (!_tree.TryGetManagedSensor(CurrentUser, sensorId, out var sensor))
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
            if (!_tree.TryGetNode(CurrentUser, SensorPathHelper.DecodeGuid(encodedId), out var product))
                return _emptyResult;

            var model = new ProductInfoViewModel(product);
            PopulateAlertSchedules(model);
            return PartialView("_MetaInfo", model);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProductInfo(ProductInfoViewModel newModel)
        {
            if (!_tree.TryGetManagedNode(CurrentUser, SensorPathHelper.DecodeGuid(newModel.EncodedId), out var product))
                return _emptyResult;

            if (!ModelState.IsValid)
            {
                var invalidModel = new ProductInfoViewModel(product);
                PopulateAlertSchedules(invalidModel);
                return PartialView("_MetaInfo", invalidModel);
            }

            var update = new ProductUpdate
            {
                Id = product.Id,
                TTL = newModel.ExpectedUpdateInterval.ToModel(product.TTL),
                DefaultChats = newModel.DefaultChats?.ToUpdate(product, _chatsManager),
                KeepHistory = newModel.SavedHistoryPeriod.ToModel(product.KeepHistory),
                SelfDestroy = newModel.SelfDestroyPeriod.ToModel(product.SelfDestroy),
                Description = newModel.Description ?? string.Empty,
                Initiator = CurrentInitiator
            };

            await _treeValuesCache.UpdateProductAsync(update);

            var model = new ProductInfoViewModel(product.RecalculateCharacteristics());
            PopulateAlertSchedules(model);
            return PartialView("_MetaInfo", model);
        }

        [HttpGet]
        public IActionResult GetFolderInfo(string id)
        {
            return _tree.TryGetFolder(CurrentUser, SensorPathHelper.DecodeGuid(id), out var folder)
                ? PartialView("_MetaInfo", FolderInfo(folder))
                : _emptyResult;
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFolderInfo(FolderInfoViewModel newModel)
        {
            // A folder's settings are inherited by every product in it, so they are the folder Manager's to
            // change (the rule EditAlerts applies to a folder): seeing the folder through one of its
            // products is not enough, and the answer is the one of an unknown id.
            if (!_tree.TryGetManagedFolder(CurrentUser, newModel.EncodedId.ToGuid(), out var folder))
                return _emptyResult;

            if (!ModelState.IsValid)
                return PartialView("_MetaInfo", FolderInfo(folder));

            var update = new FolderUpdate
            {
                Id = folder.Id,
                Description = newModel.Description ?? string.Empty,
                TTL = newModel.ExpectedUpdateInterval,
                KeepHistory = newModel.SavedHistoryPeriod,
                SelfDestroy = newModel.SelfDestroyPeriod,
                DefaultChats = newModel.DefaultChats,
                Initiator = CurrentInitiator,
            };

            return await _folderManager.TryUpdate(update)
                ? PartialView("_MetaInfo", FolderInfo(_folderManager[update.Id]))
                : _emptyResult;
        }

        // Server-wide database maintenance, offered on the admin Configuration page only.
        [HttpGet]
        [AuthorizeIsAdmin]
        public JsonResult Compact()
        {
            if (_database.IsCompactRunning)
                return Json(JsonSerializer.Serialize(new { Status = "Error", Error = "A compaction or a history sealing step is already running" }));

            _database.Compact();

            return Json(JsonSerializer.Serialize(new { Status = "Ok", Result = Math.Round(_database.TotalDbSize / (double)(1 << 20), 2, MidpointRounding.AwayFromZero) }));
        }

        // Turns a sealed week back into rows (#1804, ADR-0020 decision 10); the work and its rules are the database core's.
        [HttpPost]
        [AuthorizeIsAdmin]
        [ValidateAntiForgeryToken]
        public JsonResult Unseal(string name)
        {
            var report = _database.UnsealHistoryWeek(name, DateTime.UtcNow);

            return report.Outcome == HistorySealingOutcome.Done
                ? Json(JsonSerializer.Serialize(new { Status = "Ok", Result = report.ToString() }))
                : Json(JsonSerializer.Serialize(new { Status = "Error", Error = report.ToString() }));
        }

        // Server-wide database maintenance, offered on the admin Configuration page only.
        [HttpGet]
        [AuthorizeIsAdmin]
        public JsonResult Export(string name)
        {
            if (_database.IsExportRunning)
                return Json(JsonSerializer.Serialize(new { Status = "Error", Error = "Export already running" }));

            _database.ExportValuesDatabase(name, _treeValuesCache.GetSensors().ToDictionary(x => x.Id, x => x.FullPath));

            return Json(JsonSerializer.Serialize(new { Status = "Ok" }));
        }

        // The folder panel for the current user: its statistics cover the products this user sees.
        private FolderInfoViewModel FolderInfo(FolderModel folder) =>
            new(folder, _tree.GetVisibleProducts(CurrentUser, folder).Values);

        private bool TryGetSensor(string encodedId, out SensorNodeViewModel sensor) =>
            _tree.TryGetSensor(CurrentUser, SensorPathHelper.DecodeGuid(encodedId), out sensor);

        // GetGrid and GetList page through the node or folder connected in the user's state by SelectNode.
        // That state outlives a role change, so the selection is checked again on every page request, and a
        // folder's product list is taken anew: it holds the products the user sees NOW.
        private bool IsSelectedNodeVisible()
        {
            if (StoredUser.SelectedNode.Id is not { } selected)
                return false;

            var selectedId = selected.ToGuid();

            if (_tree.TryGetFolder(CurrentUser, selectedId, out var folder))
            {
                StoredUser.SelectedNode.RefreshFolderProducts(folder.Id, _tree.GetVisibleProducts(CurrentUser, folder));
                return true;
            }

            return _tree.IsVisible(CurrentUser, selectedId);
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

        private List<SelectListItem> GetAlertSchedulesSelectList()
        {
            return [.. _alertScheduleProvider.GetAllSchedules().Select(tz => new SelectListItem
            {
                Value = tz.Id.ToString(),
                Text = $"{tz.Name}"
            })];
        }

        private void PopulateAlertSchedules(NodeInfoBaseViewModel model)
        {
            var schedulesList = GetAlertSchedulesSelectList();
            foreach (var (_, alerts) in model.DataAlerts)
                foreach (var alert in alerts)
                    alert.Schedules = schedulesList;
        }
    }
}
