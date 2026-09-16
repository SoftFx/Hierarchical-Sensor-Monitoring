using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Schedule;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.SensorTree;

namespace HSMServer.Model.ManagementApi.Alerts
{
    /// <summary>
    /// The single implementation of the alert read surface (#1393): templates
    /// and schedules, list and item, with the visibility rules that decide what
    /// a caller may see. Transport-agnostic — the REST controllers and the MCP
    /// alert tools both call it; the caller supplies the token principal and
    /// gets DTOs or a <see cref="SensorTreeReadResult{T}"/> failure back, never
    /// an IActionResult (the shared read envelope of the management area).
    /// </summary>
    // Extracted from AlertTemplatesApiController/AlertSchedulesApiController
    // and the MCP AlertsMcpTools that mirrored them (#1392 review finding 7):
    // the per-folder template sight, the caller-wide schedules gate and the
    // sensor-path visibility filter existed in two places, and a future change
    // to those rules could drift between REST and MCP unreviewed — a VISIBILITY
    // drift, not a cosmetic one. The controllers' suites are the regression
    // net: the wire behavior they pin must not change.
    //
    // Visibility semantics (unchanged from both sources):
    //  - templates are folder-scoped — an item is listed exactly when GET
    //    would answer it, out-of-sight folders are silently absent, unknown and
    //    invisible ids answer the SAME NotFound;
    //  - schedules are gated caller-wide (the owner is an admin or currently
    //    holds a role on at least one product/folder — the evaluator's
    //    CanSeeAnyBoundary, with the denial audit record inside), and each
    //    schedule's sensor references are filtered through the sensor's
    //    product boundary, memoized per distinct product within one request.
    public sealed class AlertReadService
    {
        private readonly ITreeValuesCache _cache;
        private readonly IAlertScheduleProvider _schedules;
        private readonly IApiTokenAuthorizationService _authorization;

        public AlertReadService(ITreeValuesCache cache, IAlertScheduleProvider schedules,
            IApiTokenAuthorizationService authorization)
        {
            _cache = cache;
            _schedules = schedules;
            _authorization = authorization;
        }


        /// <summary>
        /// Templates visible to the token's owner, ordered by name then id,
        /// paginated with the area's shared clamps. An owner who sees nothing
        /// gets an empty list: out-of-sight folders are simply not listed,
        /// never a per-item failure.
        /// </summary>
        public ApiPageDto<AlertTemplateDto> ListTemplates(ClaimsPrincipal user, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            // The list predicate is the owner-sight half of the item read decision,
            // so an item is listed exactly when GET {id} would answer it. The
            // decision is memoized per DISTINCT folder (templates cluster into a
            // handful of folders, and the evaluator re-resolves user + token on
            // every call); IsVisible records nothing, unlike per-item
            // authorization.
            var decisionByFolder = new Dictionary<Guid, bool>();

            bool IsListable(Guid folderId) =>
                decisionByFolder.TryGetValue(folderId, out var listable)
                    ? listable
                    : decisionByFolder[folderId] = _authorization.IsVisible(user, FolderResource(folderId));

            var visible = (_cache.GetAlertTemplateModels() ?? [])
                .Where(template =>
                {
                    // A disconnected caller must not keep the per-item evaluator
                    // pass running (the sensor-tree scan's rule).
                    cancellationToken.ThrowIfCancellationRequested();

                    return IsListable(template.FolderId);
                })
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.Id)
                .ToList();

            var totalPages = ApiPagination.TotalPagesOf(visible.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return new ApiPageDto<AlertTemplateDto>
            {
                Items = [.. visible.Skip((page - 1) * pageSize).Take(pageSize).Select(AlertTemplateDtoMapper.ToDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = visible.Count,
                TotalPages = totalPages,
            };
        }


        /// <summary>
        /// One template by id. Unknown and invisible ids answer the SAME
        /// NotFound (the area's anti-enumeration rule).
        /// </summary>
        public SensorTreeReadResult<AlertTemplateDto> GetTemplate(Guid id, ClaimsPrincipal user)
        {
            var template = _cache.GetAlertTemplate(id);

            if (template is null)
                return SensorTreeReadResult<AlertTemplateDto>.Fail(SensorTreeReadOutcome.NotFound);

            // Reads authorize at the template's FOLDER through the Product
            // boundary: the evaluator's 404 arm keeps invisible
            // indistinguishable from unknown; the Forbidden arm is unreachable
            // in the owner-mirror read model and kept only so the evaluator's
            // full decision surface maps to a response.
            return _authorization.AuthorizeRead(user, FolderResource(template.FolderId)) switch
            {
                ApiTokenAuthorization.Allowed =>
                    SensorTreeReadResult<AlertTemplateDto>.Ok(AlertTemplateDtoMapper.ToDto(template)),
                ApiTokenAuthorization.Forbidden => SensorTreeReadResult<AlertTemplateDto>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: "The token's owner cannot see this template."),
                _ => SensorTreeReadResult<AlertTemplateDto>.Fail(SensorTreeReadOutcome.NotFound),
            };
        }


        /// <summary>
        /// All schedules, ordered by name then id, paginated with the area's
        /// shared clamps; each schedule's sensor list carries only paths the
        /// token's owner may see. Requires the caller-wide gate — an owner who
        /// sees no boundary at all gets Forbidden for every id, and the
        /// provider is never queried.
        /// </summary>
        public SensorTreeReadResult<ApiPageDto<AlertScheduleDto>> ListSchedules(ClaimsPrincipal user, int page,
            int pageSize, CancellationToken cancellationToken = default)
        {
            // The caller-wide gate lives in the evaluator: the owner is an admin
            // or currently holds a role on at least one product/folder,
            // recording one AuthorizationDenied (the plain 403 kind — never
            // the enumeration-probe kind) when nothing qualifies. Nothing about
            // schedule existence is per-caller scoped, so the denial says
            // nothing about any concrete id.
            if (!_authorization.CanSeeAnyBoundary(user))
                return SensorTreeReadResult<ApiPageDto<AlertScheduleDto>>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: NoBoundarySightMessage);

            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            var all = (_schedules.GetAllSchedules() ?? [])
                .OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(schedule => schedule.Id)
                .ToList();

            var totalPages = ApiPagination.TotalPagesOf(all.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            cancellationToken.ThrowIfCancellationRequested();

            // The page's sensor references are resolved in ONE pass over the
            // sensor cache (the per-id lookup scans every sensor, so per-item
            // calls would be a full scan per schedule), and the visibility
            // decision is memoized per DISTINCT product (sensors cluster into
            // few products; the evaluator re-resolves caller + token on every
            // call).
            var sensorsBySchedule = _cache.GetSensorsByAlertSchedules([.. pageItems.Select(s => s.Id)]) ?? [];
            var isProductVisible = _authorization.MemoizedProductVisibility(user);

            return SensorTreeReadResult<ApiPageDto<AlertScheduleDto>>.Ok(new ApiPageDto<AlertScheduleDto>
            {
                Items = [.. pageItems.Select(schedule =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return ToDto(schedule,
                        sensorsBySchedule.TryGetValue(schedule.Id, out var sensors) ? sensors : null,
                        isProductVisible);
                })],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            });
        }


        /// <summary>
        /// One schedule by id (same caller-wide gate as the list); its sensor
        /// list carries only paths the token's owner may see.
        /// </summary>
        public SensorTreeReadResult<AlertScheduleDto> GetSchedule(Guid id, ClaimsPrincipal user)
        {
            if (!_authorization.CanSeeAnyBoundary(user))
                return SensorTreeReadResult<AlertScheduleDto>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: NoBoundarySightMessage);

            var schedule = _schedules.GetSchedule(id);

            if (schedule is null)
                return SensorTreeReadResult<AlertScheduleDto>.Fail(SensorTreeReadOutcome.NotFound);

            return SensorTreeReadResult<AlertScheduleDto>.Ok(ToDto(schedule,
                _cache.GetSensorsByAlertSchedule(id), _authorization.MemoizedProductVisibility(user)));
        }


        // The caller-wide gate's 403 body — the list and the item answers must
        // stay the same string, single-sourced (#1395 review): the code this
        // service replaced had one Denied() helper for exactly that reason.
        private const string NoBoundarySightMessage = "The token's owner cannot see any product or folder.";

        private static ApiTokenResource FolderResource(Guid folderId) =>
            new(ApiTokenResourceKind.Folder, folderId);

        private static AlertScheduleDto ToDto(Core.Model.Policies.AlertSchedule schedule,
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
