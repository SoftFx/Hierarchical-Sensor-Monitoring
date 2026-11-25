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
using HSMTableBlocks;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    //
    // `_tableLimits` are the limits a table registration is checked against (the configured
    // TableSensors section): its column count and the byte length of its path. Required, so that
    // a construction site cannot fall back to the defaults without saying so.
    public sealed class SensorIngestionService(ITreeValuesCache _cache, ISensorApiTelemetry _telemetry, TableBlockLimits _tableLimits)
    {
        public const string InvalidRequest = "Public API request info not found";
        public const string UnsupportedCommand = "This type of command is not supported";
        public const string UnconvertibleRegistration = "The registration cannot be converted";
        public const string UnknownValue = "it carries a value this server does not know (a sensor type, a unit or an alert setting of a newer version)";
        public const string MissingPath = "it has no path";
        public const string NullAlert = "one of its alerts is null";
        public const string NullAlertCondition = "one of its alert conditions is null";
        public const string TableRefused = "The table sensor registration is refused";
        public const string ColumnsWithoutTableType = "only a registration of a table sensor (SensorType 11) carries table columns";

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly TaskResult _invalidRequestResult = TaskResult.FromError(InvalidRequest);

        // One for the server: a collector that moves between the two transports, or uses
        // both registration routes, repeats the same fault.
        private readonly RepeatedFaultLog _faultLog = new();


        // Whether the fault of a registration request is to be logged now (#1749). A fault
        // is answered 500 and a collector sends the package again on every cycle, so the
        // same fault of the same key gets one Error line per RepeatedFaultLog.DefaultWindow
        // and is counted in between; `unlogged` is that count for the line being written.
        // Both transports ask before they write their line.
        public bool IsFaultLogDue(Guid key, Exception fault, out long unlogged) => _faultLog.IsDue(key, fault, out unlogged);


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

        // The sensor moves a /commands batch asks for (#1724), made first and as a whole: a
        // layout that reuses names (p0 becomes p1 while p1 becomes p2) cannot be applied one
        // registration at a time. See ITreeValuesCache.ApplyPreviousPathsAsync. Both
        // transports call it once before they apply the commands, and then pass
        // previousPathsApplied: true to ApplyCommandAsync.
        //
        // Registrations are grouped by the key that sent them. Every registration's path
        // counts as one the batch registers; one that will be refused (it cannot be
        // converted) moves nothing, as on the single route. Never throws and never fails a
        // registration: a fault here is logged and the commands are applied as usual.
        public async Task ApplyPreviousPathsAsync(IReadOnlyList<(PublicApiRequestInfo Info, CommandRequestBase Command)> batch)
        {
            try
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
            catch (Exception ex)
            {
                _logger.Error(ex, "Previous paths of a registration batch not applied");
            }
        }

        // The registration's type and, when it carries previous paths, its settings. A
        // registration that AddOrUpdateSensorAsync will refuse must not fail the batch here,
        // and must move nothing: so every refusal it can make from the request alone, the
        // table ones included, is checked here too.
        private bool CanConvert(AddOrUpdateSensorRequest request, string keyName, out HSMCommon.Model.SensorType? type)
        {
            type = null;

            if (FindUnconvertiblePart(request) is not null)
                return false;

            if (FindTableRefusal(request, out _) is not null)
                return false;

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

        // One command of a /commands batch. Returns its error, or null when it was applied.
        // previousPathsApplied: the batch's sensor moves were already made by ApplyPreviousPathsAsync.
        public async Task<string> ApplyCommandAsync(PublicApiRequestInfo info, CommandRequestBase command, bool previousPathsApplied = false)
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
            // First what is checked here and not left to the type of an exception: a part
            // the conversion would fail on as a null reference, and more TTLs than TTL
            // alerts. Neither type tells the request's fault from a bug of the server.
            if (FindUnconvertiblePart(request) is { } unconvertible)
                return RefuseUnconvertible(request, unconvertible, cause: null);

            // Converted first: a registration that cannot even be converted moves nothing.
            SensorUpdate update;
            HSMCommon.Model.SensorType type;

            try
            {
                update = request.Convert(Guid.Empty, keyName);
                type = request.SensorType?.Convert() ?? HSMCommon.Model.SensorType.Boolean;
            }
            // Only what the converters throw for a request they cannot convert and nothing
            // else throws: NotImplementedException, their answer to a value of an
            // enumeration this server does not know (its own text says nothing to the
            // reader of a collector log). Not ArgumentException, which they throw on
            // purpose for one case only, more TTLs than TTL alerts, checked above: LINQ and
            // the runtime throw it and its subclasses for a null source or a bad index, so
            // catching it would answer a bug of a converter as a final refusal. Anything
            // else is not known to be the request's fault and leaves as an exception.
            catch (NotImplementedException ex)
            {
                return RefuseUnconvertible(request, UnknownValue, ex);
            }

            // The table pre-checks (#1845): what is wrong with a table registration is wrong on
            // every repeat, so it is a refusal, made before anything moves.
            if (FindTableRefusal(request, out var schema) is { } tableRefusal)
                return RefuseTable(request, tableRefusal);

            var coreRequest = new SensorAddOrUpdateRequest(productId, request.Path)
            {
                Update = update,
                Type = type,
                TypeIsExplicit = request.SensorType is not null,
                Schema = schema,
            };

            // A path with no part in it ("" or "/") names no sensor. The cache has no check
            // of its own for a registration and fails on it with an exception, which is a
            // fault there (#1756); it is the request's, so it is refused here.
            if (coreRequest.PathParts.Length == 0)
                return RefuseUnconvertible(request, MissingPath, cause: null);

            // Sensor move from the collector (#1724): the sensor registered under an old path
            // moves here first, so the registration below updates it instead of a new one. It
            // never blocks the registration (refusals are logged inside). A move it makes stands
            // even if the queue then refuses the registration's settings.
            if (!previousPathsApplied && request.PreviousPaths is { Count: > 0 })
                await _cache.ApplyPreviousPathsAsync(keyId, productId, request.Path, request.PreviousPaths,
                    coreRequest.TypeIsExplicit ? coreRequest.Type : null,
                    HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector(keyName, false));

            var result = await _cache.AddOrUpdateSensorAsync(coreRequest);

            // A fault of the cache or of the store while the registration was applied
            // (#1756) leaves as an exception, like every fault of this service: both
            // transports answer it 500 and go on with the rest of the package, and the
            // collector sends the registration again. The cache has logged the cause.
            if (result.IsFault)
                throw new RegistrationFaultException(coreRequest.Path, result.Error);

            return result;
        }

        // Every refusal of a table registration the request alone decides (#1845), in the order
        // they are answered; null when there is none. One place for the registration and for the
        // move pre-pass of a batch, which must not move anything for a registration refused here.
        // `schema` is the table schema when the registration carries valid columns.
        //  - Columns on a registration of another type.
        //  - A schema that breaks the rules of TableSchema or the configured column limit.
        //  - A path not in its normalized form: a block names its table by the registered path
        //    byte for byte (#1844) and the server stores a path normalized, so a table under
        //    another spelling would have every block refused as an unknown sensor.
        //  - A path longer in UTF-8 than the configured table identifier limit: every block
        //    naming it would be refused by the block format.
        // A path with no part in it is left to the check of a missing path.
        private string FindTableRefusal(AddOrUpdateSensorRequest request, out TableSchema schema)
        {
            schema = null;

            var isTable = request.SensorType is HSMSensorDataObjects.SensorType.TableSensor;

            if (request.TableColumns is not null)
            {
                if (!isTable)
                    return ColumnsWithoutTableType;

                if (!TryBuildSchema(request.TableColumns, out schema, out var schemaError))
                    return $"its table schema is invalid: {schemaError}";
            }

            if (!isTable)
                return null;

            var normalized = new SensorAddOrUpdateRequest(Guid.Empty, request.Path);

            if (normalized.PathParts.Length == 0)
                return null;

            if (normalized.Path != request.Path)
                return $"a table sensor is registered under its normalized path \"{normalized.Path}\", " +
                       $"and blocks name it so; this registration names \"{request.Path}\"";

            var limit = (_tableLimits ?? TableBlockLimits.Default).MaxTableIdLength;
            var bytes = Encoding.UTF8.GetByteCount(normalized.Path);

            if (bytes > limit)
                return $"blocks name a table sensor by its path in UTF-8, at most {limit} bytes, this one has {bytes}";

            return null;
        }

        // What makes a registration unconvertible and is the request's fault for certain:
        // the parts the conversion dereferences without a check, and more TTLs than TTL
        // alerts, the one case the converters answer with an ArgumentException on purpose
        // (ApiConverters.Convert(AddOrUpdateSensorRequest); the same condition and text).
        private static string FindUnconvertiblePart(AddOrUpdateSensorRequest request)
        {
            if (request.Path is null)
                return MissingPath;

            if (request.TtlAlerts is { Count: > 0 } && request.TTLs is { Count: > 0 } && request.TTLs.Count > request.TtlAlerts.Count)
                return $"TTLs count ({request.TTLs.Count}) exceeds TtlAlerts count ({request.TtlAlerts.Count}). Each TTL must have a corresponding TtlAlert.";

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

        // The schema of a table registration, checked by the rules of the table schema and the
        // configured column limit. The column types are mapped one by one: a cast would read
        // 257 as 1, the time.
        private bool TryBuildSchema(List<TableColumnRequest> columns, out TableSchema schema, out string error)
        {
            schema = null;
            error = null;

            // The count first, with the text of the schema's own rule: a huge array is refused
            // before a column of it is mapped.
            var limits = _tableLimits ?? TableBlockLimits.Default;
            if (columns.Count > limits.MaxColumnsPerTable)
            {
                error = $"a table schema has at most {limits.MaxColumnsPerTable} columns, this one has {columns.Count}";
                return false;
            }

            var built = new List<TableSchemaColumn>(columns.Count);

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];

                if (column is null)
                {
                    error = $"column {i} is null";
                    return false;
                }

                HSMTableBlocks.TableColumnType columnType;

                switch (column.Type)
                {
                    case HSMSensorDataObjects.SensorRequests.TableColumnType.Time:
                        columnType = HSMTableBlocks.TableColumnType.Time;
                        break;
                    case HSMSensorDataObjects.SensorRequests.TableColumnType.Int64:
                        columnType = HSMTableBlocks.TableColumnType.Int64;
                        break;
                    case HSMSensorDataObjects.SensorRequests.TableColumnType.Double:
                        columnType = HSMTableBlocks.TableColumnType.Double;
                        break;
                    default:
                        error = $"column {i} has the unknown type {(int)column.Type}";
                        return false;
                }

                built.Add(new TableSchemaColumn(column.Name, columnType));
            }

            // Only the schema's own rules throw here, on what the request carries; the message
            // names the rule (e.g. "column name 'bid' is used twice").
            try
            {
                schema = new TableSchema(built, _tableLimits);
                return true;
            }
            catch (ArgumentException ex)
            {
                // Without the runtime's " (Parameter 'columns')": the reader is a collector's log.
                error = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);
                return false;
            }
        }

        private static TaskResult RefuseTable(AddOrUpdateSensorRequest request, string reason)
        {
            _logger.Warn($"A table sensor registration of \"{CollectorSession.Printable(request.Path, 200)}\" is refused: {CollectorSession.Printable(reason, 300)}");

            return TaskResult.FromError($"{TableRefused}: {reason}");
        }

        private static TaskResult RefuseUnconvertible(AddOrUpdateSensorRequest request, string reason, Exception cause)
        {
            // A warning without a stack trace, as for a rejected key: a client mistake.
            // The path is the client's text.
            _logger.Warn($"A registration of \"{CollectorSession.Printable(request.Path, 200)}\" is refused, it cannot be converted{(cause is null ? string.Empty : $" ({cause.GetType().Name})")}: {reason}");

            return TaskResult.FromError($"{UnconvertibleRegistration}: {reason}");
        }
    }


    // The cache or the store failed while a registration was applied (#1756). The cause
    // is in the server's log, where the cache wrote it; this carries its text.
    //
    // Both parts are made one line of a bounded length: the message goes into the Error
    // line of the route or the session handler and into the key RepeatedFaultLog remembers
    // the fault by, and the cause of a queue fault is the request as text, with whatever the
    // client put into its description, line breaks included.
    public sealed class RegistrationFaultException(string path, string cause)
        : Exception($"The registration of \"{CollectorSession.Printable(path, MaxPathLength)}\" was not applied: {CollectorSession.Printable(cause, MaxCauseLength)}")
    {
        public const int MaxPathLength = 200;
        public const int MaxCauseLength = 300;
    }
}
