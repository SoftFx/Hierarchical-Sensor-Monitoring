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
        private readonly TreeViewModel _tree;


        static AlertsController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertsController(ITelegramChatsManager telegram, ITreeValuesCache cache, IUserManager users) : base(users)
        {
            _telegram = telegram;
            _cache = cache;
        }


        [HttpGet]
        public IActionResult ExportAlerts(Guid selectedId)
        {
            var node = _cache.GetProduct(selectedId);

            if (node is null)
                return _emptyResult;

            var exportModel = node.Policies.SaveStateToExportGroup(new PolicyExportGroup(), string.Empty);
            var relativeNodes = new LinkedList<string>();

            void RunDfsLoad(ProductModel curNode)
            {
                foreach (var (_, subNode) in curNode.SubProducts)
                {
                    relativeNodes.AddLast(subNode.DisplayName);

                    subNode.Policies.SaveStateToExportGroup(exportModel, string.Join('/', relativeNodes));

                    RunDfsLoad(subNode);

                    relativeNodes.RemoveLast();
                }
            }

            RunDfsLoad(node);

            var fileName = $"{node.FullPath.Replace('/', '_')}-alerts.json";
            var content = JsonSerializer.SerializeToUtf8Bytes(exportModel.Select(p => new AlertExportViewModel(p.Value)), _serializeOptions);

            Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");

            return File(content, fileName.GetContentType(), fileName);
        }


        [HttpPost]
        public string ImportAlerts([FromBody] AlertImportViewModel model)
        {
            var toastViewModel = new ImportAlertsToastViewModel();

            if (_tree.Nodes.TryGetValue(model.NodeId, out var targetNode))
            {
                try
                {
                    var importList = JsonSerializer.Deserialize<List<AlertExportViewModel>>(model.FileContent, _deserializeOptions);

                    var availableChats = targetNode.GetAvailableChats(_telegram).ToDictionary(k => k.Value, v => v.Key);
                    var productName = targetNode.RootProduct.Name;

                    var newSensorAlerts = new CGuidDict<List<PolicyUpdate>>();

                    foreach (var importModel in importList)
                        foreach (var sensorPath in importModel.Sensors)
                        {
                            var fullSensorPath = $"{targetNode.Path}/{sensorPath}";

                            if (_cache.TryGetSensorByPath(productName, fullSensorPath, out var sensor))
                            {
                                var newAlert = importModel.ToUpdate(sensor.Id, availableChats);

                                newSensorAlerts[sensor.Id].Add(newAlert);
                            }
                            else
                                toastViewModel.AddError("Sensor by path not found", fullSensorPath);
                        }

                    foreach (var (sensorId, alertUpdates) in newSensorAlerts)
                    {
                        var update = new SensorUpdate()
                        {
                            Id = sensorId,
                            Policies = alertUpdates,
                            Initiator = CurrentInitiator,
                        };

                        if (!_cache.TryUpdateSensor(update, out var error))
                            toastViewModel.AddError(error, _tree.Sensors[sensorId].Name);
                    }
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }

            return toastViewModel.ToResponse();
        }
    }
}