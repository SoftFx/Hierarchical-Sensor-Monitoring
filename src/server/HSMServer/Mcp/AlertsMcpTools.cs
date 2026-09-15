using System;
using System.ComponentModel;
using System.Security.Claims;
using System.Threading;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Alerts;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The four alert tools of the read-only MCP surface (#1391): templates and
    /// schedules as the REST surface serves them — a thin rendering of
    /// <see cref="AlertReadService"/> (#1393: the per-folder sight, the
    /// caller-wide gate and the sensor-path filter had existed in two copies,
    /// a security-relevant drift risk).
    /// </summary>
    // The same bearer token, the same owner sight, the same DTOs as the REST
    // surface; expected failures surface as tool errors (isError) for the
    // calling agent to self-correct, never as protocol-level crashes.
    [McpServerToolType]
    public sealed class AlertsMcpTools
    {
        private readonly AlertReadService _reader;
        private readonly IHttpContextAccessor _http;

        public AlertsMcpTools(AlertReadService reader, IHttpContextAccessor http)
        {
            _reader = reader;
            _http = http;
        }


        [McpServerTool(Name = "list_alert_templates", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists alert templates visible to the token's owner, ordered by name. A template carries path patterns, policies (conditions) and destinations — enough to understand what alerts exist for which sensors. Templates have no narrowing dimension, so `page` walks beyond the limit.")]
        public McpAlertTemplatesResult ListAlertTemplates(
            [Description("Maximum templates to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1,
            CancellationToken cancellationToken = default)
        {
            var result = _reader.ListTemplates(User, HsmMcp.NormalizePage(page), HsmMcp.NormalizeLimit(limit),
                cancellationToken);

            return new McpAlertTemplatesResult
            {
                Templates = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }


        [McpServerTool(Name = "get_alert_template", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one alert template by id. Unknown and invisible ids answer the same error.")]
        public AlertTemplateDto GetAlertTemplate(
            [Description("Template id.")] Guid templateId) =>
            McpToolErrors.Unwrap(_reader.GetTemplate(templateId, User));


        [McpServerTool(Name = "list_alert_schedules", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists alert schedules (working-time windows that gate template policies), ordered by name; each schedule's sensor list carries only paths the token's owner may see. Schedules have no narrowing dimension, so `page` walks beyond the limit.")]
        public McpAlertSchedulesResult ListAlertSchedules(
            [Description("Maximum schedules to return (1..200, default 20); the result echoes the effective limit, the served page and totalPages alongside totalFound.")] int limit = HsmMcp.DefaultLimit,
            [Description("1-based page when totalFound exceeds the limit; clamped to the last page.")] int page = 1,
            CancellationToken cancellationToken = default)
        {
            var result = McpToolErrors.Unwrap(_reader.ListSchedules(User, HsmMcp.NormalizePage(page),
                HsmMcp.NormalizeLimit(limit), cancellationToken));

            return new McpAlertSchedulesResult
            {
                Schedules = result.Items,
                TotalFound = result.TotalCount,
                Limit = result.PageSize,
                Page = result.Page,
                TotalPages = result.TotalPages,
            };
        }


        [McpServerTool(Name = "get_alert_schedule", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one alert schedule by id (same caller-wide gate as the list); its sensor list carries only paths the token's owner may see.")]
        public AlertScheduleDto GetAlertSchedule(
            [Description("Schedule id.")] Guid scheduleId) =>
            McpToolErrors.Unwrap(_reader.GetSchedule(scheduleId, User));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
