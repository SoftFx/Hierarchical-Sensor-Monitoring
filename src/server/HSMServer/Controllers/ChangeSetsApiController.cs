using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The change-set surface of the undo journal (#1570): list with filters,
    /// per-entity before/after details, and rollback (with force and dryRun).
    /// </summary>
    // Thin by design (the #1500 controller contract): every decision —
    // visibility filtering, anti-enumeration 404s, conflict detection — lives
    // in ChangeSetAdministrationService; the controller renders its
    // PolicyWriteResult through the area's uniform error contract.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/change-sets")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ChangeSetsApiController : ControllerBase
    {
        private readonly IChangeSetAdministrationService _changeSets;

        public ChangeSetsApiController(IChangeSetAdministrationService changeSets) =>
            _changeSets = changeSets;


        /// <summary>
        /// Change sets touching entities visible to the caller, newest first.
        /// Writers without a changeSetId form their own single-entry change
        /// sets — every REST/MCP configuration write is listed here.
        /// </summary>
        /// <param name="author">Exact author name filter (case-insensitive).</param>
        /// <param name="fromUtc">Created at or after (UTC ISO 8601).</param>
        /// <param name="toUtc">Created at or before (UTC ISO 8601).</param>
        /// <param name="status">applied | rolledBack | partial.</param>
        /// <param name="entityId">Only change sets touching this entity id (sensor, product, template, schedule).</param>
        /// <param name="limit">Page size, 1..200 (default 50).</param>
        /// <param name="offset">Skip count (default 0).</param>
        [HttpGet]
        [ProducesResponseType(typeof(System.Collections.Generic.List<ChangeSetSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult ListChangeSets(
            [FromQuery] string author = null,
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            [FromQuery] string status = null,
            [FromQuery] Guid? entityId = null,
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0) =>
            _changeSets.ListChangeSets(User, new ChangeSetFilter
            {
                Author = author,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Status = status,
                EntityId = entityId,
                Limit = limit,
                Offset = offset,
            }).ToActionResult();

        /// <summary>
        /// One change set's details: per-entity before/after images (visible
        /// entries only). An unknown or invisible id answers 404.
        /// </summary>
        /// <param name="id">Change set id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ChangeSetDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetChangeSet(Guid id) =>
            _changeSets.GetChangeSet(id, User).ToActionResult();

        /// <summary>
        /// Roll back a change set: compensating writes replayed in reverse
        /// order through the same write gates. Refuses with a per-entity diff
        /// when an entity changed after the change set recorded it, unless
        /// forced. Restores configuration only — notifications sent, alerts
        /// fired and sensor history are untouched.
        /// </summary>
        /// <param name="id">Change set id.</param>
        /// <param name="force">Override content conflicts deliberately.</param>
        /// <param name="dryRun">Report conflicts and the restore plan, apply nothing.</param>
        [HttpPost("{id:guid}/rollback")]
        [ProducesResponseType(typeof(RollbackResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Rollback(Guid id, [FromQuery] bool force = false,
            [FromQuery] bool dryRun = false)
        {
            var result = await _changeSets.RollbackAsync(id, User, force, dryRun);

            if (result.Success && !dryRun)
                Response.SetChangeSet(Guid.Parse(result.Value.RollbackChangeSetId));

            return result.ToActionResult();
        }
    }
}
