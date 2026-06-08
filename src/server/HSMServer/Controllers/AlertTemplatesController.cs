using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Schedule;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;



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
        private readonly IAlertScheduleProvider _alertScheduleProvider;


        static AlertTemplatesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertTemplatesController(ITelegramChatsManager telegram, IFolderManager folders, TreeViewModel tree, ITreeValuesCache cache,
                                        IUserManager users, IAlertScheduleProvider provider) : base(users)
        {
            _telegram = telegram;
            _folders = folders;
            _cache = cache;
            _tree = tree;
            _alertScheduleProvider = provider;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var templates = _cache.GetAlertTemplateModels() ?? [];

            var result = new DataAlertTemplateTableViewModel()
            {
                Keys = templates.Select(x =>
                {
                    var (type, sensors) = GetAffectedSensors(x.SensorType, x.Paths, x.FolderId);
                    return new DataAlertTemplateViewModel(x) { Sensors = sensors };
                }).ToList(),
            };

            return View(result);
        }

        private sealed record ChatItem(Guid Id, string Name, byte Type);

        private class UpdateResponse
        {
            private const int MAX_SENSORS = 4;

            public byte? Type { get; set; }
            public string Sensors { get; set; }
            public string Name { get; set; }
            public List<ChatItem> Chats { get; set; }

            public UpdateResponse(byte? type, List<BaseSensorModel> sensors, string name, List<ChatItem> chats)
            {
                Type = type;
                Name = name;
                Chats = chats;

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
        public IActionResult UpdateTemplate(byte type, string paths, Guid folderId)
        {
            List<string> pathList = [];
            if (!string.IsNullOrWhiteSpace(paths))
            {
                try { pathList = JsonSerializer.Deserialize<List<string>>(paths)?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? []; }
                catch (JsonException) { }
            }

            var (sensorType, sensors) = GetAffectedSensors(type, pathList, folderId);

            var name = GetTemplateName(pathList.FirstOrDefault(), folderId);

            List<ChatItem> chats = [];
            if (_folders.TryGetValue(folderId, out var folder))
            {
                chats = _telegram.GetValues()
                    .Where(c => folder.TelegramChats.Contains(c.Id))
                    .Select(c => new ChatItem(c.Id, c.Name, (byte)c.Type))
                    .ToList();
            }

            var response = new UpdateResponse(sensorType, sensors, name, chats);

            return Json(JsonSerializer.Serialize(response));
        }


        [HttpGet]
        public IActionResult New(Guid? id = null)
        {
            var folders = _folders.GetUserFolders(CurrentUser);
            var model = new DataAlertTemplateViewModel(folders);

            if (folders.Count == 0)
                return Json("User does not belong to any folders");

            if (id.HasValue)
            {
                var sensor = _cache.GetSensor(id.Value);
                if (sensor != null)
                {
                    model.FolderId = sensor.Root.FolderId ?? folders.FirstOrDefault().Id;
                    model.PathTemplates = [$"*/{sensor.Path}"];
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

            var model = new DataAlertTemplateViewModel(data, _folders.GetUserFolders(CurrentUser));

            if (_folders.TryGetValue(data.FolderId, out var folder))
                PopulateAvailableChats(model, folder.TelegramChats);

            foreach (var (_, alerts) in model.DataAlerts)
                foreach (var alert in alerts)
                    alert.Schedules = GetAlertSchedulesSelectList();

            return View("AlertTemplate", model);
        }

        [HttpPost]
        public async ValueTask<IActionResult> AlertTemplate(DataAlertTemplateViewModel data)
        {
            if (_cache.GetAlertTemplateModels().Any(x => x.Name == data.Name && x.Id != data.Id))
                ModelState.AddModelError(nameof(data.Name), "The name must be unique.");

            if (data.PathTemplates == null || data.PathTemplates.All(string.IsNullOrWhiteSpace))
                ModelState.AddModelError(nameof(data.PathTemplates), "At least one path template is required.");

            Dictionary<Guid, string> availableChats = null;
            if (_folders.TryGetValue(data.FolderId, out var folder) && folder.TelegramChats.Count > 0)
                availableChats = folder.TelegramChats.GetAvailableChatsDictionary(_telegram);

            AlertTemplateModel model = null;

            if (ModelState.IsValid)
            {
                model = data.ToModel(availableChats);

                if (!model.TryApplyPathTemplates(out var pathError))
                    ModelState.AddModelError(nameof(data.PathTemplates), $"Invalid path template: {pathError}");
            }

            if (ModelState.IsValid)
            {
                var (success, error) = await _cache.AddAlertTemplateAsync(model);

                if (!success)
                    return Json(new { success = false, error = error });

                return Ok();
            }

            model ??= data.ToModel(availableChats);
            data = new DataAlertTemplateViewModel(model, _folders.GetUserFolders(CurrentUser));

            if (folder != null)
                PopulateAvailableChats(data, folder.TelegramChats);

            foreach (var (_, alerts) in data.DataAlerts)
                foreach (var alert in alerts)
                    alert.Schedules = GetAlertSchedulesSelectList();

            return PartialView("_AlertTemplate", data);
        }

        [HttpGet]
        public async ValueTask<IActionResult> Remove(Guid id)
        {
            await _cache.RemoveAlertTemplateAsync(id);

            return RedirectToAction("Index");
        }


        private (byte?, List<BaseSensorModel>) GetAffectedSensors(byte type, List<string> paths, Guid folder)
        {
            var allSensors = new Dictionary<Guid, BaseSensorModel>();

            foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                foreach (var sensor in _cache.GetSensors(path, type == DataAlertTemplateViewModel.AnyType ? null : (SensorType)type, folder))
                    allSensors.TryAdd(sensor.Id, sensor);
            }

            var sensors = allSensors.Values.ToList();

            if (sensors.Count == 0)
                return (null, sensors);

            if (type == DataAlertTemplateViewModel.AnyType)
            {
                // For "Any" type, show all matching sensors regardless of type.
                // Auto-detect: if all sensors are the same type, return it; otherwise keep as Any.
                var distinctTypes = sensors.Select(x => x.Type).Distinct().ToList();
                byte? sensorType = distinctTypes.Count == 1 ? (byte)distinctTypes[0] : null;
                return (sensorType, sensors);
            }

            return ((byte)sensors.FirstOrDefault()!.Type, sensors);
        }

        private string GetTemplateName(string path, Guid folderId)
        {
            var folderName = _folders.GetUserFolders(CurrentUser).FirstOrDefault(x => x.Id == folderId)?.Name ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var result = path.Split('/');

            return $"{folderName}/{result[^1]}";

        }

        private List<SelectListItem> GetAlertSchedulesSelectList()
        {
            return [.. _alertScheduleProvider.GetAllSchedules().Select(tz => new SelectListItem
            {
                Value = tz.Id.ToString(),
                Text = $"{tz.Name}"
            })];
        }

        private static void PopulateAvailableChats(DataAlertTemplateViewModel model, HashSet<Guid> folderChats)
        {
            foreach (var (_, alerts) in model.DataAlerts)
                foreach (var alert in alerts)
                    foreach (var action in alert.Actions)
                        action.AvailableChats.UnionWith(folderChats);
        }

    }
}
