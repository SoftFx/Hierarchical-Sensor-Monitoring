using System;
using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
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


        [McpServerTool(Name = "create_sensor_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Creates one data policy (alert) on a sensor — conditions, destination chats, notification schedule. The policy id is server-generated; the response echoes the STORED policy. The write merges into one atomic full-list sensor update: every other policy of the sensor rides through untouched. Fails with 422-class errors (condition-vs-type, chat availability, schedule references) and 409 for template-owned conflicts.")]
        public async System.Threading.Tasks.Task<object> CreateSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The policy content — conditions, destination, notification, icon/status settings. Use list_chats to discover valid destination chat ids.")] PolicyDto policy,
            [Description("Group this write under a change set id - the undo handle echoed as changeSetId. Omit to journal the write standalone.")] string changeSetId = null,
            [Description("Validate and report the affected entities WITHOUT applying anything.")] bool dryRun = false)
        {
            var result = await _writer.CreateSensorPolicyAsync(sensorId, policy, User, WriteOptionsOf(changeSetId, dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun ? result.Preview : result.Value with { ChangeSetId = result.ChangeSetId };
        }

        [McpServerTool(Name = "update_sensor_policy", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Replaces one data policy's content (full replace at item granularity; the id stays). Template-owned policies accept only the isDisabled toggle — anything else fails with a conflict. The response echoes the stored policy.")]
        public async System.Threading.Tasks.Task<object> UpdateSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Policy id to replace.")] Guid policyId,
            [Description("The new policy content.")] PolicyDto policy,
            [Description("Group this write under a change set id - the undo handle echoed as changeSetId. Omit to journal the write standalone.")] string changeSetId = null,
            [Description("Validate and report the affected entities WITHOUT applying anything.")] bool dryRun = false)
        {
            var result = await _writer.UpdateSensorPolicyAsync(sensorId, policyId, policy, User, WriteOptionsOf(changeSetId, dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun ? result.Preview : result.Value with { ChangeSetId = result.ChangeSetId };
        }

        [McpServerTool(Name = "delete_sensor_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes one data policy; every other policy of the sensor rides through untouched.")]
        public async System.Threading.Tasks.Task<object> DeleteSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Policy id.")] Guid policyId,
            [Description("Group this write under a change set id - the undo handle echoed as changeSetId. Omit to journal the write standalone.")] string changeSetId = null,
            [Description("Validate and report the affected entities WITHOUT applying anything.")] bool dryRun = false)
        {
            var result = await _writer.DeleteSensorPolicyAsync(sensorId, policyId, User, WriteOptionsOf(changeSetId, dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun ? result.Preview
                : new McpDeletedResult { Id = policyId, ChangeSetId = result.ChangeSetId };
        }


        [McpServerTool(Name = "create_sensor_ttl_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Creates one TTL (inactivity) policy on a sensor: an explicit interval or inherit:true (the reset-to-parent switch) — never both, never neither.")]
        public async System.Threading.Tasks.Task<object> CreateSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The TTL policy — `interval` (TimeSpan string) or `inherit: true`.")] TtlPolicyDto policy,
            [Description("Group this write under a change set id - the undo handle echoed as changeSetId. Omit to journal the write standalone.")] string changeSetId = null,
            [Description("Validate and report the affected entities WITHOUT applying anything.")] bool dryRun = false)
        {
            var result = await _writer.CreateSensorTtlPolicyAsync(sensorId, policy, User, WriteOptionsOf(changeSetId, dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun ? result.Preview : result.Value with { ChangeSetId = result.ChangeSetId };
        }

        [McpServerTool(Name = "update_sensor_ttl_policy", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Replaces one TTL policy's content (full replace at item granularity; the id stays).")]
        public async System.Threading.Tasks.Task<object> UpdateSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("TTL policy id to replace.")] Guid policyId,
            [Description("The new TTL policy content.")] TtlPolicyDto policy,
            [Description("Group this write under a change set id - the undo handle echoed as changeSetId. Omit to journal the write standalone.")] string changeSetId = null,
            [Description("Validate and report the affected entities WITHOUT applying anything.")] bool dryRun = false)
        {
            var result = await _writer.UpdateSensorTtlPolicyAsync(sensorId, policyId, policy, User, WriteOptionsOf(changeSetId, dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun ? result.Preview : result.Value with { ChangeSetId = result.ChangeSetId };
        }

        [McpServerTool(Name = "delete_sensor_ttl_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes one TTL (inactivity) policy; every other policy of the sensor rides through untouched.")]
        public async System.Threading.Tasks.Task<object> DeleteSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("TTL policy id.")] Guid policyId,
            [Description("Group this write under a change set id - the undo handle echoed as changeSetId. Omit to journal the write standalone.")] string changeSetId = null,
            [Description("Validate and report the affected entities WITHOUT applying anything.")] bool dryRun = false)
        {
            var result = await _writer.DeleteSensorTtlPolicyAsync(sensorId, policyId, User, WriteOptionsOf(changeSetId, dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun ? result.Preview
                : new McpDeletedResult { Id = policyId, ChangeSetId = result.ChangeSetId };
        }



    // #1570 helpers shared by the journaled write tools: the optional
    // grouping/dry-run arguments, and the success arms echoing the
    // change-set id on the structured content.
    internal static ChangeSetWriteOptions WriteOptionsOf(string changeSetId, bool dryRun)
    {
        var id = Guid.TryParse(changeSetId, out var parsed) ? parsed : (Guid?)null;

        return id is null && !dryRun ? null : new ChangeSetWriteOptions { ChangeSetId = id, DryRun = dryRun };
    }

    internal static object JournaledValue<T>(PolicyWriteResult<T> result) =>
        result.IsDryRun ? result.Preview : result.Value;


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
