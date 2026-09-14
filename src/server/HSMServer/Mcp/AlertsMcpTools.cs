using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Schedule;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The four alert tools of the read-only MCP surface (#1391): alert
    /// templates and alert schedules as the REST surface serves them. Per the
    /// #1391 design these tools use the existing thin providers directly (the
    /// shared-service extraction is scoped to the sensor tree); the list/get
    /// logic below mirrors AlertTemplatesApiController/AlertSchedulesApiController.
    /// </summary>
    // The same authorization semantics as the REST controllers (#1391):
    //  - templates are folder-scoped — an item is listed exactly when GET
    //    would answer it, out-of-sight folders are silently absent, unknown and
    //    invisible ids answer the same error;
    //  - schedules are gated caller-wide (the owner is an admin or sees at
    //    least one boundary) and their sensor references are filtered to the
    //    owner's sight through the sensor's product boundary.
    [McpServerToolType]
    public sealed class AlertsMcpTools
    {
        private readonly ITreeValuesCache _cache;
        private readonly IAlertScheduleProvider _schedules;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IHttpContextAccessor _http;

        public AlertsMcpTools(ITreeValuesCache cache, IAlertScheduleProvider schedules,
            IApiTokenAuthorizationService authorization, IHttpContextAccessor http)
        {
            _cache = cache;
            _schedules = schedules;
            _authorization = authorization;
            _http = http;
        }


        [McpServerTool(Name = "list_alert_templates", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists alert templates visible to the token's owner, ordered by name. A template carries path patterns, policies (conditions) and destinations — enough to understand what alerts exist for which sensors.")]
        public McpAlertTemplatesResult ListAlertTemplates(
            [Description("Maximum templates to return (1..200, default 20); totalFound carries the full count.")] int limit = HsmMcp.DefaultLimit)
        {
            var user = User;

            // The list predicate is the owner-sight half of the item read decision
            // (mirrors the REST controller): memoized per DISTINCT folder —
            // templates cluster into a handful of folders and the evaluator
            // re-resolves user + token on every call.
            var decisionByFolder = new Dictionary<Guid, bool>();

            bool IsListable(Guid folderId) =>
                decisionByFolder.TryGetValue(folderId, out var listable)
                    ? listable
                    : decisionByFolder[folderId] = _authorization.IsVisible(user, FolderResource(folderId));

            var visible = (_cache.GetAlertTemplateModels() ?? [])
                .Where(template => IsListable(template.FolderId))
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.Id)
                .ToList();

            return new McpAlertTemplatesResult
            {
                Templates = [.. visible.Take(HsmMcp.NormalizeLimit(limit)).Select(AlertTemplateDtoMapper.ToDto)],
                TotalFound = visible.Count,
            };
        }


        [McpServerTool(Name = "get_alert_template", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one alert template by id. Unknown and invisible ids answer the same error.")]
        public AlertTemplateDto GetAlertTemplate(
            [Description("Template id.")] Guid templateId)
        {
            var template = _cache.GetAlertTemplate(templateId);

            if (template is null)
                throw new McpException(ManagementApiErrors.NotFoundMessage);

            // Reads authorize at the template's FOLDER through the Product
            // boundary (mirrors the REST controller): the evaluator's 404 arm
            // keeps invisible indistinguishable from unknown; the Forbidden arm
            // is unreachable in the owner-mirror read model and kept only so
            // the full decision surface maps to a response.
            return _authorization.AuthorizeRead(User, FolderResource(template.FolderId)) switch
            {
                ApiTokenAuthorization.Allowed => AlertTemplateDtoMapper.ToDto(template),
                ApiTokenAuthorization.Forbidden => throw new McpException("The token's owner cannot see this template."),
                _ => throw new McpException(ManagementApiErrors.NotFoundMessage),
            };
        }


        [McpServerTool(Name = "list_alert_schedules", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists alert schedules (working-time windows that gate template policies), ordered by name; each schedule's sensor list carries only paths the token's owner may see.")]
        public McpAlertSchedulesResult ListAlertSchedules(
            [Description("Maximum schedules to return (1..200, default 20); totalFound carries the full count.")] int limit = HsmMcp.DefaultLimit)
        {
            var user = User;

            if (!AuthorizeSchedulesRead())
                throw new McpException("The token's owner cannot see any product or folder.");

            var all = (_schedules.GetAllSchedules() ?? [])
                .OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(schedule => schedule.Id)
                .ToList();

            var pageItems = all.Take(HsmMcp.NormalizeLimit(limit)).ToList();

            // The page's sensor references are resolved in ONE pass over the
            // sensor cache (the per-id lookup scans every sensor, so per-item
            // calls would be a full scan per schedule), and the visibility
            // decision is memoized per DISTINCT product (mirrors the REST
            // controller's list).
            var sensorsBySchedule = _cache.GetSensorsByAlertSchedules([.. pageItems.Select(s => s.Id)]) ?? [];
            var isProductVisible = _authorization.MemoizedProductVisibility(user);

            return new McpAlertSchedulesResult
            {
                Schedules = [.. pageItems.Select(schedule => ToDto(schedule,
                    sensorsBySchedule.TryGetValue(schedule.Id, out var sensors) ? sensors : null,
                    isProductVisible))],
                TotalFound = all.Count,
            };
        }


        [McpServerTool(Name = "get_alert_schedule", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one alert schedule by id (same caller-wide gate as the list); its sensor list carries only paths the token's owner may see.")]
        public AlertScheduleDto GetAlertSchedule(
            [Description("Schedule id.")] Guid scheduleId)
        {
            if (!AuthorizeSchedulesRead())
                throw new McpException("The token's owner cannot see any product or folder.");

            var schedule = _schedules.GetSchedule(scheduleId);

            if (schedule is null)
                throw new McpException(ManagementApiErrors.NotFoundMessage);

            return ToDto(schedule, _cache.GetSensorsByAlertSchedule(scheduleId),
                _authorization.MemoizedProductVisibility(User));
        }


        // The caller-wide gate lives in the evaluator (mirrors the REST
        // controller): the owner is an admin or currently holds a role on at
        // least one product/folder, recording one AuthorizationDenied when
        // nothing qualifies.
        private bool AuthorizeSchedulesRead() =>
            _authorization.CanSeeAnyBoundary(User);

        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);


        private static ApiTokenResource FolderResource(Guid folderId) =>
            new(ApiTokenResourceKind.Folder, folderId);

        private AlertScheduleDto ToDto(Core.Model.Policies.AlertSchedule schedule,
            List<Core.Model.BaseSensorModel> sensors, Func<Guid, bool> isProductVisible)
        {
            // Sensor references filtered to the owner's sight — resolved exactly
            // the way the evaluator resolves sensors: through the sensor's
            // product's current boundary. Parentless sensors fail closed
            // (dropped from the list).
            var visiblePaths = (sensors ?? [])
                .Where(sensor => sensor.Parent?.Root is { } product && isProductVisible(product.Id))
                .Select(sensor => sensor.FullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return AlertScheduleDtoMapper.ToDto(schedule, visiblePaths);
        }
    }
}
