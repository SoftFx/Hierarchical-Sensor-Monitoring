using HSMCommon.TaskResult;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Requests;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.SensorApi.Sessions;
using NLog;
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
    // and turns the outcome into a status code or an ack. For a registration the two
    // outcomes must stay apart (#1749): a refusal is a returned error, which the caller
    // answers as a refusal (a collector does not send that registration again); an
    // exception that leaves this service is a fault of the server, which the caller
    // answers 500 (a collector sends the package again).
    public sealed class SensorIngestionService(ITreeValuesCache _cache, ISensorApiTelemetry _telemetry)
    {
        public const string InvalidRequest = "Public API request info not found";
        public const string UnsupportedCommand = "This type of command is not supported";
        public const string UnconvertibleRegistration = "The registration cannot be converted";
        public const string UnknownValue = "it carries a value this server does not know (a sensor type, a unit or an alert setting of a newer version)";
        public const string MissingPath = "it has no path";
        public const string NullAlert = "one of its alerts is null";
        public const string NullAlertCondition = "one of its alert conditions is null";

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
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

            // What the server knows about the caller is read before the conversion and
            // outside its catch (#1749): a failure here (a key or a product that went away
            // under the request) is a fault of the server's state, and must leave as an
            // exception, never as a refusal of the registration.
            var productId = info.Product.Id;
            var keyId = info.Key.Id;
            var keyName = info.Key.DisplayName;

            // A registration the server cannot convert is refused, not faulted (#1749).
            // Everything below reads nothing but the request, so the same request fails
            // the same way on every repeat; answered 500 it would make a collector send
            // the whole package again on every cycle.
            //
            // First what would otherwise fail as a null reference, which is not told apart
            // from a bug of the server by its type.
            if (FindMissingPart(request) is { } missing)
                return RefuseUnconvertible(request, missing, cause: null);

            // Converted first: a registration that cannot even be converted moves nothing.
            SensorUpdate update;
            HSMCommon.Model.SensorType type;

            try
            {
                update = request.Convert(Guid.Empty, keyName);
                type = request.SensorType?.Convert() ?? HSMCommon.Model.SensorType.Boolean;
            }
            // Only what the converters throw for a request they cannot convert: a value of
            // an enumeration this server does not know (NotImplementedException, whose own
            // text says nothing to the reader of a collector log) and more TTLs than TTL
            // alerts (ArgumentException). Anything else is not known to be the request's
            // fault and leaves as an exception.
            catch (Exception ex) when (ex is NotImplementedException or ArgumentException)
            {
                return RefuseUnconvertible(request, ex is NotImplementedException ? UnknownValue : ex.Message, ex);
            }

            var coreRequest = new SensorAddOrUpdateRequest(productId, request.Path)
            {
                Update = update,
                Type = type,
                TypeIsExplicit = request.SensorType is not null,
            };

            // Sensor move from the collector (#1724): the sensor registered under an old path
            // moves here first, so the registration below updates it instead of a new one. It
            // never blocks the registration (refusals are logged inside). A move it makes stands
            // even if the queue then refuses the registration's settings.
            if (request.PreviousPaths is { Count: > 0 })
                await _cache.ApplyPreviousPathsAsync(keyId, productId, request.Path, request.PreviousPaths,
                    coreRequest.TypeIsExplicit ? coreRequest.Type : null,
                    HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector(keyName, false));

            return await _cache.AddOrUpdateSensorAsync(coreRequest);
        }

        // The parts of a registration the conversion dereferences without a check.
        private static string FindMissingPart(AddOrUpdateSensorRequest request)
        {
            if (request.Path is null)
                return MissingPath;

            foreach (var alerts in new[] { request.Alerts, request.TtlAlerts })
            {
                if (alerts is null)
                    continue;

                foreach (var alert in alerts)
                {
                    if (alert is null)
                        return NullAlert;

                    if (alert.Conditions?.Contains(null) == true)
                        return NullAlertCondition;
                }
            }

            return null;
        }

        private static TaskResult RefuseUnconvertible(AddOrUpdateSensorRequest request, string reason, Exception cause)
        {
            // A warning without a stack trace, as for a rejected key: a client mistake.
            // The path is the client's text.
            _logger.Warn($"A registration of \"{CollectorSession.Printable(request.Path, 200)}\" is refused, it cannot be converted{(cause is null ? string.Empty : $" ({cause.GetType().Name})")}: {reason}");

            return TaskResult.FromError($"{UnconvertibleRegistration}: {reason}");
        }
    }
}
