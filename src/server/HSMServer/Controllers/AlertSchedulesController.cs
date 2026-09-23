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
using HSMServer.Constants;
using HSMServer.Core.DataLayer;
using HSMServer.Model.AlertSchedule;
using System.Collections.Generic;
using HSMServer.Core.Schedule;
using HSMServer.Core.Cache;



namespace HSMServer.Controllers
{
    // #1409: schedule VIEWING (Index, table, editor partials) is open to
    // every authenticated user — schedules appear in the alert editors'
    // dropdowns for managers too. Schedule MUTATION is a different class of
    // action: schedules are global cross-folder objects, and every mutation
    // (create/edit/delete) rewrites or removes what policies tree-wide point
    // at — so SavePartial and Remove carry [AuthorizeIsAdmin] individually.
    [Authorize]
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
        [AuthorizeIsAdmin]
        public async Task<IActionResult> Remove(Guid id)
        {
            // The detach runs unconditionally — deliberately NO existence
            // guard (#1409 review): a guard made a retry after a partial
            // detach a silent no-op, and the one population GUARANTEED to
            // dangle (policies referencing a stored schedule whose parse
            // failed — a row the loader skips, so it never enters the cache
            // a guard reads) is exactly what the guard skipped. When nothing
            // references the id the walk dispatches no work at all, and the
            // idempotent DeleteSchedule below purges the row either way.
            //
            // Detach FIRST, before the schedule id disappears. The result
            // reports completion, and on ANY incomplete detach — request
            // abort, a per-entity failure, a failed persist — the schedule
            // is NOT deleted: deleting it would strand the surviving
            // references permanently (nothing left to Remove), while the
            // live schedule keeps the operator's retry meaningful — Remove
            // again re-runs the idempotent detach over the survivors. The
            // failure is flashed on the Index page (same pattern as
            // AlertTemplatesController.Remove); the per-entity detail is
            // Error-logged inside the detach. A process crash between the
            // two calls leaves the same recoverable state.
            // RequestAborted lets a browser-side timeout stop the dispatch
            // of further detach work instead of continuing with no feedback.
            var detach = await _cache.DetachAlertScheduleFromPoliciesAsync(id, HttpContext.RequestAborted);

            if (!detach.IsOk)
            {
                TempData[TextConstants.TempDataErrorText] =
                    $"Schedule was not deleted — not every policy reference could be cleared. Remove it again to retry. {detach.Error}";

                return RedirectToAction("Index");
            }

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
        // Admin-only like Remove (#1409): creating/editing a global schedule
        // is the same class of tree-wide mutation as deleting one.
        [AuthorizeIsAdmin]
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