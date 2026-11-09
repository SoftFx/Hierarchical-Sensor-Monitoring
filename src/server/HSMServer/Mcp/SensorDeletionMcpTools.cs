using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The sensor deletion tool of the MCP surface (#1637): delete a sensor —
    /// a thin delegation to <see cref="SensorDeletionAdministrationService"/>,
    /// the same engine the REST delete endpoint runs on.
    /// </summary>
    // The authorization is REUSED, not re-implemented: the service resolves
    // the caller's owner through IApiTokenAuthorizationService at the sensor's
    // boundary (the 404/403 anti-enumeration split and the security-event
    // recording live inside). Read-write enforcement needs no tool-level
    // check: /mcp serves every message via POST, and the HsmApiToken
    // management policy rejects read-only tokens on unsafe methods outright.
    //
    // The removal semantics stay core-owned (the same RemoveSensorAsync the
    // web UI's remove-node drives): cache, policies, mute state, the WHOLE
    // stored history and the undo change-set references — irreversible by
    // design, hence the dryRun preview arm of the envelope.
    [McpServerToolType]
    public sealed class SensorDeletionMcpTools
    {
        private readonly SensorDeletionAdministrationService _writer;
        private readonly IHttpContextAccessor _http;

        public SensorDeletionMcpTools(SensorDeletionAdministrationService writer, IHttpContextAccessor http)
        {
            _writer = writer;
            _http = http;
        }


        // Idempotent=false: a create/delete hazard flag — re-running a delete
        // against an already-deleted id answers not-found, not a repeat success.
        [McpServerTool(Name = "delete_sensor", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Deletes a sensor IRREVERSIBLY: its cache entry, attached data and TTL policies, mute state, its WHOLE stored history and its undo references are removed (a journal record with the initiator is kept; there is no undo). Use dryRun first to preview everything the deletion would destroy. Fails on an unknown/invisible sensor id and for read-only tokens.")]
        public async System.Threading.Tasks.Task<McpSensorDeleteResult> DeleteSensorAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Preview everything the deletion would destroy (the sensor, its policies, its history size) WITHOUT deleting anything — the answer carries `preview` instead of `value`.")] bool dryRun = false)
        {
            // A deletion journals no undo entry (it PURGES change-set
            // references instead, #1570), so no undo group is minted — only
            // the dry-run flag rides the write options.
            var result = await _writer.DeleteSensorAsync(sensorId, User, new ChangeSetWriteOptions { DryRun = dryRun });

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = (SensorDeletePreviewDto)result.Preview }
                : new() { Value = new McpDeletedResult { Id = sensorId } };
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool body reads like its REST twin's `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
