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
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Core.Cache;



namespace HSMServer.Controllers
{
    // #1409: schedule VIEWING (Index, table, editor partials) is open to
    // every authenticated user — schedules appear in the alert editors'
    // dropdowns for managers too. Schedule MUTATION is a different class of
    // action: schedules are global cross-folder objects, and every mutation
    // (create/edit/delete) rewrites or removes what policies tree-wide point
    // at — so SavePartial and Remove carry [AuthorizeIsAdmin] individually,
    // and the Index/table views gate the Add/Edit/Remove controls on the
    // same check (non-admins get the read-only page, not 401 buttons).
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
        private readonly AlertScheduleAdministrationService _writer;

        static AlertSchedulesController()
        {
            _deserializeOptions.Converters.Add(new JsonStringEnumConverter());

            _serializeOptions.Converters.Add(new ListAsJsonStringConverter());
            _serializeOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public AlertSchedulesController(IAlertScheduleProvider scheduleProvider, IUserManager users,
            ITreeValuesCache cache, AlertScheduleAdministrationService writer) : base(users)
        {
            _scheduleProvider = scheduleProvider;
            _cache = cache;
            _writer = writer;
        }

        // The web form's write options (#1588): the post's request-scoped
        // undo group — the journaled save rides it.
        private ChangeSetWriteOptions JournalWriteOptions() =>
            new() { ChangeSetId = ChangeSetHeaders.RequestGroupId(HttpContext) };

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
            // #1588 single write path: the detach walk and the delete ride
            // the schedule administration service — one journaled unit with
            // its per-node before-images (the same engine REST/MCP use).
            var result = await _writer.DeleteScheduleAsync(id, User, HttpContext.RequestAborted,
                JournalWriteOptions(), new CookieUserAccess(CurrentUser, _cache));

            if (!result.Success)
            {
                TempData[TextConstants.TempDataErrorText] = result.Failure.Message;

                return RedirectToAction("Index");
            }

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
                try
                {
                    // #1588 single write path: the parsed form rides the
                    // schedule administration service — the same uniqueness
                    // gate, id ownership and undo journaling as REST/MCP.
                    var schedule = _parser.Parse(model.Schedule);

                    schedule.Name = model.Name;
                    schedule.Timezone = model.Timezone;

                    var id = model.Id == Guid.Empty ? (Guid?)null : model.Id;
                    var result = _writer.SaveSchedule(User, id, schedule, JournalWriteOptions(),
                        new CookieUserAccess(CurrentUser, _cache));

                    if (!result.Success)
                    {
                        foreach (var (field, messages) in result.Failure.Errors ?? new Dictionary<string, string[]>())
                            foreach (var message in messages)
                                ModelState.AddModelError(field, message);

                        // A message-less failure (the schedule was deleted
                        // while being edited) must not render as a silent
                        // no-op save (#1588 review round 3).
                        ModelState.AddModelError(nameof(model.Name),
                            result.Failure.Message ?? "The schedule no longer exists. Refresh the list.");

                        return PartialView("_AlertSchedule", model);
                    }

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