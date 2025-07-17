using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Folders;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;



namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertTemplatesController : BaseController
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


        static AlertTemplatesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertTemplatesController(ITelegramChatsManager telegram, IFolderManager folders, TreeViewModel tree, ITreeValuesCache cache, IUserManager users) : base(users)
        {
            _telegram = telegram;
            _folders = folders;
            _cache = cache;
            _tree = tree;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var templates = _cache.GetAlertTemplateModels() ?? [];

            var result = new DataAlertTemplateTableViewModel()
            {
                Keys = templates.Select(x =>
                {
                    var (type, sensors) = GetAffectedSensors(x.SensorType, x.Path, x.FolderId);
                    return new DataAlertTemplateViewModel(x) { Sensors = sensors };
                }).ToList(),
            };

            return View(result);
        }

        private class UpdateResponse
        {
            private const int MAX_SENSORS = 4;

            public byte? Type { get; set; }
            public string Sensors { get; set; }

            public string Name { get; set; }

            public UpdateResponse(byte? type, List<BaseSensorModel> sensors, string name)
            {
                Type = type;
                Name = name;

                if (sensors.Count > 0)
                {
                    var sb = new StringBuilder(128);
                    for (int i = 0; i < sensors.Count; i++)
                    {
                        if (i == MAX_SENSORS && sensors.Count > MAX_SENSORS + 1)
                        {
                            sb.Append($"<div class=\"d-flex flex-row align-items-center fullCondition\">... and other {sensors.Count - MAX_SENSORS}</div>");
                            break;
                        }
                        sb.Append($"<div class=\"d-flex flex-row align-items-center fullCondition\">{sensors[i].FullPath}</div>");
                    }

                    Sensors = sb.ToString();
                }
                else
                    Sensors = null;
            }
        }

        [HttpGet]
        public IActionResult UpdateTemplate(byte type, string path, Guid folderId)
        {
            var (sensorType, sensors) = GetAffectedSensors(type, path, folderId);

            var name = GetTemplateName(path, folderId);

            var response = new UpdateResponse(sensorType, sensors, name);

            return Json(JsonSerializer.Serialize(response));
        }


        [HttpGet]
        public IActionResult New(Guid? id = null)
        {
            var folders = _folders.GetUserFolders(CurrentUser);
            var model = new DataAlertTemplateViewModel(folders);

            if (folders.Count == 0)
                return Json(model);

            if (id.HasValue)
            {
                var sensor = _cache.GetSensor(id.Value);
                if (sensor != null)
                {
                    model.FolderId = sensor.Root.FolderId ?? folders.FirstOrDefault().Id;
                    model.PathTemplate = $"*/{sensor.Path}";
                    model.Type = (byte)sensor.Type;
                    model.Name = GetTemplateName(sensor.Path, model.FolderId);
                }
            }

            return View("AlertTemplate", model);
        }

        [HttpGet]
        public IActionResult Edit(Guid id)
        {
            var data = _cache.GetAlertTemplate(id);

            if (data is null)
                return _emptyResult;

            return View("AlertTemplate", new DataAlertTemplateViewModel(data, _folders.GetUserFolders(CurrentUser)));
        }

        [HttpPost]
        public IActionResult AlertTemplate(DataAlertTemplateViewModel data)
        {
            if (_cache.GetAlertTemplateModels().Any(x => x.Name == data.Name && x.Id != data.Id))
                ModelState.AddModelError(nameof(data.Name), "The name must be unique.");

            if (ModelState.IsValid)
            {
                var model = data.ToModel();
                _cache.AddAlertTemplate(model);
                return Ok();
            }

            data = new DataAlertTemplateViewModel(data.ToModel(), _folders.GetUserFolders(CurrentUser));

            return PartialView("_AlertTemplate", data);
        }

        [HttpGet]
        public IActionResult Remove(Guid id)
        {
            _cache.RemoveAlertTemplate(id);

            return RedirectToAction("Index");
        }


        private (byte?, List<BaseSensorModel>) GetAffectedSensors(byte type, string path, Guid folder)
        {
            byte? sensorType = null;

            var sensors = _cache.GetSensors(path, type == DataAlertTemplateViewModel.AnyType ? null : (SensorType)type, folder);

            if (sensors.Count > 0)
            {
                sensorType = (byte)sensors.FirstOrDefault()?.Type;
                sensors = sensors.Where(x => x.Type == (SensorType)sensorType).ToList();
            }

            return (sensorType, sensors);
        }

        private string GetTemplateName(string path, Guid folderId)
        {
            var folderName = _folders.GetUserFolders(CurrentUser).FirstOrDefault(x => x.Id == folderId)?.Name ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var result = path.Split('/');

            return $"{folderName}/{result[^1]}";

        }

    }
}