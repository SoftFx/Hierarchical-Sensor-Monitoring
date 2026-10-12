using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.Alerts;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The six policy write tools of the MCP surface (phase 2 of AI-agent
    /// access): data-policy and TTL-policy CRUD on a sensor — thin
    /// delegations to <see cref="PolicyAdministrationService"/> (#1500), the
    /// same engine the REST policy controllers run on.
    /// </summary>
    // The authorization is REUSED, not re-implemented: every service call
    // resolves the caller's owner through IApiTokenAuthorizationService at
    // the sensor's boundary (the 404/403 anti-enumeration split and the
    // security-event recording live inside). Read-write enforcement needs no
    // tool-level check: /mcp serves every message via POST, and the
    // HsmApiToken management policy rejects read-only tokens on unsafe
    // methods outright. Product-policy writes stay REST-only (a deliberate
    // bound: they route to the owning sensor's list anyway).
    [McpServerToolType]
    public sealed class PoliciesMcpTools
    {
        private readonly PolicyAdministrationService _writer;
        private readonly IHttpContextAccessor _http;

        public PoliciesMcpTools(PolicyAdministrationService writer, IHttpContextAccessor http)
        {
            _writer = writer;
            _http = http;
        }


        [McpServerTool(Name = "create_sensor_policy", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Creates one data policy (alert) on a sensor — conditions, destination chats, notification schedule. The policy id is server-generated; the response echoes the STORED policy. The write merges into one atomic full-list sensor update: every other policy of the sensor rides through untouched. Fails with 422-class errors (condition-vs-type, chat availability, schedule references) and 409 for template-owned conflicts.")]
        public async System.Threading.Tasks.Task<PolicyDto> CreateSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The policy content — conditions, destination, notification, icon/status settings. Use list_chats to discover valid destination chat ids.")] PolicyDto policy) =>
            McpToolWriteErrors.Unwrap(await _writer.CreateSensorPolicyAsync(sensorId, policy, User));

        [McpServerTool(Name = "update_sensor_policy", ReadOnly = false, Idempotent = true, OpenWorld = false)]
        [Description("Replaces one data policy's content (full replace at item granularity; the id stays). Template-owned policies accept only the isDisabled toggle — anything else fails with a conflict. The response echoes the stored policy.")]
        public async System.Threading.Tasks.Task<PolicyDto> UpdateSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Policy id to replace.")] Guid policyId,
            [Description("The new policy content.")] PolicyDto policy) =>
            McpToolWriteErrors.Unwrap(await _writer.UpdateSensorPolicyAsync(sensorId, policyId, policy, User));

        [McpServerTool(Name = "delete_sensor_policy", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Removes one data policy; every other policy of the sensor rides through untouched.")]
        public async System.Threading.Tasks.Task<McpDeletedResult> DeleteSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Policy id.")] Guid policyId)
        {
            McpToolWriteErrors.Unwrap(await _writer.DeleteSensorPolicyAsync(sensorId, policyId, User));

            return new McpDeletedResult { Id = policyId };
        }


        [McpServerTool(Name = "create_sensor_ttl_policy", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Creates one TTL (inactivity) policy on a sensor: an explicit interval or inherit:true (the reset-to-parent switch) — never both, never neither.")]
        public async System.Threading.Tasks.Task<TtlPolicyDto> CreateSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The TTL policy — `interval` (TimeSpan string) or `inherit: true`.")] TtlPolicyDto policy) =>
            McpToolWriteErrors.Unwrap(await _writer.CreateSensorTtlPolicyAsync(sensorId, policy, User));

        [McpServerTool(Name = "update_sensor_ttl_policy", ReadOnly = false, Idempotent = true, OpenWorld = false)]
        [Description("Replaces one TTL policy's content (full replace at item granularity; the id stays).")]
        public async System.Threading.Tasks.Task<TtlPolicyDto> UpdateSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("TTL policy id to replace.")] Guid policyId,
            [Description("The new TTL policy content.")] TtlPolicyDto policy) =>
            McpToolWriteErrors.Unwrap(await _writer.UpdateSensorTtlPolicyAsync(sensorId, policyId, policy, User));

        [McpServerTool(Name = "delete_sensor_ttl_policy", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Removes one TTL (inactivity) policy; every other policy of the sensor rides through untouched.")]
        public async System.Threading.Tasks.Task<McpDeletedResult> DeleteSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("TTL policy id.")] Guid policyId)
        {
            McpToolWriteErrors.Unwrap(await _writer.DeleteSensorTtlPolicyAsync(sensorId, policyId, User));

            return new McpDeletedResult { Id = policyId };
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
