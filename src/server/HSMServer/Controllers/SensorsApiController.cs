using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Sensor surface of the management API: flat recursive sensor search, one
    /// sensor with its current value, the sensor's recent history (#1386), and
    /// sensor deletion (#1637). Bearer-token authenticated only (the HsmApiToken
    /// scheme); served on the web-UI port only. A token sees exactly the sensors
    /// its owner sees; unknown and invisible ids answer the SAME 404. Deletion
    /// needs a read-write token and the owner's write access at the sensor.
    /// </summary>
    // The AI-agent workhorse: "find network sensors in product X" is
    // GET /api/v1/sensors?product={X}&amp;search=network. Since #1391 the read
    // logic lives in SensorTreeReadService, shared with the MCP tools; this
    // controller is the REST rendering of it — its suites are the regression net
    // for the service's behavior. The DELETE renders
    // SensorDeletionAdministrationService, the same engine the MCP delete tool
    // runs on (#1637) — the removal semantics (cache, policies, history, the
    // TreeCrud journal record) live in the core, the same entry the web UI's
    // remove-node drives.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/sensors")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class SensorsApiController : ControllerBase
    {
        private readonly SensorTreeReadService _reader;
        private readonly SensorDeletionAdministrationService _deleter;
        private readonly SensorMoveAdministrationService _mover;

        public SensorsApiController(SensorTreeReadService reader, SensorDeletionAdministrationService deleter,
            SensorMoveAdministrationService mover)
        {
            _reader = reader;
            _deleter = deleter;
            _mover = mover;
        }


        /// <summary>
        /// Search sensors, paginated, ordered by full path then id. Without
        /// parameters: every sensor visible to the token's owner. With
        /// <c>product</c>: the subtree of that node (any product or folder id —
        /// unknown and invisible ids answer the uniform 404). The search text
        /// matches name, description and path (OR).
        /// </summary>
        /// <param name="product">Optional node id (root product or folder) whose whole subtree is searched.</param>
        /// <param name="search">Optional search text (see searchMode).</param>
        /// <param name="searchMode">How the search text matches: "contains" (default, case-insensitive substring) or "regex" (.NET regular expression, case-insensitive, time-bounded).</param>
        /// <param name="type">Optional sensor type name, e.g. "Double" or "IntegerBar".</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<SensorDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult GetSensors(Guid? product = null, string search = null,
            string searchMode = null, string type = null, int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.FindSensors(product, search, searchMode, type, page, pageSize, User,
                    SensorTreeDtoMapper.ToSensorDto, HttpContext.RequestAborted)
                .ToActionResult();


        /// <summary>
        /// Get one sensor by id: metadata plus its current value — the same shape
        /// the search list returns.
        /// </summary>
        /// <param name="id">Sensor id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(SensorDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSensor(Guid id) =>
            _reader.GetSensor(id, User).ToActionResult();


        /// <summary>
        /// Read the sensor's history: the NEWEST maxPoints values inside
        /// [from, to], oldest first, no aggregation. Timeout markers (OffTime
        /// points where the sensor was silent) are included. When the window holds
        /// more values than requested, the oldest excess is dropped and
        /// <c>truncated</c> is set — narrow the window for full resolution. A
        /// File sensor whose history is being read by another request answers
        /// with <c>readUnavailable</c>=true and no points — retry shortly (File
        /// response bounds are additionally capped at 100 points). For
        /// aggregated (bar) sensors the response may carry one point older than
        /// the echoed <c>from</c> — the pre-window border value (only in an
        /// under-full response: a full one drops the border first, and dropping
        /// it alone never sets <c>truncated</c> — it is outside the window).
        /// </summary>
        /// <param name="id">Sensor id.</param>
        /// <param name="from">Window start, UTC ISO 8601; default: to − 24 hours.</param>
        /// <param name="to">Window end, UTC ISO 8601; default: now.</param>
        /// <param name="maxPoints">Point limit, 1..10000 (default 1000); non-positive values fall back to the default.</param>
        [HttpGet("{id:guid}/history")]
        [ProducesResponseType(typeof(SensorHistoryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSensorHistory(Guid id, DateTime? from = null, DateTime? to = null,
            int maxPoints = SensorTreeReadService.DefaultMaxPoints) =>
            (await _reader.GetSensorHistoryAsync(id, from, to, maxPoints, User, HttpContext.RequestAborted))
                .ToActionResult();


        /// <summary>
        /// Delete the sensor — IRREVERSIBLY: the cache entry, its attached
        /// policies, its mute state, its whole stored history and its undo
        /// change-set references are removed (a TreeCrud journal record with
        /// the initiator is written; there is no undo). The 204 verifies the
        /// cache removal — the persistence-side cleanup (journal record,
        /// history wipe, change-set purge) is best-effort inside the core's
        /// swallow-only handler and a failure there is logged server-side,
        /// the same visibility the web UI's remove-node has. A dry run
        /// (the <c>X-HSM-Dry-Run: true</c> header) answers 200 with a preview
        /// of everything the real write would destroy and applies nothing.
        /// </summary>
        /// <param name="id">Sensor id.</param>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(typeof(SensorDeletePreviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteSensor(Guid id)
        {
            var result = await _deleter.DeleteSensorAsync(id, User, Request.WriteOptions());

            if (result.IsDryRun)
                return Ok(result.Preview);

            if (!result.Success)
                return PolicyWriteResult.ToActionResult(result.Failure);


            return NoContent();
        }


        /// <summary>
        /// Move sensors to another path, in their product or into another root
        /// product (#1718): each sensor keeps its id, history, settings and alerts,
        /// and its old path becomes an alias that keeps routing values to it.
        /// One sensor goes to a full <c>path</c>; one or more go into a
        /// <c>node</c> keeping their names. Not atomic: the 200 body reports
        /// every sensor's outcome (a sensor the token's owner cannot see or
        /// write is reported there, it does not fail the others). With
        /// <c>onConflict: "merge"</c> a sensor moving onto a path held by a
        /// sensor of the same type absorbs it — IRREVERSIBLE. A dry run
        /// (<c>X-HSM-Dry-Run: true</c>) answers the prediction per sensor —
        /// conflicts, nodes to create, alert templates detached/applied,
        /// inherited settings that change — and applies nothing.
        /// </summary>
        [HttpPost("move")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(SensorMoveResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> MoveSensors([FromBody] SensorMoveRequestDto request) =>
            (await _mover.MoveSensorsAsync(request, User, Request.WriteOptions(), HttpContext.RequestAborted))
                .ToActionResult(Response);


        /// <summary>
        /// Remove one alias of a moved sensor (an entry of the sensor's
        /// <c>aliases</c>): values sent to that old path stop landing in the
        /// sensor — the next one creates a new sensor there.
        /// </summary>
        /// <param name="id">Sensor id.</param>
        /// <param name="product">The alias's root product id.</param>
        /// <param name="path">The alias's path relative to that product.</param>
        [HttpDelete("{id:guid}/aliases")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> RemoveSensorAlias(Guid id, [FromQuery] Guid product, [FromQuery] string path)
        {
            var result = await _mover.RemoveAliasAsync(id, product, path, User, HttpContext.RequestAborted);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }
    }
}
