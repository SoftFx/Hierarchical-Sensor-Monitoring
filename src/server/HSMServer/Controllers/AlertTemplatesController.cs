using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using HSMServer.Authentication;
using HSMServer.Constants;
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
        private readonly ISlackDestinationsManager _slackDestinations;


        static AlertTemplatesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertTemplatesController(ITelegramChatsManager telegram, IFolderManager folders, TreeViewModel tree, ITreeValuesCache cache,
                                        IUserManager users, IAlertScheduleProvider provider, ISlackDestinationsManager slackDestinations) : base(users)
        {
            _telegram = telegram;
            _folders = folders;
            _cache = cache;
            _tree = tree;
            _alertScheduleProvider = provider;
            _slackDestinations = slackDestinations;
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
            private const int PageSize = 10;

            public byte? Type { get; set; }
            public string Sensors { get; set; }
            public List<ChatItem> Chats { get; set; }
            public int TotalCount { get; set; }
            public int Page { get; set; }
            public int TotalPages { get; set; }

            public UpdateResponse(byte? type, List<BaseSensorModel> sensors, List<ChatItem> chats, int page = 1)
            {
                Type = type;
                Chats = chats;
                TotalCount = sensors.Count;
                Page = page;
                TotalPages = sensors.Count > 0 ? (int)Math.Ceiling((double)sensors.Count / PageSize) : 0;

                if (sensors.Count > 0)
                {
                    var pageSensors = sensors.Skip((page - 1) * PageSize).Take(PageSize).ToList();
                    var sb = new StringBuilder(pageSensors.Count * 64);
                    foreach (var sensor in pageSensors)
                        sb.Append($"<div class=\"d-flex flex-row align-items-center fullCondition\">{System.Web.HttpUtility.HtmlEncode(sensor.FullPath)}</div>");

                    Sensors = sb.ToString();
                }
                else
                    Sensors = null;
            }
        }

        [HttpGet]
        public IActionResult UpdateTemplate(byte type, string paths, Guid folderId, int page = 1)
        {
            List<string> pathList = [];
            if (!string.IsNullOrWhiteSpace(paths))
            {
                try { pathList = JsonSerializer.Deserialize<List<string>>(paths)?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? []; }
                catch (JsonException) { }
            }

            var (sensorType, sensors) = GetAffectedSensors(type, pathList, folderId);

            List<ChatItem> chats = [];
            if (_folders.TryGetValue(folderId, out var folder))
            {
                chats = _telegram.GetValues()
                    .Where(c => folder.TelegramChats.Contains(c.Id))
                    .Select(c => new ChatItem(c.Id, c.Name, (byte)c.Type))
                    .ToList();
            }

            var response = new UpdateResponse(sensorType, sensors, chats, page);

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
                    model.Name = string.Empty;
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

            var availableSlackDestinations = _slackDestinations.GetValues()
                .Where(d => d.SendMessages)
                .ToDictionary(d => d.Id, d => d.Name);

            AlertTemplateModel model = null;

            if (ModelState.IsValid)
            {
                model = data.ToModel(availableChats, availableSlackDestinations);

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

            model ??= data.ToModel(availableChats, availableSlackDestinations);
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
            var (success, error) = await _cache.RemoveAlertTemplateAsync(id);

            if (!success)
                TempData[TextConstants.TempDataErrorText] = error;

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
