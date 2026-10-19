using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The sensor muting tools of the MCP surface (#1569, phase 2): suppress or
    /// restore a sensor's alert notifications — thin delegations to
    /// <see cref="SensorMuteAdministrationService"/>, the same engine the REST
    /// mute controller runs on.
    /// </summary>
    // The authorization is REUSED, not re-implemented: every service call
    // resolves the caller's owner through IApiTokenAuthorizationService at the
    // sensor's boundary (the 404/403 anti-enumeration split and the
    // security-event recording live inside). Read-write enforcement needs no
    // tool-level check: /mcp serves every message via POST, and the HsmApiToken
    // management policy rejects read-only tokens on unsafe methods outright.
    // The mute engine itself (period expiry included) stays core-owned — the
    // tools are the adapter, exactly like their REST twins.
    [McpServerToolType]
    public sealed class SensorMuteMcpTools
    {
        private readonly SensorMuteAdministrationService _writer;
        private readonly IHttpContextAccessor _http;

        public SensorMuteMcpTools(SensorMuteAdministrationService writer, IHttpContextAccessor http)
        {
            _writer = writer;
            _http = http;
        }


        // Idempotent=true (the retry-safe state-set writes): re-sending the
        // same mute/unmute converges on the same state — no create/delete
        // hazard the false flag of creates/deletes exists to warn about.
        [McpServerTool(Name = "mute_sensor", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Mutes a sensor's alert notifications — the sensor's data keeps flowing, only notifications are suppressed. Omit endOfMuting to mute indefinitely (never auto-unmutes); pass a UTC ISO 8601 instant (in the future) to mute until it — the server auto-unmutes at expiry. The response echoes the resulting sensor (state, endOfMuting). Fails on a Blocked sensor and on an unknown/invisible sensor id.")]
        public async System.Threading.Tasks.Task<SensorDto> MuteSensorAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Optional UTC ISO 8601 instant to mute until (must be in the future); omitted or null = mute indefinitely.")] DateTime? endOfMuting = null) =>
            McpToolWriteErrors.Unwrap(await _writer.MuteSensorAsync(sensorId, endOfMuting, User));

        [McpServerTool(Name = "unmute_sensor", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lifts a sensor's mute: alert notifications resume and the stored muting period clears. The response echoes the resulting sensor; a sensor that is not muted answers with its unchanged state (idempotent). Fails on a Blocked sensor and on an unknown/invisible sensor id.")]
        public async System.Threading.Tasks.Task<SensorDto> UnmuteSensorAsync(
            [Description("Sensor id.")] Guid sensorId) =>
            McpToolWriteErrors.Unwrap(await _writer.UnmuteSensorAsync(sensorId, User));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
