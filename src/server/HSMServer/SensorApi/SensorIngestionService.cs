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
using System.Linq;
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

        // A /commands batch, the same for the HTTP route and the session. The sensor moves
        // its registrations ask for (#1724) are made first and as a whole — a layout that
        // reuses names (p0 becomes p1 while p1 becomes p2) cannot be applied one registration
        // at a time — then every command in the listed order. Returns the per-path errors of
        // the commands that were not applied (empty when all were). Info is null for a
        // command that is not a registration.
        public async Task<Dictionary<string, string>> ApplyCommandsAsync(IReadOnlyList<(PublicApiRequestInfo Info, CommandRequestBase Command)> batch)
        {
            await ApplyPreviousPathsAsync(batch);

            var errors = new Dictionary<string, string>();

            foreach (var (info, command) in batch)
            {
                var error = await ApplyCommandAsync(info, command, previousPathsApplied: true);

                if (error is not null)
                    errors[command.Path] = error;
            }

            return errors;
        }

        // See ITreeValuesCache.ApplyPreviousPathsAsync. Registrations are grouped by the key
        // that sent them. Every registration's path counts as one the batch registers; one
        // that cannot be converted moves nothing, as on the single route (it is refused when
        // applied). Never fails a registration.
        private async Task ApplyPreviousPathsAsync(IReadOnlyList<(PublicApiRequestInfo Info, CommandRequestBase Command)> batch)
        {
            var byKey = batch
                .Where(entry => entry.Info is not null && entry.Command is AddOrUpdateSensorRequest)
                .GroupBy(entry => (Key: entry.Info.Key.Id, Product: entry.Info.Product.Id));

            foreach (var group in byKey)
            {
                // Almost every batch: nothing to move, nothing to look at.
                if (!group.Any(entry => ((AddOrUpdateSensorRequest)entry.Command).PreviousPaths is { Count: > 0 }))
                    continue;

                var keyName = group.First().Info.Key.DisplayName;
                var registrations = new List<PreviousPathsRegistration>();

                foreach (var (_, command) in group)
                {
                    var request = (AddOrUpdateSensorRequest)command;
                    var previousPaths = request.PreviousPaths;

                    if (!CanConvert(request, keyName, out var type))
                        previousPaths = null;

                    registrations.Add(new PreviousPathsRegistration(request.Path, previousPaths, type));
                }

                await _cache.ApplyPreviousPathsAsync(group.Key.Key, group.Key.Product, registrations,
                    HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector(keyName, false));
            }
        }

        // The registration's type and, when it carries previous paths, its settings. A type
        // the server does not know must not fail the whole batch here: the registration
        // itself is refused when applied, after the commands listed before it.
        private static bool CanConvert(AddOrUpdateSensorRequest request, string keyName, out HSMCommon.Model.SensorType? type)
        {
            type = null;

            try
            {
                type = request.SensorType?.Convert();

                if (request.PreviousPaths is { Count: > 0 })
                    request.Convert(Guid.Empty, keyName);

                return true;
            }
            catch (Exception)
            {
                type = null;
                return false;
            }
        }

        // One command. Returns its error, or null when it was applied.
        // previousPathsApplied: the batch's sensor moves were already made by ApplyCommandsAsync.
        private async Task<string> ApplyCommandAsync(PublicApiRequestInfo info, CommandRequestBase command, bool previousPathsApplied)
        {
            if (command is not AddOrUpdateSensorRequest sensorUpdate)
                return UnsupportedCommand;

            var result = await AddOrUpdateSensorAsync(info, sensorUpdate, previousPathsApplied);

            return result.IsOk ? null : result.Error;
        }

        public async Task<TaskResult> AddOrUpdateSensorAsync(PublicApiRequestInfo info, AddOrUpdateSensorRequest request,
            bool previousPathsApplied = false)
        {
            if (info is null)
                return _invalidRequestResult;

            // Converted first: a registration that cannot even be converted moves nothing.
            var coreRequest = new SensorAddOrUpdateRequest(info.Product.Id, request.Path)
            {
                Update = request.Convert(Guid.Empty, info.Key.DisplayName),
                Type = request.SensorType?.Convert() ?? HSMCommon.Model.SensorType.Boolean,
                TypeIsExplicit = request.SensorType is not null,
            };

            // Sensor move from the collector (#1724): the sensor registered under an old path
            // moves here first, so the registration below updates it instead of a new one. It
            // never blocks the registration (refusals are logged inside). A move it makes stands
            // even if the queue then refuses the registration's settings.
            if (!previousPathsApplied && request.PreviousPaths is { Count: > 0 })
                await _cache.ApplyPreviousPathsAsync(info.Key.Id, info.Product.Id, request.Path, request.PreviousPaths,
                    coreRequest.TypeIsExplicit ? coreRequest.Type : null,
                    HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector(info.Key.DisplayName, false));

            return await _cache.AddOrUpdateSensorAsync(coreRequest);
        }
    }
}
