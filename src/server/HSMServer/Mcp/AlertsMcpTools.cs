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
    /// The alert tools of the MCP surface — four reads (#1391) and, since the
    /// phase-2 write surface, six writes: template and schedule CRUD. All ten
    /// are thin renderings of the transport-agnostic services the REST
    /// controllers run on (<see cref="AlertReadService"/> for the reads,
    /// <see cref="AlertTemplateAdministrationService"/> and
    /// <see cref="AlertScheduleAdministrationService"/> for the writes — the
    /// #1393 one-decision-two-transports pattern).
    /// </summary>
    // The same bearer token, the same owner sight, the same DTOs as the REST
    // surface; expected failures surface as tool errors (isError) for the
    // calling agent to self-correct, never as protocol-level crashes. Write
    // authorization is inside the services (folder boundary for templates,
    // the admin-only Global boundary for schedules); read-write tokens are
    // enforced at the /mcp policy backstop (every MCP message is a POST).
    [McpServerToolType]
    public sealed class AlertsMcpTools
    {
        private readonly AlertReadService _reader;
        private readonly AlertTemplateAdministrationService _templateWriter;
        private readonly AlertScheduleAdministrationService _scheduleWriter;
        private readonly IHttpContextAccessor _http;

        public AlertsMcpTools(AlertReadService reader, AlertTemplateAdministrationService templateWriter,
            AlertScheduleAdministrationService scheduleWriter, IHttpContextAccessor http)
        {
            _reader = reader;
            _templateWriter = templateWriter;
            _scheduleWriter = scheduleWriter;
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


        [McpServerTool(Name = "create_alert_template", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Creates an alert template and applies its policies to every matching sensor tree-wide. The template id is server-generated; the response echoes the STORED template (normalized ids, canonical chat names). Requires the owner's write access at the target folder; use list_folders to discover valid folderId values and list_chats for destination chat ids.")]
        public async System.Threading.Tasks.Task<AlertTemplateDto> CreateAlertTemplateAsync(
            [Description("The template — name (globally unique), folderId, sensor type, path patterns, policies and TTL entries.")] AlertTemplateDto template) =>
            McpToolWriteErrors.Unwrap(await _templateWriter.CreateTemplateAsync(User, template));

        [McpServerTool(Name = "update_alert_template", ReadOnly = false, Idempotent = true, OpenWorld = false)]
        [Description("Updates an alert template (upsert semantics; the route id is the identity). Moving it to another folder requires the owner's write access on BOTH folders. Re-applies policies to matching sensors; the response echoes the stored template.")]
        public async System.Threading.Tasks.Task<AlertTemplateDto> UpdateAlertTemplateAsync(
            [Description("Template id.")] Guid templateId,
            [Description("The new template content; the body id, when carried, must equal templateId.")] AlertTemplateDto template) =>
            McpToolWriteErrors.Unwrap(await _templateWriter.UpdateTemplateAsync(templateId, User, template));

        [McpServerTool(Name = "delete_alert_template", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Deletes an alert template and strips its per-sensor policies tree-wide. Requires the owner's write access at the template's folder.")]
        public async System.Threading.Tasks.Task<McpDeletedResult> DeleteAlertTemplateAsync(
            [Description("Template id.")] Guid templateId)
        {
            McpToolWriteErrors.Unwrap(await _templateWriter.DeleteTemplateAsync(templateId, User));

            return new McpDeletedResult { Id = templateId };
        }


        [McpServerTool(Name = "create_alert_schedule", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Creates an alert schedule (working-time window). The id is server-generated; the response echoes the stored schedule. Requires an ADMIN owner and a read-write token (the Global boundary is admin-only). Name must be globally unique; the YAML body must satisfy the parser's window rules; the timezone is a validated IANA id.")]
        public async System.Threading.Tasks.Task<AlertScheduleDto> CreateAlertScheduleAsync(
            [Description("The schedule — name, timezone, schedule (YAML).")] AlertScheduleUpsertDto schedule) =>
            McpToolWriteErrors.Unwrap(await _scheduleWriter.CreateScheduleAsync(User, schedule));

        [McpServerTool(Name = "update_alert_schedule", ReadOnly = false, Idempotent = true, OpenWorld = false)]
        [Description("Updates an alert schedule (upsert at a known id); the response echoes the stored schedule. Requires an ADMIN owner and a read-write token.")]
        public async System.Threading.Tasks.Task<AlertScheduleDto> UpdateAlertScheduleAsync(
            [Description("Schedule id.")] Guid scheduleId,
            [Description("The new schedule content — name, timezone, schedule (YAML).")] AlertScheduleUpsertDto schedule) =>
            McpToolWriteErrors.Unwrap(await _scheduleWriter.UpdateScheduleAsync(scheduleId, User, schedule));

        [McpServerTool(Name = "delete_alert_schedule", ReadOnly = false, Idempotent = false, OpenWorld = false)]
        [Description("Deletes an alert schedule after detaching it from every referencing policy; on an incomplete detach the schedule survives and the error says so (delete again to retry). Requires an ADMIN owner and a read-write token.")]
        public async System.Threading.Tasks.Task<McpDeletedResult> DeleteAlertScheduleAsync(
            [Description("Schedule id.")] Guid scheduleId)
        {
            McpToolWriteErrors.Unwrap(await _scheduleWriter.DeleteScheduleAsync(scheduleId, User));

            return new McpDeletedResult { Id = scheduleId };
        }


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
