using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.ApiObjectsConverters;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;

namespace HSMServer.Core.Cache
{
    // Values the server derives itself for sensors of a user's product (#1847), see IServerSensorValues. Internal: reached
    // through the ServerSensorValues adapter only.
    public sealed partial class TreeValuesCache
    {
        internal async Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<ServerSensorValue> values, CancellationToken token = default)
        {
            // Only a root product holds sensors by path (as for a value sent with a key).
            if (!TryGetRootProduct(productId, out _, out var error))
                return new Dictionary<string, string> { [productId.ToString()] = error };

            var request = new ServerSensorValuesRequest(productId, values);
            var result = await ProcessRequestAsync(productId, request, token);

            if (!result.IsOk)
                request.Response[productId.ToString()] = result.Error;

            return request.Response;
        }

        // On the product's queue: for each value one decision on the exact path, add, create and add, or refuse. The
        // creation is under the tree's structure lock with the path checked again there, so a sensor or alias that a move
        // puts on the path in between is found and left alone. Nothing of an existing sensor is ever updated here.
        private void AddServerValues(ServerSensorValuesRequest request)
        {
            foreach (var item in request.Values)
            {
                var add = new AddSensorValueRequest(Guid.Empty, request.ProductId, item.Value.Path, item.Value.Convert());

                if (!add.TryCheckRequest(out var message))
                {
                    request.Response[add.Path ?? string.Empty] = message;
                    continue;
                }

                var type = add.BaseValue.Type;

                if (!TryGetSensorFromCache(request.ProductId, add.Path, out var sensor))
                {
                    lock (_treeStructureLock)
                    {
                        if (!TryGetSensorFromCache(request.ProductId, add.Path, out sensor))
                        {
                            // An alias keeps routing its collector's values to a moved sensor: never followed (the sensor
                            // may be in another product) and never displaced by a sensor of the server.
                            if (TryPeekAlias(request.ProductId, add.Path, out var moved))
                            {
                                request.Response[add.Path] = $"The path is an alias of the moved sensor '{moved.RootProductName}{moved.Path}'; it is left as it is.";
                                continue;
                            }

                            if (!TryAddSensorUnderLock(add, type, request.ProductId, DefaultAlertsOptions.None, checkStore: false, out sensor, out var error, out _, item))
                            {
                                request.Response[add.Path] = string.IsNullOrEmpty(error) ? "The sensor could not be created." : error;
                                continue;
                            }
                        }
                    }
                }

                if (sensor is TableSensorModel)
                {
                    request.Response[add.Path] = "The path holds a table sensor; it is left as it is.";
                    continue;
                }

                if (sensor.Type != type)
                {
                    request.Response[add.Path] = $"The path holds a {sensor.Type} sensor, the value is {type}; it is left as it is.";
                    continue;
                }

                if (!AddValueToSensor(sensor, add.Path, add.BaseValue))
                    request.Response[add.Path] = sensor.State == SensorState.Blocked ? "The sensor is blocked." : "The sensor rejected the value.";
            }
        }
    }
}


namespace HSMServer.Core.Model.Requests
{
    /// <summary>Values the server derives itself, added on the product's queue (#1847); see <see cref="Cache.IServerSensorValues"/>.</summary>
    internal sealed record ServerSensorValuesRequest(Guid ProductId, IReadOnlyList<Cache.ServerSensorValue> Values) : IUpdateRequest
    {
        public Dictionary<string, string> Response { get; } = [];

        public override string ToString() => $"Server values, ProductId = {ProductId}, Count = {Values.Count}";
    }
}
