using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.Core.Model;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Read-only sensor surface of the management API (#1386): flat recursive
    /// sensor search, one sensor with its current value, and the sensor's recent
    /// history. Bearer-token authenticated only (the HsmApiToken scheme); served on
    /// the web-UI port only. A token sees exactly the sensors its owner sees;
    /// unknown and invisible ids answer the SAME 404.
    /// </summary>
    // The AI-agent workhorse: "find network sensors in product X" is
    // GET /api/v1/sensors?product={X}&amp;search=network. Filters resolve on the
    // live cache — ConcurrentDictionary scans, no locking; the visibility decision
    // is memoized per DISTINCT root product within one request (sensors cluster
    // into few products and the evaluator re-resolves caller and token on every
    // call — the schedules-list pattern).
    //
    // History is the NEWEST maxPoints values of the window (no decimation, #1386):
    // the database streams a window oldest-first with no count primitive, so the
    // endpoint keeps a sliding buffer of the requested size and reports whether
    // anything older was dropped (truncated) — the agent narrows the window for
    // full resolution instead of the server guessing how to aggregate.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/sensors")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class SensorsApiController : ControllerBase
    {
        public const int DefaultMaxPoints = 1_000;
        public const int MaxPointsLimit = 10_000;

        // File metadata history pages rarely need thousands of entries, and
        // every scanned File row deserializes a full payload while holding the
        // per-sensor lock: a much tighter response bound for them (#1387 r4).
        public const int FileMaxPointsLimit = 100;

        // Whole-request ceiling on search evaluation (#1386 review): the regex
        // match timeout bounds one IsMatch; this bounds the full scan.
        public const int SearchBudgetMs = 2_000;

        private static readonly TimeSpan DefaultHistoryWindow = TimeSpan.FromHours(24);

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;

        public SensorsApiController(ITreeValuesCache cache, IApiTokenAuthorizationService authorization)
        {
            _cache = cache;
            _authorization = authorization;
        }


        /// <summary>
        /// Search sensors, paginated, ordered by full path then id. Without
        /// parameters: every sensor visible to the token's owner. With
        /// <c>product</c>: the subtree of that node (any product or folder id —
        /// unknown and invisible ids answer the uniform 404). The search text
        /// matches name, description and path (OR).
        /// </summary>
        /// <param name="product">Optional node id (root product or folder) whose whole subtree is searched.</param>
        /// <param name="search">Optional search text (see searchMode).</param>
        /// <param name="searchMode">How the search text matches: "contains" (default, case-insensitive substring) or "regex" (.NET regular expression, case-insensitive, time-bounded).</param>
        /// <param name="type">Optional sensor type name, e.g. "Double" or "IntegerBar".</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<SensorDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult GetSensors(Guid? product = null, string search = null,
            string searchMode = null, string type = null, int page = 1, int pageSize = ApiPagination.DefaultPageSize)
        {
            // The optional subtree filter: an unknown id is the plain area 404 (no
            // evaluator call), an invisible one the evaluator's 404 — the caller
            // cannot tell a forbidden tree from a missing one. Sight is keyed on
            // the node's ROOT product (ProductsRoles holds root ids; folder roles
            // materialize per root) — authorizing the folder id itself would 404
            // a scoped owner whose sensors under that folder ARE listable via
            // the root, breaking the tree walk the flat search offers.
            ProductModel subtree = null;

            if (product is { } productId)
            {
                if (!_cache.TryGetProduct(productId, out var node) || node is null ||
                    _authorization.AuthorizeRead(User, ApiTokenResource.Product(node.Root.Id)) != ApiTokenAuthorization.Allowed)
                {
                    return ManagementApiErrors.NotFound();
                }

                subtree = node;
            }

            if (!SensorSearchMatcher.TryBuild(search, searchMode, out var predicate, out var regexMode, out var searchErrors))
                return ManagementApiErrors.Validation(searchErrors);

            if (!TryResolveTypeFilter(type, out var typeFilter, out var typeErrors))
                return ManagementApiErrors.Validation(typeErrors);

            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            var all = FilterSensors(subtree, predicate, typeFilter, out var searchAborted);

            if (searchAborted)
            {
                // A capacity abort is NOT a validation failure (#1387 review,
                // round 4): a 400 tells an agent its request is malformed, and
                // an exhausted evaluation budget is a server-side bound. 503
                // with the mode-appropriate remedy in the message.
                var message = string.IsNullOrEmpty(search)
                    ? "The listing exceeded the evaluation budget; narrow it with 'product'."
                    : regexMode
                        ? $"The search pattern is too complex to evaluate (per-match timeout {SensorSearchMatcher.RegexTimeoutMs} ms, evaluation budget {SearchBudgetMs} ms); simplify it or narrow the listing with 'product'."
                        : "The search exceeded the evaluation budget; narrow it with 'product' or a more specific text.";

                return ManagementApiErrors.Unavailable(message);
            }

            var totalPages = ApiPagination.TotalPagesOf(all.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return Ok(new ApiPageDto<SensorDto>
            {
                Items = [.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(SensorTreeDtoMapper.ToSensorDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            });
        }


        /// <summary>
        /// Get one sensor by id: metadata plus its current value — the same shape
        /// the search list returns.
        /// </summary>
        /// <param name="id">Sensor id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(SensorDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetSensor(Guid id)
        {
            if (!TryGetVisibleSensor(id, out var sensor, out var failure))
                return failure;

            return Ok(SensorTreeDtoMapper.ToSensorDto(sensor));
        }


        /// <summary>
        /// Read the sensor's history: the NEWEST maxPoints values inside
        /// [from, to], oldest first, no aggregation. Timeout markers (OffTime
        /// points where the sensor was silent) are included. When the window holds
        /// more values than requested, the oldest excess is dropped and
        /// <c>truncated</c> is set — narrow the window for full resolution. A
        /// File sensor whose history is being read by another request answers
        /// with <c>readUnavailable</c>=true and no points — retry shortly (File
        /// response bounds are additionally capped at 100 points). For
        /// aggregated (bar) sensors the response may carry one point older than
        /// the echoed <c>from</c> — the pre-window border value.
        /// </summary>
        /// <param name="id">Sensor id.</param>
        /// <param name="from">Window start, UTC ISO 8601; default: to − 24 hours.</param>
        /// <param name="to">Window end, UTC ISO 8601; default: now.</param>
        /// <param name="maxPoints">Point limit, 1..10000 (default 1000); non-positive values fall back to the default.</param>
        [HttpGet("{id:guid}/history")]
        [ProducesResponseType(typeof(SensorHistoryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSensorHistory(Guid id, DateTime? from = null, DateTime? to = null,
            int maxPoints = DefaultMaxPoints)
        {
            if (!TryGetVisibleSensor(id, out var sensor, out var failure))
                return failure;

            // The sensor may have been deleted between the sight check and the
            // stream — the page read would dereference a null deep in the cache
            // (a pre-existing landmine there). A cheap re-check keeps this
            // endpoint on the 404 path; the residual race (deleted after the
            // re-check) is the cache's own, see feature.md Known Issues.
            if (_cache.GetSensor(id) is null)
                return ManagementApiErrors.NotFound();

            var toUtc = (to ?? DateTime.UtcNow).ToUtcInstant();
            var fromUtc = (from ?? toUtc - DefaultHistoryWindow).ToUtcInstant();

            if (fromUtc > toUtc)
                return ManagementApiErrors.Validation(new Dictionary<string, string[]>
                {
                    ["from"] = ["'from' must not be later than 'to'."],
                });

            maxPoints = maxPoints <= 0 ? DefaultMaxPoints : Math.Min(maxPoints, MaxPointsLimit);

            if (sensor.Type == SensorType.File)
                maxPoints = Math.Min(maxPoints, FileMaxPointsLimit);

            // File history reads are serialized per sensor; when another reader
            // holds the lock the cache answers an EMPTY stream. An agent must be
            // able to tell "no values in this window" from "busy, retry" — hence
            // the explicit flag instead of an indistinguishable empty 200
            // (#1387 review, round 3).
            var readUnavailable = sensor.Type == SensorType.File && _cache.IsFileHistoryReadInProgress(id);

            // The database streams the window NEWEST-FIRST (Database.GetValueToFrom
            // iterates descending keys, `to` → `from` — #1389), so the newest-N
            // read is a bounded prefix of the stream: ask for maxPoints + 1 (the
            // page generator's own count bound), drop the extra — the OLDEST of
            // the batch — and reverse for the oldest-first wire order. Projection
            // happens INSIDE the loop: the buffer holds metadata DTOs, never the
            // BaseValue instances — a File sensor's values carry the full
            // (decompressed) byte payloads (#1387 review). includeTtl: OffTime
            // markers are part of the honest picture of a sensor's timeline.
            var points = new List<SensorValueDto>(maxPoints + 1);

            if (!readUnavailable)
            {
                await foreach (var page in _cache.GetSensorValuesPage(id, fromUtc, toUtc, maxPoints + 1,
                                   RequestOptions.IncludeTtl))
                {
                    // A disconnected caller must not keep the (heavy) stream
                    // running — cancellations rethrow untouched by the /api
                    // exception middleware (#1387 review, round 3).
                    HttpContext.RequestAborted.ThrowIfCancellationRequested();

                    foreach (var value in page)
                        points.Add(SensorTreeDtoMapper.ToValueDto(sensor, value));
                }
            }

            // The lock can also be taken WHILE this read ran (the pre-read check
            // races by nature): an empty File-sensor answer with the lock held
            // now is likelier "busy" than "no data".
            if (!readUnavailable && sensor.Type == SensorType.File && points.Count == 0)
                readUnavailable = _cache.IsFileHistoryReadInProgress(id);

            // Newest-first arrival: the surplus point beyond maxPoints is the
            // oldest of the batch — its presence is exactly "the window held
            // more than maxPoints".
            var truncated = points.Count > maxPoints;
            if (truncated)
                points.RemoveAt(points.Count - 1);

            points.Reverse();

            return Ok(new SensorHistoryDto
            {
                Points = points,
                From = fromUtc,
                To = toUtc,
                MaxPoints = maxPoints,
                Truncated = truncated,
                ReadUnavailable = readUnavailable,
            });
        }


        private bool TryResolveTypeFilter(string type, out SensorType? typeFilter,
            out IDictionary<string, string[]> errors)
        {
            typeFilter = null;
            errors = null;

            if (string.IsNullOrEmpty(type))
                return true;

            // Names only (#1387 review, round 3): Enum.TryParse would happily
            // resolve "5" to IntegerBar, quietly diverging from the documented
            // name-list contract.
            if (type.All(char.IsLetter) &&
                Enum.TryParse(type, ignoreCase: true, out SensorType parsed) && Enum.IsDefined(parsed))
            {
                typeFilter = parsed;
                return true;
            }

            errors = new Dictionary<string, string[]>
            {
                ["type"] = [$"Unknown sensor type '{type}'. Valid values: {string.Join(", ", Enum.GetNames<SensorType>())}."],
            };

            return false;
        }


        // The single filter pass: candidates (a subtree's recursive sensors, or the
        // whole cache), the owner's sight per ROOT product (memoized — the same
        // resolution the evaluator applies to a sensor resource), the type filter
        // and the search predicate, ordered by path for stable pagination. The
        // regex match timeout bounds ONE match; the evaluation budget bounds the
        // WHOLE scan in EVERY mode — the unfiltered full-cache listing is the
        // most expensive shape of this request (a materialized copy, a FullPath
        // allocation per ancestor level per sensor, and a full sort), repeated
        // per page by a paginating agent with nothing else in the server to
        // throttle it (#1387 review, round 3). The abort message blames the
        // mode-appropriate field (see GetSensors).
        // The path sort runs through OrderBy — FullPath is a recursive, allocating
        // property, and a comparison-delegate Sort would re-walk the parent chain
        // on every comparison (#1387 review).
        private List<BaseSensorModel> FilterSensors(ProductModel subtree, Func<BaseSensorModel, bool> predicate,
            SensorType? typeFilter, out bool searchAborted)
        {
            searchAborted = false;

            var isProductVisible = _authorization.MemoizedProductVisibility(User);
            var budgetTicks = StopwatchTimestamps.PerMs * SearchBudgetMs;

            // The clock covers EVERY phase of the shape, not just the loop: the
            // candidate snapshot (GetSensors materializes a full copy), the
            // filter pass, and the sort after it (#1387 review, round 4 —
            // checking only inside the loop bounded the cheapest phase of the
            // most expensive request).
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            IEnumerable<BaseSensorModel> candidates = subtree is not null ? subtree.GetAllSensors() : _cache.GetSensors();

            bool OutOfBudget() => System.Diagnostics.Stopwatch.GetTimestamp() - startedAt > budgetTicks;

            var matched = new List<BaseSensorModel>();

            try
            {
                foreach (var sensor in candidates)
                {
                    if (OutOfBudget())
                    {
                        searchAborted = true;
                        return [];
                    }

                    // A disconnected caller must not keep the scan (and the sort
                    // after it) occupying a thread-pool thread: the /api
                    // exception middleware rethrows cancellations untouched, so
                    // this surfaces as an aborted request, never a 500 (#1387 r3).
                    HttpContext.RequestAborted.ThrowIfCancellationRequested();

                    if (sensor.Parent?.Root is not { } root || !isProductVisible(root.Id))
                        continue;

                    if (typeFilter is { } type && sensor.Type != type)
                        continue;

                    if (!predicate(sensor))
                        continue;

                    matched.Add(sensor);
                }

                var sorted = matched
                    .OrderBy(sensor => sensor.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(sensor => sensor.Id)
                    .ToList();

                if (OutOfBudget())
                {
                    searchAborted = true;
                    return [];
                }

                return sorted;
            }
            catch (RegexMatchTimeoutException)
            {
                searchAborted = true;
                return [];
            }
        }


        private static class StopwatchTimestamps
        {
            public static readonly long PerMs = System.Diagnostics.Stopwatch.Frequency / 1000;
        }


        // Unknown sensor id: the plain area 404 BEFORE the evaluator call; an
        // invisible one — the evaluator's 404 through the sensor's root product.
        // Reads are never forbidden in the owner-mirror model; the Forbidden arm
        // is unreachable and kept only so the evaluator's full decision surface
        // maps to a response.
        private bool TryGetVisibleSensor(Guid id, out BaseSensorModel sensor, out IActionResult failure)
        {
            sensor = _cache.GetSensor(id);
            failure = null;

            if (sensor is null)
            {
                failure = ManagementApiErrors.NotFound();
                return false;
            }

            switch (_authorization.AuthorizeRead(User, ApiTokenResource.Sensor(id)))
            {
                case ApiTokenAuthorization.Allowed:
                    return true;

                case ApiTokenAuthorization.Forbidden:
                    failure = ManagementApiErrors.Forbidden("The token's owner cannot see this sensor.");
                    return false;

                default:
                    failure = ManagementApiErrors.NotFound();
                    return false;
            }
        }


    }
}
