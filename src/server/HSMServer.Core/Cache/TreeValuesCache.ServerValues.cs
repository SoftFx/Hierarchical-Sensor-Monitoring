using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.ApiObjectsConverters;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;

namespace HSMServer.Core.Cache
{
    // Values the server derives itself for sensors of a user's product (#1847), see IServerSensorValues.
    public sealed partial class TreeValuesCache : IServerSensorValues
    {
        public async Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<SensorValueBase> values, CancellationToken token = default)
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

        // On the product's queue. The same steps as a value sent with a key after its key was checked, except that a
        // missing sensor is not created and only a sensor of the value's type that is not a table sensor takes it.
        private void AddServerValues(ServerSensorValuesRequest request)
        {
            foreach (var value in request.Values)
            {
                var add = new AddSensorValueRequest(Guid.Empty, request.ProductId, value.Path, value.Convert());

                if (!add.TryCheckRequest(out var message))
                {
                    request.Response[value.Path ?? string.Empty] = message;
                    continue;
                }

                if (!TryGetSensorFromCache(request.ProductId, add.Path, out var sensor))
                {
                    switch (ResolveAlias(request.ProductId, add.Path, add.BaseValue.Type, out sensor))
                    {
                        case AliasResolution.Forwarded when sensor is not TableSensorModel:
                            Dispatch(sensor.Root, new AliasValueRequest(sensor.Id, sensor.Root.Id, add.BaseValue, Hops: 0));
                            continue;

                        case AliasResolution.None:
                            request.Response[add.Path] = IServerSensorValues.NoSensor;
                            continue;
                    }
                }

                if (sensor is TableSensorModel)
                {
                    request.Response[add.Path] = $"Sensor '{add.Path}' is a table sensor: {TableSensorModel.NoValuesError}";
                    continue;
                }

                if (sensor.Type != add.BaseValue.Type)
                {
                    request.Response[add.Path] = $"Sensor '{add.Path}' is a {sensor.Type} sensor, the value is {add.BaseValue.Type}.";
                    continue;
                }

                AddValueToSensor(sensor, add.Path, add.BaseValue);
            }
        }
    }
}


namespace HSMServer.Core.Model.Requests
{
    /// <summary>Values the server derives itself, added on the product's queue (#1847); see <see cref="Cache.IServerSensorValues"/>.</summary>
    internal sealed record ServerSensorValuesRequest(Guid ProductId, IReadOnlyList<SensorValueBase> Values) : IUpdateRequest
    {
        public Dictionary<string, string> Response { get; } = [];

        public override string ToString() => $"Server values, ProductId = {ProductId}, Count = {Values.Count}";
    }
}
