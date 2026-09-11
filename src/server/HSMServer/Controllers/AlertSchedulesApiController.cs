using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Schedule;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertSchedules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Read-only listing of alert schedules (epic #1347). Bearer-token authenticated
    /// only (the HsmApiToken scheme — see the HsmApiToken security scheme of this
    /// document); served on the web-UI port only. Readable by any token whose owner
    /// is an admin or holds a role on at least one product/folder; sensor references
    /// are filtered to the products the owner can see.
    /// </summary>
    // Read-only REST surface for alert schedules (#1352, epic #1347) — the second
    // /api/v1 resource controller; area conventions are identical to
    // AlertTemplatesApiController (see aicontext/features/server/management-api/).
    // Writes are deliberately out of scope for v1 (schedule parser/timezone
    // complexity is a follow-up).
    //
    // Authorization differs from folder-scoped resources: schedules are GLOBAL, and
    // the web UI shows them to every logged-in user. The token-side equivalent of
    // "this principal may work with alerts" is delegated to the evaluator's
    // caller-wide gate (CanSeeAnyBoundary): the owner is an admin or currently sees
    // at least one boundary, with liveness and the denial audit record inside the
    // evaluator. Nothing about schedule existence is per-caller scoped, so an
    // entitled caller gets a plain 404 for an unknown id while an unentitled one
    // gets 403 for every id.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/alertSchedules")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertSchedulesApiController : ControllerBase
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        private readonly IAlertScheduleProvider _schedules;
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;

        public AlertSchedulesApiController(IAlertScheduleProvider schedules, ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization)
        {
            _schedules = schedules;
            _cache = cache;
            _authorization = authorization;
        }


        /// <summary>
        /// List schedules, paginated, ordered by name then id. Requires the caller-wide
        /// gate (the owner is an admin or sees at least one boundary); each schedule's
        /// sensors list carries only paths the caller's owner may see.
        /// </summary>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<AlertScheduleDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSchedules(int page = 1, int pageSize = DefaultPageSize)
        {
            if (!AuthorizeSchedulesRead())
                return Denied();

            page = Math.Max(page, 1);
            pageSize = Math.Min(pageSize <= 0 ? DefaultPageSize : pageSize, MaxPageSize);

            var all = (_schedules.GetAllSchedules() ?? [])
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Id)
                .ToList();

            var totalPages = all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)pageSize);

            // Clamp the page into [1, totalPages]: unchecked (page - 1) * pageSize
            // would overflow int for huge page numbers, and a NEGATIVE Skip count
            // silently returns the FIRST page labeled as page N.
            page = Math.Min(page, Math.Max(totalPages, 1));

            var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // The page's sensor references are resolved in ONE pass over the sensor
            // cache (the per-id lookup scans every sensor, so per-item calls would be
            // a full scan per schedule), and the visibility decision is memoized per
            // DISTINCT product — the same per-request memoization the templates list
            // applies per folder.
            var sensorsBySchedule = _cache.GetSensorsByAlertSchedules([.. pageItems.Select(s => s.Id)]) ?? [];
            var isProductVisible = _authorization.MemoizedProductVisibility(User);

            return Ok(new ApiPageDto<AlertScheduleDto>
            {
                Items = [.. pageItems.Select(s => ToDto(s,
                    sensorsBySchedule.TryGetValue(s.Id, out var sensors) ? sensors : null,
                    isProductVisible))],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            });
        }

        /// <summary>Get one schedule by id (same caller-wide gate as the list).</summary>
        /// <param name="id">Schedule id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AlertScheduleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSchedule(Guid id)
        {
            if (!AuthorizeSchedulesRead())
                return Denied();

            var schedule = _schedules.GetSchedule(id);

            if (schedule is null)
                return ManagementApiErrors.NotFound();

            return Ok(ToDto(schedule, _cache.GetSensorsByAlertSchedule(id), _authorization.MemoizedProductVisibility(User)));
        }


        // The caller-wide gate lives in the evaluator: the owner is an admin or
        // currently holds a role on at least one product/folder, recording one
        // AuthorizationDenied (the 403 kind — never the enumeration-probe kind) when
        // nothing qualifies.
        private bool AuthorizeSchedulesRead() =>
            _authorization.CanSeeAnyBoundary(User);

        private IActionResult Denied() =>
            ManagementApiErrors.Forbidden(
                "The token's owner cannot see any product or folder.");

        private AlertScheduleDto ToDto(Core.Model.Policies.AlertSchedule schedule,
            List<Core.Model.BaseSensorModel> sensors, Func<Guid, bool> isProductVisible)
        {
            // Sensor references filtered to the owner's sight — resolved exactly the
            // way the evaluator resolves sensors: through the sensor's product's
            // current boundary. Parentless sensors fail closed (dropped from the list).
            var visiblePaths = (sensors ?? [])
                .Where(sensor => sensor.Parent?.Root is { } product && isProductVisible(product.Id))
                .Select(sensor => sensor.FullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return AlertScheduleDtoMapper.ToDto(schedule, visiblePaths);
        }
    }
}
