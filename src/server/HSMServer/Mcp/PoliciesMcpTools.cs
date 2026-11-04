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
    /// The policy tools of the MCP surface: two list reads — the id
    /// discovery half without which the update/delete tools were one-shot
    /// (#1615; an id existed only inside the create response of the session
    /// that created it) — and the six phase-2 write tools: data-policy and
    /// TTL-policy CRUD on a sensor. Thin delegations to the shared services
    /// (<see cref="AlertReadService"/> for the reads,
    /// <see cref="PolicyAdministrationService"/> (#1500) for the writes), the
    /// same engines the REST policy controllers run on.
    /// </summary>
    // The authorization is REUSED, not re-implemented: every service call
    // resolves the caller's owner through IApiTokenAuthorizationService at
    // the sensor's boundary (the 404/403 anti-enumeration split and the
    // security-event recording live inside). Read-write enforcement needs no
    // tool-level check: /mcp serves every message via POST, and the
    // HsmApiToken management policy rejects read-only tokens on unsafe
    // methods outright. Product-policy writes stay REST-only (a deliberate
    // bound: they route to the owning sensor's list anyway).
    //
    // #1588: every write journals under a server-minted per-call undo group
    // (one tool call = one rollback group); there is no client-facing
    // changeSetId argument or echo anymore — undo is addressed through
    // journal records.
    [McpServerToolType]
    public sealed class PoliciesMcpTools
    {
        private readonly AlertReadService _reader;
        private readonly PolicyAdministrationService _writer;
        private readonly IHttpContextAccessor _http;

        public PoliciesMcpTools(AlertReadService reader, PolicyAdministrationService writer, IHttpContextAccessor http)
        {
            _reader = reader;
            _writer = writer;
            _http = http;
        }


        [McpServerTool(Name = "list_sensor_policies", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists one sensor's data policies (alerts), ordered by policy id — every policy is listed, template-owned ones included with their template linkage. This is the id source for update_sensor_policy and delete_sensor_policy: policies created earlier (via REST, the Web UI, a template apply or a previous MCP session) are otherwise unreachable.")]
        public McpSensorPoliciesResult ListSensorPolicies(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Maximum policies to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1)
        {
            var result = McpToolErrors.Unwrap(_reader.ListSensorPolicies(User, sensorId,
                HsmMcp.NormalizePage(page), HsmMcp.NormalizeLimit(limit)));

            return new McpSensorPoliciesResult
            {
                Policies = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }

        [McpServerTool(Name = "list_sensor_ttl_policies", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists one sensor's TTL (inactivity) policies, ordered by policy id — the id source for update_sensor_ttl_policy and delete_sensor_ttl_policy.")]
        public McpSensorTtlPoliciesResult ListSensorTtlPolicies(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Maximum policies to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1)
        {
            var result = McpToolErrors.Unwrap(_reader.ListSensorTtlPolicies(User, sensorId,
                HsmMcp.NormalizePage(page), HsmMcp.NormalizeLimit(limit)));

            return new McpSensorTtlPoliciesResult
            {
                TtlPolicies = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }


        [McpServerTool(Name = "create_sensor_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Creates one data policy (alert) on a sensor — conditions, destination chats, notification schedule. The policy id is server-generated; the response's `value` echoes the STORED policy. The write merges into one atomic full-list sensor update: every other policy of the sensor rides through untouched. Fails with 422-class errors (condition-vs-type, chat availability, schedule references) and 409 for template-owned conflicts.")]
        public async System.Threading.Tasks.Task<McpWriteResult<PolicyDto>> CreateSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The policy content — conditions, destination, notification, icon/status settings. Use list_chats to discover valid destination chat ids.")] PolicyDto policy,
            [Description("Validate and report the affected entities WITHOUT applying anything (the answer carries `preview` instead of `value`).")] bool dryRun = false)
        {
            var result = await _writer.CreateSensorPolicyAsync(sensorId, policy, User, WriteOptionsOf(dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = result.Preview }
                : new() { Value = result.Value };
        }

        [McpServerTool(Name = "update_sensor_policy", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Replaces one data policy's content (full replace at item granularity; the id stays). Template-owned policies accept only the isDisabled toggle — anything else fails with a conflict. The response's `value` echoes the stored policy.")]
        public async System.Threading.Tasks.Task<McpWriteResult<PolicyDto>> UpdateSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Policy id to replace.")] Guid policyId,
            [Description("The new policy content.")] PolicyDto policy,
            [Description("Validate and report the affected entities WITHOUT applying anything (the answer carries `preview` instead of `value`).")] bool dryRun = false)
        {
            var result = await _writer.UpdateSensorPolicyAsync(sensorId, policyId, policy, User, WriteOptionsOf(dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = result.Preview }
                : new() { Value = result.Value };
        }

        [McpServerTool(Name = "delete_sensor_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes one data policy; every other policy of the sensor rides through untouched.")]
        public async System.Threading.Tasks.Task<McpWriteResult<McpDeletedResult>> DeleteSensorPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Policy id.")] Guid policyId,
            [Description("Validate and report the affected entities WITHOUT applying anything (the answer carries `preview` instead of `value`).")] bool dryRun = false)
        {
            var result = await _writer.DeleteSensorPolicyAsync(sensorId, policyId, User, WriteOptionsOf(dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = result.Preview }
                : new() { Value = new McpDeletedResult { Id = policyId } };
        }


        [McpServerTool(Name = "create_sensor_ttl_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Creates one TTL (inactivity) policy on a sensor: an explicit interval or inherit:true (the reset-to-parent switch) — never both, never neither.")]
        public async System.Threading.Tasks.Task<McpWriteResult<TtlPolicyDto>> CreateSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The TTL policy — `interval` (TimeSpan string) or `inherit: true`.")] TtlPolicyDto policy,
            [Description("Validate and report the affected entities WITHOUT applying anything (the answer carries `preview` instead of `value`).")] bool dryRun = false)
        {
            var result = await _writer.CreateSensorTtlPolicyAsync(sensorId, policy, User, WriteOptionsOf(dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = result.Preview }
                : new() { Value = result.Value };
        }

        [McpServerTool(Name = "update_sensor_ttl_policy", ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Replaces one TTL policy's content (full replace at item granularity; the id stays).")]
        public async System.Threading.Tasks.Task<McpWriteResult<TtlPolicyDto>> UpdateSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("TTL policy id to replace.")] Guid policyId,
            [Description("The new TTL policy content.")] TtlPolicyDto policy,
            [Description("Validate and report the affected entities WITHOUT applying anything (the answer carries `preview` instead of `value`).")] bool dryRun = false)
        {
            var result = await _writer.UpdateSensorTtlPolicyAsync(sensorId, policyId, policy, User, WriteOptionsOf(dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = result.Preview }
                : new() { Value = result.Value };
        }

        [McpServerTool(Name = "delete_sensor_ttl_policy", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes one TTL (inactivity) policy; every other policy of the sensor rides through untouched.")]
        public async System.Threading.Tasks.Task<McpWriteResult<McpDeletedResult>> DeleteSensorTtlPolicyAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("TTL policy id.")] Guid policyId,
            [Description("Validate and report the affected entities WITHOUT applying anything (the answer carries `preview` instead of `value`).")] bool dryRun = false)
        {
            var result = await _writer.DeleteSensorTtlPolicyAsync(sensorId, policyId, User, WriteOptionsOf(dryRun));

            McpToolWriteErrors.Unwrap(result);

            return result.IsDryRun
                ? new() { Preview = result.Preview }
                : new() { Value = new McpDeletedResult { Id = policyId } };
        }


    // #1588: the per-call write options — every tool call journals under its
    // own server-minted undo group (one tool call = one rollback group).
    internal static ChangeSetWriteOptions WriteOptionsOf(bool dryRun) =>
        new() { ChangeSetId = Guid.NewGuid(), DryRun = dryRun };


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
