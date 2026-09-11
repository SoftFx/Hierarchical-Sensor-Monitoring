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
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        public const int DefaultMaxPoints = 1_000;
        public const int MaxPointsLimit = 10_000;

        // Whole-request ceiling on search evaluation (#1386 review): the regex
        // match timeout bounds one IsMatch; this bounds the full scan.
        public const int SearchBudgetMs = 2_000;

        // Ceiling on how many values one history read may stream (#1386 review,
        // pass 2): newest-N selection needs the window streamed oldest-first, but
        // a huge window with a tiny maxPoints must not deserialize the sensor's
        // entire stored history. Implemented as the read's own count bound; the
        // cache's generator releases the file-history lock on every exit path
        // (try/finally — the count-reached exit used to latch it, pass 3). A
        // window denser than the cap returns the newest maxPoints of the SCANNED
        // PREFIX with truncated=true — narrow the window.
        public const int MaxScannedValues = 100_000;

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
        public IActionResult GetSensors(Guid? product = null, string search = null,
            string searchMode = null, string type = null, int page = 1, int pageSize = DefaultPageSize)
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

            if (!SensorSearchMatcher.TryBuild(search, searchMode, out var predicate, out var searchErrors))
                return ManagementApiErrors.Validation(searchErrors);

            if (!TryResolveTypeFilter(type, out var typeFilter, out var typeErrors))
                return ManagementApiErrors.Validation(typeErrors);

            page = Math.Max(page, 1);
            pageSize = Math.Min(pageSize <= 0 ? DefaultPageSize : pageSize, MaxPageSize);

            var all = FilterSensors(subtree, predicate, typeFilter, out var searchAborted);

            if (searchAborted)
                return ManagementApiErrors.Validation(new Dictionary<string, string[]>
                {
                    ["search"] = [$"The search pattern is too complex (per-match timeout {SensorSearchMatcher.RegexTimeoutMs} ms, evaluation budget {SearchBudgetMs} ms)."],
                });

            var totalPages = all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)pageSize);

            // Clamp the page into [1, totalPages]: unchecked (page - 1) * pageSize
            // would overflow int for huge page numbers, and a NEGATIVE Skip count
            // silently returns the FIRST page labeled as page N.
            page = Math.Min(page, Math.Max(totalPages, 1));

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
        /// <c>truncated</c> is set — narrow the window for full resolution. One
        /// request streams at most 100001 values: a window denser than that
        /// returns the newest points of the scanned prefix (still truncated).
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

            // The window streams oldest-first; keep the newest maxPoints values in
            // a sliding buffer. The read's count is the scan cap (see
            // MaxScannedValues) — not the response size, which maxPoints bounds.
            // includeTtl: OffTime markers are part of the honest picture of a
            // sensor's timeline (#1386).
            var window = new Queue<BaseValue>(maxPoints);
            long totalSeen = 0;

            await foreach (var page in _cache.GetSensorValuesPage(id, fromUtc, toUtc, MaxScannedValues + 1,
                               RequestOptions.IncludeTtl))
            {
                foreach (var value in page)
                {
                    window.Enqueue(value);
                    totalSeen++;

                    if (window.Count > maxPoints)
                        window.Dequeue();
                }
            }

            return Ok(new SensorHistoryDto
            {
                Points = [.. window.Select(value => SensorTreeDtoMapper.ToValueDto(sensor, value))],
                From = fromUtc,
                To = toUtc,
                MaxPoints = maxPoints,
                Truncated = totalSeen > maxPoints,
            });
        }


        private bool TryResolveTypeFilter(string type, out SensorType? typeFilter,
            out IDictionary<string, string[]> errors)
        {
            typeFilter = null;
            errors = null;

            if (string.IsNullOrEmpty(type))
                return true;

            if (Enum.TryParse(type, ignoreCase: true, out SensorType parsed) && Enum.IsDefined(parsed))
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
        // regex match timeout bounds ONE match; this budget bounds the WHOLE scan
        // — a pattern running just under the per-match timeout over thousands of
        // sensors must not stretch one request into minutes.
        private List<BaseSensorModel> FilterSensors(ProductModel subtree, Func<BaseSensorModel, bool> predicate,
            SensorType? typeFilter, out bool searchAborted)
        {
            searchAborted = false;

            IEnumerable<BaseSensorModel> candidates = subtree is not null ? subtree.GetAllSensors() : _cache.GetSensors();

            var isProductVisible = _authorization.MemoizedProductVisibility(User);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var matched = new List<BaseSensorModel>();

            try
            {
                foreach (var sensor in candidates)
                {
                    if (stopwatch.ElapsedMilliseconds > SearchBudgetMs)
                    {
                        searchAborted = true;
                        return [];
                    }

                    if (sensor.Parent?.Root is not { } root || !isProductVisible(root.Id))
                        continue;

                    if (typeFilter is { } type && sensor.Type != type)
                        continue;

                    if (!predicate(sensor))
                        continue;

                    matched.Add(sensor);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                searchAborted = true;
                return [];
            }

            matched.Sort((left, right) =>
            {
                var byPath = string.Compare(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase);
                return byPath != 0 ? byPath : left.Id.CompareTo(right.Id);
            });

            return matched;
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
