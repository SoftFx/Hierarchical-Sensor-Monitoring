using System;
using System.Threading.Tasks;
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
    /// CRUD over alert schedules (epic #1347). Bearer-token authenticated
    /// only (the HsmApiToken scheme — see the HsmApiToken security scheme of
    /// this document); served on the web-UI port only. Reads pass the
    /// caller-wide gate (the owner is an admin or holds a role on at least
    /// one product/folder, sensor references filtered to the owner's sight);
    /// WRITES additionally require the owner to be an ADMIN with a read-write
    /// token — the evaluator's Global-boundary decision (the token-side
    /// equivalent of the web UI's [AuthorizeIsAdmin] on Save/Remove).
    /// </summary>
    // REST surface for alert schedules (#1352 read-only; writes per the
    // follow-up fixed in management-api feature.md) — area conventions are
    // identical to AlertTemplatesApiController (see
    // aicontext/features/server/management-api/).
    //
    // Authorization differs per direction: reads keep the caller-wide gate
    // (CanSeeAnyBoundary), writes go through AuthorizeWrite(User, GlobalScope)
    // — the direction the feature doc fixed when the read surface landed
    // ("a future schedule WRITE must not reuse the caller-wide gate").
    // The evaluator makes the Global boundary admin-only on both sight and
    // write, with the usual split: a non-admin owner answers 404 (Global
    // sight is admin-only, so nothing about the surface's existence leaks),
    // an admin owner with a read-only token answers 403 (the method backstop
    // rejects read-only tokens on unsafe methods before the action anyway).
    //
    // Since #1393 the read logic lives in AlertReadService, shared with the MCP
    // alert tools; since the schedule write surface the writes live in
    // AlertScheduleAdministrationService, shared the same way. This controller
    // is the thin REST rendering of both.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/alertSchedules")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertSchedulesApiController : ControllerBase
    {
        private readonly AlertReadService _reader;
        private readonly AlertScheduleAdministrationService _writer;

        public AlertSchedulesApiController(AlertReadService reader, AlertScheduleAdministrationService writer)
        {
            _reader = reader;
            _writer = writer;
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
        public IActionResult GetSchedules(int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListSchedules(User, page, pageSize, HttpContext.RequestAborted).ToActionResult();

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

        /// <summary>
        /// Create a schedule. A client-sent id is ignored (the server generates one —
        /// the provider's Save is an upsert by id); the response echoes the STORED
        /// schedule with sensor paths filtered to the owner's sight.
        /// </summary>
        /// <remarks>Requires a read-write token and an ADMIN owner (the Global boundary is admin-only in the evaluator — the token-side equivalent of the web UI's admin gate on schedule editing).</remarks>
        [HttpPost]
        [ProducesResponseType(typeof(AlertScheduleDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateSchedule([FromBody] AlertScheduleUpsertDto dto)
        {
            var result = await _writer.CreateScheduleAsync(User, dto);

            return result.Success
                ? CreatedAtAction(nameof(GetSchedule), new { id = result.Value.Id }, result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>
        /// Update a schedule (upsert semantics at a known id); the response echoes
        /// the stored schedule. The id never travels in the body — the route owns it.
        /// </summary>
        /// <param name="id">Schedule id.</param>
        /// <remarks>Requires a read-write token and an ADMIN owner (the Global boundary is admin-only in the evaluator).</remarks>
        [HttpPut("{id:guid}")]
        [ProducesResponseType(typeof(AlertScheduleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateSchedule(Guid id, [FromBody] AlertScheduleUpsertDto dto) =>
            (await _writer.UpdateScheduleAsync(id, User, dto)).ToActionResult();

        /// <summary>
        /// Delete a schedule. Detaches the id from every referencing policy FIRST;
        /// on an incomplete detach the schedule survives and the answer is 409 —
        /// retrying re-runs the idempotent detach over the survivors.
        /// </summary>
        /// <param name="id">Schedule id.</param>
        /// <remarks>Requires a read-write token and an ADMIN owner (the Global boundary is admin-only in the evaluator).</remarks>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteSchedule(Guid id)
        {
            var result = await _writer.DeleteScheduleAsync(id, User, HttpContext.RequestAborted);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }
    }
}
