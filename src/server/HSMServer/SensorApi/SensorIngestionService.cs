using HSMCommon.TaskResult;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Requests;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.SensorApi
{
    // What the Sensor API does with sensor values and sensor registrations once the caller
    // is known (#1664). The HTTP controller and the collector session both call it, so a
    // batch is stored and counted the same way whichever transport carried it.
    //
    // It knows nothing about HTTP: the caller resolves who is asking (PublicApiRequestInfo)
    // and turns the outcome into a status code or an ack.
    public sealed class SensorIngestionService(ITreeValuesCache _cache, ISensorApiTelemetry _telemetry)
    {
        public const string InvalidRequest = "Public API request info not found";
        public const string UnsupportedCommand = "This type of command is not supported";

        private static readonly TaskResult _invalidRequestResult = TaskResult.FromError(InvalidRequest);


        // The /list batch. Returns the per-path errors of the values that were not stored
        // (empty when all were).
        public async Task<Dictionary<string, string>> AddValuesAsync(PublicApiRequestInfo info, List<SensorValueBase> values)
        {
            var response = await _cache.AddSensorValuesAsync(info.Key.Id, info.Product.Id, values);

            _telemetry.AddReceivedValues(info.TelemetryPath, values.Count);

            return response;
        }

        // One value of a typed single-value route.
        public async Task<TaskResult> AddValueAsync(PublicApiRequestInfo info, SensorValueBase value)
        {
            if (info is null)
                return _invalidRequestResult;

            var result = await _cache.AddSensorValueAsync(info.Key.Id, info.Product.Id, value);

            if (result.IsOk)
                _telemetry.AddReceivedValues(info.TelemetryPath, 1);

            return result;
        }

        // One command of a /commands batch. Returns its error, or null when it was applied.
        public async Task<string> ApplyCommandAsync(PublicApiRequestInfo info, CommandRequestBase command)
        {
            if (command is not AddOrUpdateSensorRequest sensorUpdate)
                return UnsupportedCommand;

            var result = await AddOrUpdateSensorAsync(info, sensorUpdate);

            return result.IsOk ? null : result.Error;
        }

        public async Task<TaskResult> AddOrUpdateSensorAsync(PublicApiRequestInfo info, AddOrUpdateSensorRequest request)
        {
            if (info is null)
                return _invalidRequestResult;

            // Sensor move from the collector (#1724): the sensor registered under an old path
            // moves here first, so the registration below updates it instead of a new one.
            if (request.PreviousPaths is { Count: > 0 })
            {
                var moved = await _cache.ApplyPreviousPathsAsync(info.Key.Id, info.Product.Id, request.Path, request.PreviousPaths,
                    request.SensorType?.Convert(), HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector(info.Key.DisplayName, false));

                if (!moved.IsOk)
                    return moved;
            }

            var coreRequest = new SensorAddOrUpdateRequest(info.Product.Id, request.Path)
            {
                Update = request.Convert(Guid.Empty, info.Key.DisplayName),
                Type = request.SensorType?.Convert() ?? HSMCommon.Model.SensorType.Boolean,
                TypeIsExplicit = request.SensorType is not null,
            };

            return await _cache.AddOrUpdateSensorAsync(coreRequest);
        }
    }
}
