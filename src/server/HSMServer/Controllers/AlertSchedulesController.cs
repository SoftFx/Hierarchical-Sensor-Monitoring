using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HSMServer.ApiObjectsConverters;
using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Core.DataLayer;
using HSMServer.Model.AlertSchedule;
using System.Collections.Generic;
using HSMServer.Core.Schedule;
using HSMServer.Core.Cache;



namespace HSMServer.Controllers
{
    [Authorize]
    // #1409: schedules are global cross-folder objects, and Remove is a
    // tree-wide force rewrite of every policy bound to the schedule — the
    // same admin-only profile as ConfigurationController and
    // ApiTokensAdminController. The nav entry is guarded to match
    // (UserRoleHelper.IsAlertSchedulesPageAllowed).
    [AuthorizeIsAdmin]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertSchedulesController : BaseController
    {
        private static readonly JsonSerializerOptions _serializeOptions = new()
        {
            WriteIndented = true,
        };

        private static readonly JsonSerializerOptions _deserializeOptions = new()
        {
            AllowTrailingCommas = true,
        };

        private readonly IAlertScheduleProvider _scheduleProvider;
        private readonly AlertScheduleParser _parser = new();
        private readonly ITreeValuesCache _cache;

        static AlertSchedulesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertSchedulesController(IAlertScheduleProvider scheduleProvider, IUserManager users, ITreeValuesCache cache) : base(users)
        {
            _scheduleProvider = scheduleProvider;
            _cache = cache;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(GetAlertScheduleList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(Guid id)
        {
            // Existence guard BEFORE the detach: the detach walks the whole
            // tree, so a Remove posted with a random/unknown id must not pay
            // for that walk (#1409). GetSchedule reads the in-memory cache
            // only, and the loader skips entities whose parse failed — such a
            // row exists in storage but never enters the cache, and without
            // the DeleteSchedule call below the guard would strand it there
            // forever. DeleteSchedule is idempotent, so this purges the
            // stored row (a no-op for a truly unknown id).
            if (_scheduleProvider.GetSchedule(id) is null)
            {
                _scheduleProvider.DeleteSchedule(id);
                return RedirectToAction("Index");
            }

            // Detach FIRST (#1409), before the schedule id disappears: the
            // detach is best-effort (internal failures are logged there, the
            // deletion proceeds), but this ordering minimizes the crash window
            // — if the process dies between the two calls, the schedule still
            // exists and the operator's Remove retry re-runs the detach.
            // RequestAborted lets a browser-side timeout stop the dispatch of
            // further detach work instead of leaving the operator with no
            // feedback while it continues.
            await _cache.DetachAlertScheduleFromPoliciesAsync(id, HttpContext.RequestAborted);

            _scheduleProvider.DeleteSchedule(id);

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult NewPartial()
        {
            return PartialView("_AlertSchedule", new AlertScheduleViewModel());
        }

        [HttpGet]
        public IActionResult EditPartial(Guid id)
        {
            var data = _scheduleProvider.GetSchedule(id);
            if (data == null)
                return NotFound();

            return PartialView("_AlertSchedule", new AlertScheduleViewModel(data));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SavePartial(AlertScheduleViewModel model)
        {
            if (_scheduleProvider.GetAllSchedules().Any(x => x.Name == model.Name && x.Id != model.Id))
                ModelState.AddModelError(nameof(model.Name), "The schedule name must be unique.");

            if (ModelState.IsValid)
            {
                if (model.Id == Guid.Empty)
                    model.Id = Guid.NewGuid();

                try
                {
                    var schedule = _parser.Parse(model.Schedule);

                    schedule.Id = model.Id;
                    schedule.Name = model.Name;
                    schedule.Timezone = model.Timezone;

                    _scheduleProvider.SaveSchedule(schedule);

                    var list = _scheduleProvider.GetAllSchedules().Select(x => new AlertScheduleViewModel(x)).ToList();
                    return PartialView("_AlertSchedulesTable", list);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("Schedule", ex.Message);
                    return PartialView("_AlertSchedule", model);
                }
            }

            return PartialView("_AlertSchedule", model);
        }

        [HttpGet]
        public IActionResult GetAlertSchedulesTable()
        {
            return PartialView("_AlertSchedulesTable", GetAlertScheduleList());
        }


        private List<AlertScheduleViewModel> GetAlertScheduleList()
        {

            var list = _scheduleProvider.GetAllSchedules().Select(x => new AlertScheduleViewModel(x)).ToList();

            // One pass over the sensor table for the whole list; the per-id lookup
            // scans every sensor, so per-item calls were a full scan per schedule.
            var sensorsBySchedule = _cache.GetSensorsByAlertSchedules(list.Select(x => x.Id).ToList());

            foreach (var item in list)
            {
                item.Sensors = sensorsBySchedule.TryGetValue(item.Id, out var sensors) ? sensors : [];
            }

            return list;
        }
}
}