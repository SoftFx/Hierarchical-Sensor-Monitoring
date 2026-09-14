using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The single implementation of the sensor-tree read surface (#1386, #1391):
    /// visible root products, one node with its direct children, flat recursive
    /// sensor search, one sensor with its current value, and the newest-N history.
    /// Transport-agnostic — the REST controllers and the MCP tools both call it;
    /// the caller supplies the token principal and a cancellation token, and gets
    /// DTOs or a <see cref="SensorTreeReadFailure"/> back, never an IActionResult.
    /// </summary>
    // Extracted verbatim from the three sensor-tree controllers (#1391) so MCP
    // tools and REST endpoints share ONE implementation of search, visibility and
    // mapping. The controllers' suites are the regression net: the wire behavior
    // they pin must not change.
    //
    // All reads resolve on the live cache — ConcurrentDictionary scans, no
    // locking; the visibility decision is memoized per DISTINCT root product
    // within one request (sensors cluster into few products and the evaluator
    // re-resolves caller and token on every call — the schedules-list pattern).
    public sealed class SensorTreeReadService
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

        public static readonly TimeSpan DefaultHistoryWindow = TimeSpan.FromHours(24);

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;

        public SensorTreeReadService(ITreeValuesCache cache, IApiTokenAuthorizationService authorization)
        {
            _cache = cache;
            _authorization = authorization;
        }


        /// <summary>
        /// Root products visible to the token's owner, ordered by name then id.
        /// An owner who sees nothing gets an empty list, not a failure: the list
        /// carries no per-item secret, and emptiness discloses nothing.
        /// </summary>
        public ApiPageDto<ProductDto> ListProducts(ClaimsPrincipal user, int page, int pageSize)
        {
            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            // GetProducts serves the ROOT-PRODUCT NAME INDEX (_productsByName —
            // roots only, maintained on add/rename/remove); the IsRoot filter is
            // a defensive no-op, kept in case the accessor ever moves to a
            // broader index. Visibility follows the owner's sight at the product
            // boundary — the same predicate every sensor listing resolves
            // through.
            var all = _cache.GetProducts()
                .Where(product => product.IsRoot && _authorization.IsVisible(user, ApiTokenResource.Product(product.Id)))
                .OrderBy(product => product.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(product => product.Id)
                .ToList();

            var totalPages = ApiPagination.TotalPagesOf(all.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return new ApiPageDto<ProductDto>
            {
                Items = [.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(SensorTreeDtoMapper.ToProductDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            };
        }


        /// <summary>
        /// One tree node (root product or folder) by id: metadata plus its DIRECT
        /// children, folders paginated, sensors capped. Unknown and invisible ids
        /// answer the SAME NotFound.
        /// </summary>
        public SensorTreeReadResult<NodeDto> GetNode(Guid id, ClaimsPrincipal user,
            int foldersPage, int foldersPageSize)
        {
            // Unknown id: the plain area 404, issued BEFORE the evaluator call —
            // the area rule that keeps unknown and invisible indistinguishable
            // without paying a security event for every random id.
            if (!_cache.TryGetProduct(id, out var node) || node is null)
                return SensorTreeReadResult<NodeDto>.Fail(SensorTreeReadOutcome.NotFound);

            // Sight is keyed on the node's ROOT product (ProductsRoles holds root
            // ids; folder roles materialize per root) — authorizing the folder id
            // itself would 404 a scoped owner whose sensors under that folder ARE
            // listable via the root. Reads are never forbidden in the owner-mirror
            // model (the read-only flag constrains writes); the Forbidden arm is
            // unreachable and kept only so the evaluator's full decision surface
            // maps to a response.
            var decision = _authorization.AuthorizeRead(user, ApiTokenResource.Product(node.Root.Id));

            // The folders list is the ONLY addressable surface for direct
            // subfolders (the sensor search pages sensors, not folders), so it
            // paginates like every area list — a node with more than 200 direct
            // subfolders would otherwise strand folders 201..N unreachably
            // (#1387 review, round 3). Shared clamps (the pagination ceiling
            // equals the mapper's children ceiling).
            (foldersPage, foldersPageSize) = ApiPagination.Normalize(foldersPage, foldersPageSize);

            var totalPages = ApiPagination.TotalPagesOf(node.SubProducts.Count, foldersPageSize);
            foldersPage = ApiPagination.ClampPage(foldersPage, totalPages);

            return decision switch
            {
                ApiTokenAuthorization.Allowed =>
                    SensorTreeReadResult<NodeDto>.Ok(SensorTreeDtoMapper.ToNodeDto(node, foldersPage, foldersPageSize)),
                ApiTokenAuthorization.Forbidden => SensorTreeReadResult<NodeDto>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: "The token's owner cannot see this node."),
                _ => SensorTreeReadResult<NodeDto>.Fail(SensorTreeReadOutcome.NotFound),
            };
        }


        /// <summary>
        /// Search sensors, ordered by full path then id. Without parameters: every
        /// sensor visible to the token's owner. With <paramref name="product"/>:
        /// the subtree of that node (any product or folder id — unknown and
        /// invisible ids answer the uniform NotFound). The search text matches
        /// name, description and path (OR).
        /// </summary>
        public SensorTreeReadResult<ApiPageDto<SensorDto>> FindSensors(Guid? product, string search, string searchMode,
            string type, int page, int pageSize, ClaimsPrincipal user, CancellationToken cancellation)
        {
            // The optional subtree filter: an unknown id is the plain area 404 (no
            // evaluator call), an invisible one the evaluator's 404 — the caller
            // cannot tell a forbidden tree from a missing one. Sight is keyed on
            // the node's ROOT product (see GetNode).
            ProductModel subtree = null;

            if (product is { } productId)
            {
                if (!_cache.TryGetProduct(productId, out var node) || node is null ||
                    _authorization.AuthorizeRead(user, ApiTokenResource.Product(node.Root.Id)) != ApiTokenAuthorization.Allowed)
                {
                    return SensorTreeReadResult<ApiPageDto<SensorDto>>.Fail(SensorTreeReadOutcome.NotFound);
                }

                subtree = node;
            }

            if (!SensorSearchMatcher.TryBuild(search, searchMode, out var predicate, out var regexMode, out var searchErrors))
                return SensorTreeReadResult<ApiPageDto<SensorDto>>.Fail(
                    SensorTreeReadOutcome.ValidationFailed, errors: searchErrors);

            if (!TryResolveTypeFilter(type, out var typeFilter, out var typeErrors))
                return SensorTreeReadResult<ApiPageDto<SensorDto>>.Fail(
                    SensorTreeReadOutcome.ValidationFailed, errors: typeErrors);

            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            var all = FilterSensors(subtree, predicate, typeFilter, user, cancellation, out var searchAborted);

            if (searchAborted)
            {
                // A capacity abort is NOT a validation failure (#1387 review,
                // round 4): a 400 tells an agent its request is malformed, and
                // an exhausted evaluation budget is a server-side bound. 503
                // with the mode-appropriate remedy in the message. The remedy
                // is parameter-NEUTRAL: the subtree filter is 'product' on REST
                // and 'productId' on MCP, and a remedy naming one surface's
                // parameter sends the other's client after an unknown argument
                // (#1392 review).
                var message = string.IsNullOrEmpty(search)
                    ? "The listing exceeded the evaluation budget; narrow it to a single product."
                    : regexMode
                        ? $"The search pattern is too complex to evaluate (per-match timeout {SensorSearchMatcher.RegexTimeoutMs} ms, evaluation budget {SearchBudgetMs} ms); simplify it or narrow the listing to a single product."
                        : "The search exceeded the evaluation budget; narrow it to a single product or a more specific text.";

                return SensorTreeReadResult<ApiPageDto<SensorDto>>.Fail(SensorTreeReadOutcome.Unavailable, message: message);
            }

            var totalPages = ApiPagination.TotalPagesOf(all.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return SensorTreeReadResult<ApiPageDto<SensorDto>>.Ok(new ApiPageDto<SensorDto>
            {
                Items = [.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(SensorTreeDtoMapper.ToSensorDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            });
        }


        /// <summary>
        /// One sensor by id: metadata plus its current value — the same shape the
        /// search list returns.
        /// </summary>
        public SensorTreeReadResult<SensorDto> GetSensor(Guid id, ClaimsPrincipal user)
        {
            if (!TryGetVisibleSensor(id, user, out var sensor, out var failure))
                return SensorTreeReadResult<SensorDto>.FromFailure(failure);

            return SensorTreeReadResult<SensorDto>.Ok(SensorTreeDtoMapper.ToSensorDto(sensor));
        }


        /// <summary>
        /// The sensor's history: the NEWEST maxPoints values inside
        /// [from, to], oldest first, no aggregation. Timeout markers (OffTime
        /// points where the sensor was silent) are included. When the window holds
        /// more values than requested, the oldest excess is dropped and
        /// <c>truncated</c> is set — narrow the window for full resolution. A
        /// File sensor whose history is being read by another request answers
        /// with <c>readUnavailable</c>=true and no points — retry shortly (File
        /// response bounds are additionally capped at 100 points). For
        /// aggregated (bar) sensors the response may carry one point older than
        /// the echoed <c>from</c> — the pre-window border value (only in an
        /// under-full response: a full one drops the border first, and dropping
        /// it alone never sets <c>truncated</c> — it is outside the window).
        /// </summary>
        public async Task<SensorTreeReadResult<SensorHistoryDto>> GetSensorHistoryAsync(Guid id, DateTime? from,
            DateTime? to, int maxPoints, ClaimsPrincipal user, CancellationToken cancellation)
        {
            if (!TryGetVisibleSensor(id, user, out var sensor, out var failure))
                return SensorTreeReadResult<SensorHistoryDto>.FromFailure(failure);

            // The sensor may have been deleted between the sight check and the
            // stream — the page read would dereference a null deep in the cache
            // (a pre-existing landmine there). A cheap re-check keeps this read
            // on the 404 path; the residual race (deleted after the re-check)
            // is the cache's own, see feature.md Known Issues.
            if (_cache.GetSensor(id) is null)
                return SensorTreeReadResult<SensorHistoryDto>.Fail(SensorTreeReadOutcome.NotFound);

            var toUtc = (to ?? DateTime.UtcNow).ToUtcInstant();
            var fromUtc = (from ?? toUtc - DefaultHistoryWindow).ToUtcInstant();

            if (fromUtc > toUtc)
                return SensorTreeReadResult<SensorHistoryDto>.Fail(SensorTreeReadOutcome.ValidationFailed,
                    errors: new Dictionary<string, string[]>
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
                    // exception middleware and surface as an aborted MCP call
                    // (#1387 review, round 3).
                    cancellation.ThrowIfCancellationRequested();

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
            // more than maxPoints", with one exception. An aggregated sensor's
            // batch may END with the pre-window BORDER value (the cache rewinds
            // its read back to it — the same AggregateValues flag gates that
            // rewind — so it arrives last on the descending stream): that point
            // is older than the echoed `from`, outside the window, and dropping
            // it alone is not truncation — every in-window value is still
            // returned. RemoveRange keeps the maxPoints response cap enforced
            // by construction, whatever the stream's exact count.
            var truncated = points.Count > maxPoints;
            if (truncated)
            {
                truncated = !(sensor.AggregateValues && points[^1].Time < fromUtc);
                points.RemoveRange(maxPoints, points.Count - maxPoints);
            }

            points.Reverse();

            return SensorTreeReadResult<SensorHistoryDto>.Ok(new SensorHistoryDto
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
        // mode-appropriate field (see FindSensors).
        // The path sort runs through OrderBy — FullPath is a recursive, allocating
        // property, and a comparison-delegate Sort would re-walk the parent chain
        // on every comparison (#1387 review).
        private List<BaseSensorModel> FilterSensors(ProductModel subtree, Func<BaseSensorModel, bool> predicate,
            SensorType? typeFilter, ClaimsPrincipal user, CancellationToken cancellation,
            out bool searchAborted)
        {
            searchAborted = false;

            var isProductVisible = _authorization.MemoizedProductVisibility(user);
            var budgetTicks = StopwatchTimestamps.PerMs * SearchBudgetMs;

            // The clock covers EVERY phase of the shape, not just the loop: the
            // candidate snapshot (GetSensors materializes a full copy), the
            // filter pass, and the sort after it (#1387 review, round 4 —
            // checking only inside the loop bounded the cheapest phase of the
            // most expensive request).
            var startedAt = Stopwatch.GetTimestamp();

            IEnumerable<BaseSensorModel> candidates = subtree is not null ? subtree.GetAllSensors() : _cache.GetSensors();

            bool OutOfBudget() => Stopwatch.GetTimestamp() - startedAt > budgetTicks;

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
                    // this surfaces as an aborted request, never a 500 — and as
                    // an aborted MCP call on the MCP transport (#1387 r3).
                    cancellation.ThrowIfCancellationRequested();

                    if (sensor.Parent?.Root is not { } root || !isProductVisible(root.Id))
                        continue;

                    if (typeFilter is { } sensorType && sensor.Type != sensorType)
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
            public static readonly long PerMs = Stopwatch.Frequency / 1000;
        }


        // Unknown sensor id: the plain area 404 BEFORE the evaluator call; an
        // invisible one — the evaluator's 404 through the sensor's root product.
        // Reads are never forbidden in the owner-mirror model; the Forbidden arm
        // is unreachable and kept only so the evaluator's full decision surface
        // maps to a response.
        private bool TryGetVisibleSensor(Guid id, ClaimsPrincipal user,
            out BaseSensorModel sensor, out SensorTreeReadFailure failure)
        {
            sensor = _cache.GetSensor(id);
            failure = null;

            if (sensor is null)
            {
                failure = new SensorTreeReadFailure { Outcome = SensorTreeReadOutcome.NotFound };
                return false;
            }

            switch (_authorization.AuthorizeRead(user, ApiTokenResource.Sensor(id)))
            {
                case ApiTokenAuthorization.Allowed:
                    return true;

                case ApiTokenAuthorization.Forbidden:
                    failure = new SensorTreeReadFailure
                    {
                        Outcome = SensorTreeReadOutcome.Forbidden,
                        Message = "The token's owner cannot see this sensor.",
                    };
                    return false;

                default:
                    failure = new SensorTreeReadFailure { Outcome = SensorTreeReadOutcome.NotFound };
                    return false;
            }
        }
    }
}
