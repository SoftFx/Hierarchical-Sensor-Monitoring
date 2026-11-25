using HSMCommon.Collections;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.MultiToastViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;

namespace HSMServer.Controllers
{

    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertsController : BaseController
    {
        private static readonly JsonSerializerOptions _serializeOptions = new()
        {
            WriteIndented = true,
        };

        private static readonly JsonSerializerOptions _deserializeOptions = new()
        {
            AllowTrailingCommas = true,
        };

        private readonly IChatsManager _chats;
        private readonly ITreeValuesCache _cache;
        // The export and the import are offered by the tree to a node's and a folder's Managers. Every id
        // from the client is resolved through the Managed lookups, which answer "not found" for an
        // entity the user does not manage; the controller holds no reference to the global tree.
        private readonly UserTreeAccess _tree;
        private readonly PolicyAdministrationService _policies;


        static AlertsController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertsController(IChatsManager chats, UserTreeAccess tree, ITreeValuesCache cache,
            IUserManager users, PolicyAdministrationService policies) : base(users)
        {
            _chats = chats;
            _cache = cache;
            _tree = tree;
            _policies = policies;
        }


        [HttpGet]
        public IActionResult ExportFolderAlerts(Guid folderId)
        {
            if (_tree.TryGetManagedFolder(CurrentUser, folderId, out var folder))
            {
                var exportGroup = new PolicyExportGroup();

                // The folder Manager role does not imply the Manager role on every product in it.
                foreach (var (productId, product) in _tree.GetManagedProducts(CurrentUser, folder))
                    exportGroup = AddNodeAlertsToGroup(exportGroup.SetProduct(product.Name), productId);

                return ExportModelToFile(folder.Name, exportGroup);
            }
            else
                return _emptyResult;
        }

        [HttpGet]
        public IActionResult ExportAlerts(Guid selectedId)
        {
            if (_tree.TryGetManagedNode(CurrentUser, selectedId, out _) && _cache.TryGetProduct(selectedId, out var product))
                return ExportModelToFile(product.FullPath, AddNodeAlertsToGroup(new PolicyExportGroup(), product.Id));
            
            return _emptyResult;
        }


        [HttpPost]
        public async ValueTask<string> ImportAlerts([FromBody] AlertImportViewModel model)
        {
            var toast = new ImportAlertsToastViewModel();

            // A node the user does not manage is no target, like an unknown id: nothing is looked up in
            // it, so the answer says nothing about what it holds.
            _tree.TryGetManagedNode(CurrentUser, model.NodeId, out var target);

            try
            {
                var importList = JsonSerializer.Deserialize<List<AlertExportViewModel>>(model.FileContent, _deserializeOptions);
                var newAlerts = new CDict<List<PolicyUpdate>>();
                var sensorNames = new Dictionary<Guid, string>();

                foreach (var importGroup in importList)
                {
                    if (importGroup.Products is null)
                        ImportAlertsToProduct(target, importGroup, newAlerts, sensorNames, toast);
                    else
                    {
                        // The names of a file are looked up over the whole server: a product the user
                        // does not manage is passed over like a name no product has.
                        foreach (var productName in importGroup.Products)
                            if (_cache.TryGetProductByName(productName, out var product) &&
                                _tree.TryGetManagedNode(CurrentUser, product.Id, out var namedProduct))
                                ImportAlertsToProduct(namedProduct, importGroup, newAlerts, sensorNames, toast);
                    }
                }

                await SendAlertUpdatesAsync(newAlerts, sensorNames, toast);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            return toast.ToResponse();
        }


        // targetNode is a node the user manages, or null when the import has no target.
        private void ImportAlertsToProduct(ProductNodeViewModel targetNode, AlertExportViewModel importGroup,
            CDict<List<PolicyUpdate>> newAlerts, Dictionary<Guid, string> sensorNames, ImportAlertsToastViewModel toast)
        {
            if (targetNode is null)
                return;

            var availableChats = targetNode.GetAvailableChats(_chats)
                .ToDictionary(k => k.Value, v => v.Key);
            var productId = targetNode.RootProduct.Id;

            foreach (var sensorPath in importGroup.Sensors)
            {
                // A root product has an empty path of its own: its sensors are indexed by their bare path.
                var fullSensorPath = string.IsNullOrEmpty(targetNode.Path) ? sensorPath : $"{targetNode.Path}/{sensorPath}";

                if (_cache.TryGetSensorByPath(productId, fullSensorPath, out var sensor))
                {
                    var newAlert = importGroup.ToUpdate(sensor.Id, availableChats);

                    newAlerts[sensor.Id].Add(newAlert);
                    sensorNames[sensor.Id] = sensor.DisplayName;
                }
                else
                    toast.AddError("Sensor by path not found", $"{productId}/{fullSensorPath}");
            }
        }

        // #1588 single write path: the import rides the policy
        // administration service — the same write gates, authorization and
        // undo journaling as every other configuration write (one import
        // request = one undo group).
        private async ValueTask SendAlertUpdatesAsync(CDict<List<PolicyUpdate>> newAlerts, Dictionary<Guid, string> sensorNames,
            ImportAlertsToastViewModel toast)
        {
            var write = new ChangeSetWriteOptions { ChangeSetId = ChangeSetHeaders.RequestGroupId(HttpContext) };

            foreach (var (sensorId, alertUpdates) in newAlerts)
            {
                try
                {
                    var result = await _policies.WriteSensorListsAsync(sensorId, alertUpdates, ttlPolicies: null,
                        User, write, new CookieUserAccess(CurrentUser, _cache));

                    if (!result.Success)
                        toast.AddError(result.Failure.Message ?? "The import was rejected.", sensorNames[sensorId]);
                }
                catch (Exception ex)
                {
                    toast.AddError(ex.Message, sensorNames[sensorId]);
                }
            }
        }


        private PolicyExportGroup AddNodeAlertsToGroup(PolicyExportGroup exportGroup, Guid nodeId)
        {
            var renderedSensors = CurrentUser.Tree.SearchedSensors;

            if (_cache.TryGetProduct(nodeId, out var node))
            {
                var relativeNodes = new LinkedList<string>();

                exportGroup = node.Policies.SaveStateToExportGroup(exportGroup, string.Empty, renderedSensors.IsRendered);

                void RunDfsLoad(ProductModel curNode)
                {
                    foreach (var (_, subNode) in curNode.SubProducts)
                    {
                        relativeNodes.AddLast(subNode.DisplayName);

                        subNode.Policies.SaveStateToExportGroup(exportGroup, string.Join('/', relativeNodes), renderedSensors.IsRendered);

                        RunDfsLoad(subNode);

                        relativeNodes.RemoveLast();
                    }
                }

                RunDfsLoad(node);
            }

            return exportGroup;
        }

        private FileContentResult ExportModelToFile(string selectedNodePath, PolicyExportGroup group)
        {
            var availableChats = _chats.GetValues().ToDictionary(ch => ch.Id, ch => ch.Name);

            var fileName = $"{selectedNodePath.Replace('/', '_')}-alerts.json";
            var content = JsonSerializer.SerializeToUtf8Bytes(group.SelectMany(p => p.Value.Select(info => (p.Key, info)))
                                                                   .Where(x => x.info.Policy.TemplateId == null)
                                                                   .GroupBy(g => (g.info.ProductName, g.Key))
                                                                   .Select(p => new AlertExportViewModel(p.Select(v => v.info), availableChats)), _serializeOptions);

            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            return File(content, fileName.GetContentType(), fileName);
        }
    }
}