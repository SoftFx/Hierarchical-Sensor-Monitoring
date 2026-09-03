using System;
using System.Linq;
using System.Security.Claims;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertSchedules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    // Read-only REST surface for alert schedules (#1352, epic #1347) — the second
    // /api/v1 resource controller; area conventions are identical to
    // AlertTemplatesApiController (see aicontext/features/server/management-api/).
    // Writes are deliberately out of scope for v1 (schedule parser/timezone
    // complexity is a follow-up).
    //
    // Authorization differs from folder-scoped resources: schedules are GLOBAL, and
    // the web UI shows them to every logged-in user. The token-side equivalent of
    // "this principal may work with alerts" is an alerts:read grant at ANY boundary
    // the owner can currently see — the intersection itself is still decided by the
    // evaluator's sanctioned list predicate (owner visibility, token liveness and
    // grant reach all inside). Nothing about schedule existence is per-caller
    // scoped, so an entitled caller gets a plain 404 for an unknown id while an
    // unentitled one gets 403 for every id.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/alertSchedules")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertSchedulesApiController : ControllerBase
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        private readonly IAlertScheduleProvider _schedules;
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenManager _tokens;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly ILogger<AlertSchedulesApiController> _logger;

        public AlertSchedulesApiController(IAlertScheduleProvider schedules, ITreeValuesCache cache,
            IApiTokenManager tokens, IApiTokenAuthorizationService authorization,
            ILogger<AlertSchedulesApiController> logger)
        {
            _schedules = schedules;
            _cache = cache;
            _tokens = tokens;
            _authorization = authorization;
            _logger = logger;
        }


        [HttpGet]
        public IActionResult GetSchedules(int page = 1, int pageSize = DefaultPageSize)
        {
            var failure = AuthorizeSchedulesRead();

            if (failure is not null)
                return failure;

            page = Math.Max(page, 1);
            pageSize = Math.Min(pageSize <= 0 ? DefaultPageSize : pageSize, MaxPageSize);

            var all = (_schedules.GetAllSchedules() ?? [])
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Id)
                .ToList();

            var totalPages = all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)pageSize);

            // Clamp the page into [1, totalPages]: unchecked (page - 1) * pageSize
            // would overflow int for huge page numbers, and a NEGATIVE Skip count
            // silently returns the FIRST page labeled as page N.
            page = Math.Min(page, Math.Max(totalPages, 1));

            return Ok(new ApiPageDto<AlertScheduleDto>
            {
                Items = [.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            });
        }

        [HttpGet("{id:guid}")]
        public IActionResult GetSchedule(Guid id)
        {
            var failure = AuthorizeSchedulesRead();

            if (failure is not null)
                return failure;

            var schedule = _schedules.GetSchedule(id);

            return schedule is null ? NotFound() : Ok(ToDto(schedule));
        }


        // The caller may read schedules when ANY of the token's alerts:read grants sits
        // at a boundary the owner can currently see. Candidate boundaries come from the
        // token's own grants (public projection), so no grant logic is duplicated — the
        // decision per candidate is the evaluator's IsVisible under the same operation.
        private IActionResult AuthorizeSchedulesRead()
        {
            var tokenId = User.FindFirst(HsmApiTokenClaims.TokenId)?.Value;
            var token = tokenId is null ? null : _tokens.GetToken(tokenId);

            if (token is not null)
            {
                foreach (var grant in token.Grants)
                {
                    if (grant.Operation != ApiTokenOperations.AlertsRead)
                        continue;

                    if (!TryGrantResource(grant, out var resource))
                        continue;

                    if (_authorization.IsVisible(User, ApiTokenOperations.AlertsRead, resource))
                        return null;
                }
            }

            // One denial audit record per request: the evaluator records security events
            // only through Authorize, and the global boundary is the closest scope for a
            // global resource. The 403 itself is returned regardless of that decision —
            // schedule existence is not a per-caller secret.
            _ = _authorization.Authorize(User, ApiTokenOperations.AlertsRead, new ApiTokenResource(ApiTokenResourceKind.Global));

            return Problem(statusCode: 403,
                detail: "The token does not grant 'alerts:read' at any boundary accessible to its owner.");
        }

        private static bool TryGrantResource(ApiTokenGrantEntity grant, out ApiTokenResource resource)
        {
            switch ((ApiTokenBoundaryKind)grant.BoundaryKind)
            {
                case ApiTokenBoundaryKind.Global when string.IsNullOrEmpty(grant.BoundaryId):
                    resource = new(ApiTokenResourceKind.Global);
                    return true;

                case ApiTokenBoundaryKind.Product when Guid.TryParse(grant.BoundaryId, out var productId):
                    resource = new(ApiTokenResourceKind.Product, productId);
                    return true;

                case ApiTokenBoundaryKind.Folder when Guid.TryParse(grant.BoundaryId, out var folderId):
                    resource = new(ApiTokenResourceKind.Folder, folderId);
                    return true;

                default:
                    resource = null;
                    return false;
            }
        }

        private AlertScheduleDto ToDto(Core.Model.Policies.AlertSchedule schedule)
        {
            // Sensor references filtered to the caller's sight under the SAME operation
            // the resource demands (alerts:read) — mere reach is not enough: a token
            // granted only, say, dashboards:read at a product must not learn its sensor
            // paths from an alerts response. Resolved exactly the way the evaluator
            // resolves sensors: through the sensor's product's current boundary.
            // Parentless sensors fail closed (dropped from the list).
            var visiblePaths = (_cache.GetSensorsByAlertSchedule(schedule.Id) ?? [])
                .Where(sensor => sensor.Parent?.Root is { } product &&
                    _authorization.IsVisible(User, ApiTokenOperations.AlertsRead, new ApiTokenResource(ApiTokenResourceKind.Product, product.Id)))
                .Select(sensor => sensor.FullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return AlertScheduleDtoMapper.ToDto(schedule, visiblePaths);
        }
    }
}
