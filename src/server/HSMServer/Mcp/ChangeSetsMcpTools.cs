using HSMServer.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
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
    /// The undo-journal tools of the MCP surface (#1570): list, details and
    /// rollback of change sets — every REST/MCP configuration write is
    /// journaled under a changeSetId and undoable as a unit here.
    /// </summary>
    [McpServerToolType]
    [Description("Change-set tools of the undo journal: every configuration write through the API or these tools is journaled under a changeSetId. List change sets, inspect their before/after images, and roll them back (undo) with conflict detection, force and dry-run.")]
    public sealed class ChangeSetsMcpTools
    {
private const int MaxLimit = 200;

        private readonly IChangeSetAdministrationService _changeSets;
        private readonly IHttpContextAccessor _http;

        public ChangeSetsMcpTools(IChangeSetAdministrationService changeSets, IHttpContextAccessor http)
        {
            _changeSets = changeSets;
            _http = http;
        }


        [McpServerTool(Name = "list_change_sets", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists change sets touching entities visible to the token's owner, newest first. Every write without an explicit changeSetId forms its own single-entry change set — all of them appear here. Filter by author, time range, status (applied/rolledBack/partial) or a touched entity id.")]
        public Task<McpChangeSetsResult> ListChangeSetsAsync(
            [Description("Exact author name filter (case-insensitive).")] string author = null,
            [Description("Created at or after, UTC ISO 8601.")] DateTime? fromUtc = null,
            [Description("Created at or before, UTC ISO 8601.")] DateTime? toUtc = null,
            [Description("Status filter: applied, rolledBack or partial.")] string status = null,
            [Description("Only change sets touching this entity id (sensor, product, template or schedule).")] Guid? entityId = null,
            [Description("Page size, 1..200 (default 50).")] int limit = 50,
            [Description("Skip count (default 0).")] int offset = 0)
        {
            var effectiveLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
            var effectiveOffset = Math.Max(offset, 0);

            var visible = McpToolWriteErrors.Unwrap(_changeSets.ListChangeSets(User, new ChangeSetFilter
            {
                Author = author,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Status = status,
                EntityId = entityId,
                Limit = MaxLimit,
            }));

            return Task.FromResult(new McpChangeSetsResult
            {
                ChangeSets = [.. visible.Skip(effectiveOffset).Take(effectiveLimit)],
                TotalFound = visible.Count,
                Limit = effectiveLimit,
                Offset = effectiveOffset,
            });
        }

        [McpServerTool(Name = "get_change_set", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("One change set's details: per-entity before/after images of every entry visible to the token's owner (policy lists as JSON rows, template and schedule bodies). An unknown or invisible id answers the uniform not-found error.")]
        public System.Threading.Tasks.Task<ChangeSetDetailsDto> GetChangeSetAsync(
            [Description("Change set id.")] Guid changeSetId) =>
            Task.FromResult(McpToolWriteErrors.Unwrap(_changeSets.GetChangeSet(changeSetId, User)));

        [McpServerTool(Name = "rollback_change_set", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Rolls back a change set: compensating writes replayed in reverse order through the same write gates. Refuses with a per-entity diff when an entity changed after the change set recorded it, unless force is set. dryRun reports conflicts and the restore plan without applying anything. The rollback itself is journaled — its id (rollbackChangeSetId) is the redo handle. Restores configuration only: notifications sent, alerts fired and sensor history are untouched.")]
        public async Task<RollbackResultDto> RollbackChangeSetAsync(
            [Description("Change set id to roll back.")] Guid changeSetId,
            [Description("Override content conflicts deliberately — the intervening changes are clobbered by the recorded before-images.")] bool force = false,
            [Description("Report conflicts and the restore plan WITHOUT applying anything.")] bool dryRun = false) =>
            McpToolWriteErrors.Unwrap(await _changeSets.RollbackAsync(changeSetId, User, force, dryRun));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
