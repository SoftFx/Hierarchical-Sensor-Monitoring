using HSMServer.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Journal;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The unified-Journal tools of the MCP surface (#1588): journal records
    /// of every configuration change (web editor, API and these tools alike)
    /// with filters, per-record details, and the admin-only rollback by
    /// record — one record addresses its request's whole undo group.
    /// </summary>
    [McpServerToolType]
    [Description("Journal tools of the unified configuration journal: every configuration change — from the web editor, the API or these tools — is journaled here. List journal records with filters, inspect a record and its undo group's before/after images, and roll a record back (undoes its whole request; admin tokens only).")]
    public sealed class JournalMcpTools
    {
        private const int MaxLimit = 200;

        private readonly JournalAdministrationService _journal;
        private readonly IHttpContextAccessor _http;

        public JournalMcpTools(JournalAdministrationService journal, IHttpContextAccessor http)
        {
            _journal = journal;
            _http = http;
        }


        [McpServerTool(Name = "list_journal_records", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists journal records of configuration changes touching entities visible to the token's owner, newest first. Every write of a single request (one tool call, one HTTP request, one form save) shares one undo group. Filter by author, time range, entity kind (sensorDataPolicies, sensorTtlPolicies, productTtlPolicies, alertTemplate, alertSchedule, nodeSettings, treeCrud, folder, other), root product or entity id.")]
        public Task<McpJournalRecordsResult> ListJournalRecordsAsync(
            [Description("Author substring filter (case-insensitive).")] string author = null,
            [Description("Recorded at or after, UTC ISO 8601 (default now - 30 days).")] DateTime? fromUtc = null,
            [Description("Recorded at or before, UTC ISO 8601.")] DateTime? toUtc = null,
            [Description("Entity kind filter: sensorDataPolicies, sensorTtlPolicies, productTtlPolicies, alertTemplate, alertSchedule, nodeSettings, treeCrud, folder or other.")] string kind = null,
            [Description("Only records of this root product id.")] Guid? productId = null,
            [Description("Only records keyed by this entity id (sensor, product, folder, template, schedule).")] Guid? entityId = null,
            [Description("Page size, 1..200 (default 50).")] int limit = 50,
            [Description("Skip count (default 0).")] int offset = 0)
        {
            var effectiveLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
            var effectiveOffset = Math.Max(offset, 0);

            var visible = McpToolWriteErrors.Unwrap(_journal.ListRecords(User, new JournalFilter
            {
                Author = author,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Kind = kind,
                ProductId = productId,
                EntityId = entityId,
                Limit = MaxLimit,
            }));

            return Task.FromResult(new McpJournalRecordsResult
            {
                Records = [.. visible.Skip(effectiveOffset).Take(effectiveLimit)],
                TotalFound = visible.Count,
                Limit = effectiveLimit,
                Offset = effectiveOffset,
            });
        }

        [McpServerTool(Name = "get_journal_record", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("One journal record's details: the record plus its undo group's per-entity before/after images (policy lists as JSON rows, template and schedule bodies) for the entries visible to the token's owner. An unknown or invisible record id answers the uniform not-found error.")]
        public Task<JournalRecordDetailsDto> GetJournalRecordAsync(
            [Description("Journal record id (the `id` field of list_journal_records rows).")] string recordId) =>
            Task.FromResult(McpToolWriteErrors.Unwrap(_journal.GetRecord(recordId, User)));

        [McpServerTool(Name = "rollback_journal_record", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Rolls back a journal record's request (its undo group): compensating writes replayed in reverse order through the same write gates — every write of the SAME request is undone together. Requires an ADMIN token owner (Manager tokens are denied, #1588). Refuses with a per-entity diff when an entity changed after the write recorded it, unless force is set. dryRun reports conflicts and the restore plan without applying anything. The rollback itself is journaled — its record is the redo handle. Restores configuration only: notifications sent, alerts fired and sensor history are untouched.")]
        public async Task<RollbackResultDto> RollbackJournalRecordAsync(
            [Description("Journal record id to roll back (the `id` field of list_journal_records rows).")] string recordId,
            [Description("Override content conflicts deliberately — the intervening changes are clobbered by the recorded before-images.")] bool force = false,
            [Description("Report conflicts and the restore plan WITHOUT applying anything.")] bool dryRun = false) =>
            McpToolWriteErrors.Unwrap(await _journal.RollbackAsync(recordId, User, force, dryRun));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
