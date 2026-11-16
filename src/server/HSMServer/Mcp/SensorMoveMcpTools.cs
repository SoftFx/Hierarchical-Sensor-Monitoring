using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The sensor-move tools of the MCP surface (#1721): move sensors, remove a
    /// moved sensor's alias — thin delegations to
    /// <see cref="SensorMoveAdministrationService"/>, the same engine as
    /// <c>POST /api/v1/sensors/move</c> and <c>DELETE /api/v1/sensors/{id}/aliases</c>.
    /// </summary>
    // Authorization is the service's (destination product and every sensor,
    // the 404/403 split); read-only tokens never reach /mcp (POST-only
    // transport, management policy). The move semantics are the core's
    // (ADR-0018). A sensor's aliases are listed by get_sensor (`aliases`).
    [McpServerToolType]
    public sealed class SensorMoveMcpTools
    {
        private readonly SensorMoveAdministrationService _mover;
        private readonly IHttpContextAccessor _http;

        public SensorMoveMcpTools(SensorMoveAdministrationService mover, IHttpContextAccessor http)
        {
            _mover = mover;
            _http = http;
        }


        // Idempotent=false: a merge removes the absorbed sensor, and a repeated
        // move of an already moved sensor answers `unchanged`, not the same result.
        [McpServerTool(Name = "move_sensors", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Moves sensors to another path, in their product or into another root product. Each sensor keeps its id, its WHOLE history, settings, alerts and journal; its old path becomes an alias (listed by get_sensor) that keeps routing values from not-yet-updated collectors to it. Give `path` to move ONE sensor to a full new path (the last segment renames it), or `node` to move one or more sensors into a node keeping their names; missing nodes are created. Not atomic: every sensor reports its own outcome. With onConflict=`merge`, a sensor moving onto a path held by a sensor of the same type absorbs that sensor's history and the other sensor is removed — IRREVERSIBLE. Use dryRun first: it predicts each outcome, the nodes to create, alert templates detached/applied and inherited settings that change.")]
        public async Task<McpSensorMoveResult> MoveSensorsAsync(
            [Description("Ids of the sensors to move, 1..100. With `path`, exactly one.")] List<Guid> sensorIds,
            [Description("Id of the destination ROOT product.")] Guid product,
            [Description("Full new path of ONE sensor relative to the product, e.g. `garage/system/CPU`. Exactly one of `path` and `node`.")] string path = null,
            [Description("Destination node path relative to the product, e.g. `garage/system`; sensors keep their names. Empty string = directly under the product. Exactly one of `path` and `node`.")] string node = null,
            [Description("`fail` (default): a sensor whose destination is taken is not moved. `merge`: it absorbs the sensor there (same type only; IRREVERSIBLE).")] string onConflict = null,
            [Description("Predict the move without applying anything — the answer carries `preview` instead of `value`.")] bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            var request = new SensorMoveRequestDto
            {
                Sensors = sensorIds,
                Product = product,
                Path = path,
                Node = node,
                OnConflict = onConflict,
            };

            var result = await _mover.MoveSensorsAsync(request, User, new ChangeSetWriteOptions { DryRun = dryRun }, cancellationToken);

            if (result.IsDryRun)
                return new() { Preview = (SensorMovePreviewDto)result.Preview };

            return new() { Value = McpToolWriteErrors.Unwrap(result) };
        }


        // Idempotent=false: a repeat answers not-found (the alias is gone).
        [McpServerTool(Name = "remove_sensor_alias", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Removes one alias of a moved sensor (an entry of get_sensor's `aliases`): values sent to that old path stop landing in the sensor, and the next one creates a new sensor there. Aliases also expire on their own after the sensor's SelfDestroy interval without arrivals. Fails on an unknown/invisible sensor, an alias the sensor does not have, and for read-only tokens.")]
        public async Task<McpSensorAliasRemovedResult> RemoveSensorAliasAsync(
            [Description("Sensor id.")] Guid sensorId,
            [Description("The alias's root product id (`aliases[].productId`).")] Guid product,
            [Description("The alias's path relative to that product (`aliases[].path`).")] string path,
            CancellationToken cancellationToken = default)
        {
            McpToolWriteErrors.Unwrap(await _mover.RemoveAliasAsync(sensorId, product, path, User, cancellationToken));

            return new() { SensorId = sensorId, ProductId = product, Path = path };
        }


        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
