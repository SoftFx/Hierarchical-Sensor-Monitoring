using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Journal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    /// <summary>
    /// The unified Journal surface of the management API (#1588): journal
    /// records of every configuration change with filters, per-record
    /// details (with the undo group's before/after images), and the
    /// admin-only rollback by record.
    /// </summary>
    // Thin by design (the #1500 controller contract): every decision —
    // visibility filtering, anti-enumeration 404s, the admin gate, conflict
    // detection — lives in JournalAdministrationService and the rollback
    // engine; the controller renders the PolicyWriteResult through the
    // area's uniform error contract.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/journal")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class JournalApiController : ControllerBase
    {
        private readonly JournalAdministrationService _journal;

        public JournalApiController(JournalAdministrationService journal) => _journal = journal;


        /// <summary>
        /// Journal records of configuration changes visible to the caller, newest first.
        /// </summary>
        /// <param name="author">Author substring filter (case-insensitive).</param>
        /// <param name="fromUtc">Recorded at or after (UTC ISO 8601; default now - 30 days).</param>
        /// <param name="toUtc">Recorded at or before (UTC ISO 8601).</param>
        /// <param name="kind">Entity kind: sensorDataPolicies, sensorTtlPolicies, productTtlPolicies, alertTemplate, alertSchedule, nodeSettings, treeCrud, folder or other.</param>
        /// <param name="productId">Only records of this root product.</param>
        /// <param name="entityId">Only records keyed by this entity id.</param>
        /// <param name="limit">Page size, 1..200 (default 100).</param>
        /// <param name="offset">Skip count (default 0).</param>
        [HttpGet]
        [ProducesResponseType(typeof(System.Collections.Generic.List<JournalRecordDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult ListRecords(
            [FromQuery] string author = null,
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            [FromQuery] string kind = null,
            [FromQuery] Guid? productId = null,
            [FromQuery] Guid? entityId = null,
            [FromQuery] int limit = 100,
            [FromQuery] int offset = 0) =>
            _journal.ListRecords(User, new JournalFilter
            {
                Author = author,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Kind = kind,
                ProductId = productId,
                EntityId = entityId,
                Limit = limit,
                Offset = offset,
            }).ToActionResult();

        /// <summary>
        /// One journal record's details: the record plus its undo group's
        /// per-entity before/after images (visible entries only). An unknown
        /// or invisible record id answers 404.
        /// </summary>
        /// <param name="recordId">Journal record id ("{entityId}:{ticks}:{type}" as returned by the list).</param>
        [HttpGet("{recordId}")]
        [ProducesResponseType(typeof(JournalRecordDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetRecord(string recordId) =>
            _journal.GetRecord(recordId, User).ToActionResult();

        /// <summary>
        /// Roll back a journal record's request (its undo group): compensating
        /// writes replayed in reverse order through the same write gates. The
        /// token's owner must be an ADMIN (#1588 breaking change — Manager
        /// tokens can no longer roll back). Refuses with a per-entity diff
        /// when an entity changed after the write recorded it, unless forced.
        /// Restores configuration only — notifications sent, alerts fired and
        /// sensor history are untouched.
        /// </summary>
        /// <param name="recordId">Journal record id.</param>
        /// <param name="force">Override content conflicts deliberately.</param>
        /// <param name="dryRun">Report conflicts and the restore plan, apply nothing.</param>
        [HttpPost("{recordId}/rollback")]
        [ProducesResponseType(typeof(RollbackResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Rollback(string recordId, [FromQuery] bool force = false,
            [FromQuery] bool dryRun = false) =>
            (await _journal.RollbackAsync(recordId, User, force, dryRun)).ToActionResult();
    }
}
