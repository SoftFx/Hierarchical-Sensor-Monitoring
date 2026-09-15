using System;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
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
    //
    // Since #1393 the read logic lives in AlertReadService, shared with the MCP
    // alert tools; this controller is the REST rendering of it.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/alertSchedules")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertSchedulesApiController : ControllerBase
    {
        public const int DefaultPageSize = ApiPagination.DefaultPageSize;

        private readonly AlertReadService _reader;

        public AlertSchedulesApiController(AlertReadService reader)
        {
            _reader = reader;
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
        public IActionResult GetSchedules(int page = 1, int pageSize = DefaultPageSize) =>
            _reader.ListSchedules(User, page, pageSize).ToActionResult();

        /// <summary>Get one schedule by id (same caller-wide gate as the list).</summary>
        /// <param name="id">Schedule id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AlertScheduleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSchedule(Guid id) =>
            _reader.GetSchedule(id, User).ToActionResult();
    }
}
