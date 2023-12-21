using HSMCommon.Collections;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.MultiToastViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        private readonly ITelegramChatsManager _telegram;
        private readonly ITreeValuesCache _cache;
        private readonly IFolderManager _folders;
        private readonly TreeViewModel _tree;


        static AlertsController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertsController(ITelegramChatsManager telegram, IFolderManager folders, TreeViewModel tree, ITreeValuesCache cache, IUserManager users) : base(users)
        {
            _telegram = telegram;
            _folders = folders;
            _cache = cache;
            _tree = tree;
        }


        [HttpGet]
        public IActionResult ExportFolderAlerts(Guid folderId)
        {
            if (_folders.TryGetValue(folderId, out var folder))
            {
                var exportGroup = new PolicyExportGroup();

                foreach (var (productId, product) in folder.Products)
                    exportGroup = AddNodeAlertsToGroup(exportGroup.SetProduct(product.Name), productId);

                return ExportModelToFile(folder.Name, exportGroup);
            }
            else
                return _emptyResult;
        }

        [HttpGet]
        public IActionResult ExportAlerts(Guid selectedId)
        {
            var node = _cache.GetProduct(selectedId);

            return node is null ? _emptyResult : ExportModelToFile(node.FullPath, AddNodeAlertsToGroup(new PolicyExportGroup(), node.Id));
        }


        [HttpPost]
        public string ImportAlerts([FromBody] AlertImportViewModel model)
        {
            var toast = new ImportAlertsToastViewModel();
            var targetId = _tree.Nodes.TryGetValue(model.NodeId, out var target) ? target.Id : Guid.Empty;

            try
            {
                var importList = JsonSerializer.Deserialize<List<AlertExportViewModel>>(model.FileContent, _deserializeOptions);
                var newAlerts = new CGuidDict<List<PolicyUpdate>>();

                foreach (var importGroup in importList)
                {
                    if (importGroup.Products is null)
                        ImportAlertsToProduct(targetId, importGroup, newAlerts, toast);
                    else
                    {
                        foreach (var productName in importGroup.Products)
                            if (_cache.TryGetProductByName(productName, out var product))
                                ImportAlertsToProduct(product.Id, importGroup, newAlerts, toast);
                    }
                }

                SendAlertUpdates(newAlerts, toast);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            return toast.ToResponse();
        }


        private void ImportAlertsToProduct(Guid nodeId, AlertExportViewModel importGroup, CGuidDict<List<PolicyUpdate>> newAlerts, ImportAlertsToastViewModel toast)
        {
            if (_tree.Nodes.TryGetValue(nodeId, out var targetNode))
            {
                var availableChats = targetNode.GetAvailableChats(_telegram).ToDictionary(k => k.Value, v => v.Key);
                var productName = targetNode.RootProduct.Name;

                foreach (var sensorPath in importGroup.Sensors)
                {
                    var fullSensorPath = $"{targetNode.Path}/{sensorPath}";

                    if (_cache.TryGetSensorByPath(productName, fullSensorPath, out var sensor))
                    {
                        var newAlert = importGroup.ToUpdate(sensor.Id, availableChats);

                        newAlerts[sensor.Id].Add(newAlert);
                    }
                    else
                        toast.AddError("Sensor by path not found", $"{productName}{fullSensorPath}");
                }
            }
        }

        private void SendAlertUpdates(CGuidDict<List<PolicyUpdate>> newAlerts, ImportAlertsToastViewModel toast)
        {
            foreach (var (sensorId, alertUpdates) in newAlerts)
            {
                var update = new SensorUpdate()
                {
                    Id = sensorId,
                    Policies = alertUpdates,
                    Initiator = CurrentInitiator,
                };

                if (!_cache.TryUpdateSensor(update, out var error))
                    toast.AddError(error, _tree.Sensors[sensorId].Name);
            }
        }


        private PolicyExportGroup AddNodeAlertsToGroup(PolicyExportGroup exportGroup, Guid nodeId)
        {
            var renderedSensors = CurrentUser.Tree.SearchedSensors;
            var node = _cache.GetProduct(nodeId);

            if (node is not null)
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
            var chats = _telegram.GetValues().ToDictionary(ch => ch.Id, ch => ch.Name);

            var fileName = $"{selectedNodePath.Replace('/', '_')}-alerts.json";
            var content = JsonSerializer.SerializeToUtf8Bytes(group.SelectMany(p => p.Value.Select(info => (p.Key, info)))
                                                                   .GroupBy(g => (g.info.ProductName, g.Key))
                                                                   .Select(p => new AlertExportViewModel(p.Select(v => v.info), chats)), _serializeOptions);

            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            return File(content, fileName.GetContentType(), fileName);
        }
    }
}