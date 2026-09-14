using System;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The five sensor-tree tools of the read-only MCP surface (#1391) — a thin
    /// rendering of <see cref="SensorTreeReadService"/> over Model Context
    /// Protocol. The same bearer token, the same owner sight, the same DTOs as
    /// the REST surface; expected failures surface as tool errors (isError) for
    /// the calling agent to self-correct, never as protocol-level crashes.
    /// </summary>
    // The Streamable HTTP transport executes a stateless tools/call INLINE
    // within its POST, so the ambient HTTP context flows into the tool and
    // carries the principal the endpoint's RequireAuthorization policy already
    // admitted — the same HsmApiToken credential and ManagementPolicy as /api/v1.
    [McpServerToolType]
    public sealed class SensorTreeMcpTools
    {
        private readonly SensorTreeReadService _reader;
        private readonly IHttpContextAccessor _http;

        public SensorTreeMcpTools(SensorTreeReadService reader, IHttpContextAccessor http)
        {
            _reader = reader;
            _http = http;
        }


        [McpServerTool(Name = "list_products", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Lists the root products visible to the token's owner — the discovery entry point of the sensor tree. Nested folders are not listed here; use get_node or find_sensors.")]
        public McpProductsResult ListProducts(
            [Description("Maximum products to return (1..200, default 20); totalFound carries the full count.")] int limit = HsmMcp.DefaultLimit)
        {
            var page = _reader.ListProducts(User, page: 1, pageSize: HsmMcp.NormalizeLimit(limit));

            return new McpProductsResult
            {
                Products = page.Items,
                TotalFound = page.TotalCount,
            };
        }


        [McpServerTool(Name = "get_node", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one tree node (a root product or a nested folder) by id: metadata plus its DIRECT children. The folders list is capped at 200 with totalFolders carrying the uncapped count; the sensors list is capped at 200 with totalSensors — use find_sensors with productId for the full list. Unknown and invisible ids answer the same error.")]
        public NodeDto GetNode(
            [Description("Node id (a root product or a nested folder).")] Guid nodeId)
        {
            var result = _reader.GetNode(nodeId, User, foldersPage: 1,
                foldersPageSize: SensorTreeDtoMapper.MaxChildrenPerNode);

            return Unwrap(result);
        }


        [McpServerTool(Name = "find_sensors", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Searches sensors visible to the token's owner — the workhorse for 'analyze network problems in product X': pass search='network' (or a regex) and productId to scope to one product's subtree. Returns compact summaries ordered by path; get_sensor on an id embeds the current value. The search text matches name, description and path (OR).")]
        public McpSensorsResult FindSensors(
            [Description("Optional search text (see searchMode).")] string search = null,
            [Description("How the search text matches: 'contains' (default, case-insensitive substring) or 'regex' (.NET regular expression, case-insensitive, time-bounded).")] string searchMode = null,
            [Description("Optional node id (root product or folder) whose whole subtree is searched.")] Guid? productId = null,
            [Description("Optional sensor type name, e.g. 'Double' or 'IntegerBar'.")] string type = null,
            [Description("Maximum sensors to return (1..200, default 20); totalFound carries the full count.")] int limit = HsmMcp.DefaultLimit,
            CancellationToken cancellationToken = default)
        {
            var result = _reader.FindSensors(productId, search, searchMode, type, page: 1,
                pageSize: HsmMcp.NormalizeLimit(limit), User, cancellationToken);

            var page = Unwrap(result);

            return new McpSensorsResult
            {
                Sensors = [.. page.Items.Select(s => new McpSensorSummary
                {
                    Id = s.Id,
                    Path = s.Path,
                    Type = s.Type,
                    Status = s.Status,
                    Unit = s.Unit,
                })],
                TotalFound = page.TotalCount,
            };
        }


        [McpServerTool(Name = "get_sensor", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Gets one sensor by id: metadata plus its current value (lastValue) — the only tool that embeds the value. Unknown and invisible ids answer the same error.")]
        public SensorDto GetSensor(
            [Description("Sensor id (from find_sensors or a node listing).")] Guid sensorId) =>
            Unwrap(_reader.GetSensor(sensorId, User));


        [McpServerTool(Name = "get_sensor_history", ReadOnly = true, Idempotent = true, OpenWorld = false)]
        [Description("Reads the sensor's history: the NEWEST maxPoints values inside [from, to], oldest first, no aggregation. When the window holds more values than requested, truncated is set — narrow the window for full resolution. A File sensor whose history another request is reading answers readUnavailable=true with no points: retry shortly. Use after find_sensors/get_sensor to analyze a value timeline.")]
        public async Task<SensorHistoryDto> GetSensorHistoryAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("Window start, UTC ISO 8601; default: to − 24 hours.")] DateTime? from = null,
            [Description("Window end, UTC ISO 8601; default: now.")] DateTime? to = null,
            [Description("Point limit, 1..10000 (default 1000; 100 for File sensors).")] int maxPoints = SensorTreeReadService.DefaultMaxPoints,
            CancellationToken cancellationToken = default) =>
            Unwrap(await _reader.GetSensorHistoryAsync(sensorId, from, to, maxPoints, User, cancellationToken));


        private ClaimsPrincipal User =>
            // The endpoint is behind RequireAuthorization(ManagementPolicy), so a
            // reaching call always has an HTTP context with an authenticated
            // principal; the throw is a defensive backstop, not a reachable path.
            _http.HttpContext?.User
            ?? throw new McpException("No authenticated HTTP context is available for this tool call.");


        // The shared read service's failure becomes a tool error (isError=true
        // with the message) — the MCP rendering of what REST answers with the
        // uniform JSON error contract. Validation messages are flattened into
        // one text (the field-keyed JSON details shape has no MCP equivalent an
        // agent consumes better).
        private static T Unwrap<T>(SensorTreeReadResult<T> result) =>
            result.Failure is { } failure
                ? throw new McpException(failure.Outcome switch
                {
                    SensorTreeReadOutcome.ValidationFailed => failure.Errors is { Count: > 0 } errors
                        ? string.Join(" ", errors.Select(pair => $"{pair.Key}: {string.Join("; ", pair.Value)}"))
                        : "The request is invalid.",
                    SensorTreeReadOutcome.Forbidden or SensorTreeReadOutcome.Unavailable => failure.Message,
                    _ => "The requested resource was not found.",
                })
                : result.Value;
    }
}
