using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Requests;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The sensor-move write engine (#1721): move sensors to another path, and
    /// remove a moved sensor's alias. Transport-agnostic — the REST controller
    /// and the MCP tools render its results through the same envelopes every
    /// other administration service uses.
    /// </summary>
    // DESIGN: the move semantics stay in the core (TreeValuesCache.MoveSensorAsync,
    // ADR-0018) — this service decides admission and shape only:
    // - the destination root product must be writable by the token's owner
    //   (404/403 like every write surface), checked once per request;
    // - each sensor must be writable by the owner; a sensor that is not is
    //   reported in its own result entry (the batch is not atomic, #1718
    //   decision 1), so one invisible id cannot fail the others;
    // - one sensor goes to a full `path`, several (or one) into a `node`
    //   keeping their names — exactly one of the two.
    // The core journals every move with the owner as initiator. A plain move
    // journals no undo entry yet (rollback is #1723); a merge is irreversible.
    // Stateless over singletons -> Scoped registration.
    public sealed class SensorMoveAdministrationService
    {
        public const int MaxSensorsPerRequest = 100;

        private const string NoSuchAlias = "The sensor has no alias at that path.";

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;

        public SensorMoveAdministrationService(ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, IUserManager users)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
        }


        public async Task<PolicyWriteResult<SensorMoveResponseDto>> MoveSensorsAsync(SensorMoveRequestDto request,
            ClaimsPrincipal user, ChangeSetWriteOptions write = null, CancellationToken token = default)
        {
            var errors = Validate(request, out var onConflict);

            if (errors.Count > 0)
                return PolicyWriteResult<SensorMoveResponseDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            var destination = _authorization.AuthorizeWrite(user, new ApiTokenResource(ApiTokenResourceKind.Product, request.Product));

            if (destination != ApiTokenAuthorization.Allowed)
                return destination == ApiTokenAuthorization.Forbidden
                    ? PolicyWriteResult<SensorMoveResponseDto>.Fail(PolicyWriteOutcome.Forbidden, message: PolicyWriteResult.WriteDeniedMessage)
                    : PolicyWriteResult<SensorMoveResponseDto>.Fail(PolicyWriteOutcome.NotFound);

            var product = _cache.GetProduct(request.Product);

            if (product is null)
                return PolicyWriteResult<SensorMoveResponseDto>.Fail(PolicyWriteOutcome.NotFound);

            if (!product.IsRoot)
                return PolicyWriteResult<SensorMoveResponseDto>.Fail(PolicyWriteOutcome.Invalid, new Dictionary<string, string[]>
                {
                    ["product"] = ["The destination must be a root product; give sub-nodes as part of `path` or `node`."],
                });

            // Admission first: the same-name check below must see only sensors
            // the caller may move, or it would name hidden sensors and let one
            // of them fail the whole batch (#1721 review).
            var admitted = request.Sensors
                .Select(id => (Id: id, Admission: Admit(id, user, out var sensor), Sensor: sensor))
                .ToList();

            // Two sensors bound for one path: the second would conflict — or,
            // under merge, absorb and delete the first, which the caller asked
            // to keep. Refused before anything moves.
            if (request.Node is not null)
            {
                var clash = admitted.Where(a => a.Admission is null).Select(a => a.Sensor)
                    .GroupBy(s => s.DisplayName, StringComparer.Ordinal)
                    .FirstOrDefault(g => g.Count() > 1);

                if (clash is not null)
                    return PolicyWriteResult<SensorMoveResponseDto>.Fail(PolicyWriteOutcome.Invalid, new Dictionary<string, string[]>
                    {
                        ["sensors"] = [$"{clash.Count()} sensors are named '{clash.Key}' and would share one destination path in the node; move them separately with `path`."],
                    });
            }

            var dryRun = write is { DryRun: true };
            var initiator = dryRun ? null : ChangeSetRecorder.InitiatorOf(user, _users);

            var results = new List<SensorMoveItemDto>(request.Sensors.Count);
            var previews = new List<SensorMovePreviewItemDto>(request.Sensors.Count);

            foreach (var (sensorId, admission, sensor) in admitted)
            {
                if (admission is not null)
                {
                    results.Add(new SensorMoveItemDto { SensorId = sensorId, Outcome = admission, Error = ErrorOf(admission) });
                    previews.Add(new SensorMovePreviewItemDto { SensorId = sensorId, Outcome = admission, Error = ErrorOf(admission) });
                    continue;
                }

                var path = DestinationOf(request, sensor.DisplayName);

                if (dryRun)
                {
                    previews.Add(ToPreviewItem(sensorId, _cache.PreviewMove(sensorId, request.Product, path, onConflict)));
                    continue;
                }

                // Sequential: every move goes through its product's queue, and
                // merges must not interleave on one destination path.
                var moved = await _cache.MoveSensorAsync(sensorId, request.Product, path, onConflict, initiator, token);
                results.Add(ToItem(sensorId, moved));
            }

            if (dryRun)
                return PolicyWriteResult<SensorMoveResponseDto>.OkPreview(new SensorMovePreviewDto
                {
                    DryRun = true,
                    Kind = "sensor",
                    Operation = "move",
                    EntityId = request.Sensors.Count == 1 ? request.Sensors[0].ToString() : null,
                    Path = $"{product.DisplayName}/{request.Path ?? request.Node}".TrimEnd('/'),
                    Affected = previews.Where(p => p.Outcome is "moved" or "merged")
                        .Select(p => new PreviewAffectedEntityDto { Id = p.SensorId.ToString(), Path = p.NewPath, Change = p.Outcome })
                        .ToList(),
                    Sensors = previews,
                });

            return PolicyWriteResult<SensorMoveResponseDto>.Ok(new SensorMoveResponseDto
            {
                Results = results,
                Moved = results.Count(r => r.Outcome is "moved" or "merged"),
                Failed = results.Count(r => r.Outcome is not ("moved" or "merged" or "unchanged")),
            });
        }

        /// <summary>
        /// Remove one alias of a moved sensor: values sent to that old path stop
        /// landing in the sensor (the next one creates a new sensor there).
        /// </summary>
        public async Task<PolicyWriteResult> RemoveAliasAsync(Guid sensorId, Guid productId, string path,
            ClaimsPrincipal user, CancellationToken token = default)
        {
            if (productId == Guid.Empty || string.IsNullOrWhiteSpace(path))
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Invalid, new Dictionary<string, string[]>
                {
                    [productId == Guid.Empty ? "product" : "path"] = ["The alias's product id and path (as listed in the sensor's `aliases`) are required."],
                });

            var admission = Admit(sensorId, user, out _);

            if (admission is not null)
                return admission == Forbidden
                    ? PolicyWriteResult.Fail(PolicyWriteOutcome.Forbidden, message: PolicyWriteResult.WriteDeniedMessage)
                    : PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            // Removing the alias changes routing in ITS product (the next value
            // there creates a new sensor), which may not be the sensor's.
            var aliasProduct = _authorization.AuthorizeWrite(user, new ApiTokenResource(ApiTokenResourceKind.Product, productId));

            if (aliasProduct != ApiTokenAuthorization.Allowed)
                return aliasProduct == ApiTokenAuthorization.Forbidden
                    ? PolicyWriteResult.Fail(PolicyWriteOutcome.Forbidden, message: PolicyWriteResult.WriteDeniedMessage)
                    : PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            return await _cache.RemoveSensorAliasAsync(sensorId, productId, path, ChangeSetRecorder.InitiatorOf(user, _users), token)
                ? PolicyWriteResult.Ok()
                : PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound, message: NoSuchAlias);
        }


        private const string NotFound = "notFound";
        private const string Forbidden = "forbidden";

        // The per-sensor admission: the evaluator's 404/403 split, then the live
        // lookup. Null = the sensor may be moved.
        private string Admit(Guid sensorId, ClaimsPrincipal user, out Core.Model.BaseSensorModel sensor)
        {
            sensor = null;

            var decision = _authorization.AuthorizeWrite(user, new ApiTokenResource(ApiTokenResourceKind.Sensor, sensorId));

            if (decision == ApiTokenAuthorization.Forbidden)
                return Forbidden;

            if (decision != ApiTokenAuthorization.Allowed)
                return NotFound;

            sensor = _cache.GetSensor(sensorId);

            return sensor is null ? NotFound : null;
        }

        private static string ErrorOf(string admission) => admission == Forbidden
            ? PolicyWriteResult.WriteDeniedMessage
            : ManagementApiErrors.NotFoundMessage;

        private static string DestinationOf(SensorMoveRequestDto request, string sensorName) =>
            request.Path ?? (string.IsNullOrWhiteSpace(request.Node) ? sensorName : $"{request.Node.TrimEnd('/')}/{sensorName}");

        private static Dictionary<string, string[]> Validate(SensorMoveRequestDto request, out SensorMoveConflict onConflict)
        {
            var errors = new Dictionary<string, string[]>();
            onConflict = SensorMoveConflict.Fail;

            if (request is null)
            {
                errors["body"] = ["A move request body is required."];
                return errors;
            }

            if (request.Sensors is not { Count: > 0 })
                errors["sensors"] = ["At least one sensor id is required."];
            else if (request.Sensors.Count > MaxSensorsPerRequest)
                errors["sensors"] = [$"At most {MaxSensorsPerRequest} sensors per request."];
            else if (request.Sensors.Distinct().Count() != request.Sensors.Count)
                errors["sensors"] = ["Sensor ids must be distinct."];

            if (request.Product == Guid.Empty)
                errors["product"] = ["The destination root product id is required."];

            if ((request.Path is null) == (request.Node is null))
                errors["path"] = ["Give exactly one of `path` (the full new path of one sensor) and `node` (the destination node; sensors keep their names)."];
            else if (request.Path is not null && string.IsNullOrWhiteSpace(request.Path))
                errors["path"] = ["`path` must name the sensor's new path; use `node` with \"\" to move into the product root."];
            else if (request.Path is not null && request.Sensors is { Count: > 1 })
                errors["path"] = ["`path` moves exactly one sensor; use `node` to move several."];

            switch (request.OnConflict?.ToLowerInvariant())
            {
                case null or "" or "fail":
                    break;
                case "merge":
                    onConflict = SensorMoveConflict.Merge;
                    break;
                default:
                    errors["onConflict"] = ["Value table: `fail` (default), `merge`."];
                    break;
            }

            return errors;
        }

        private static string OutcomeName(SensorMoveOutcome outcome) => outcome switch
        {
            SensorMoveOutcome.Moved => "moved",
            SensorMoveOutcome.Merged => "merged",
            SensorMoveOutcome.Unchanged => "unchanged",
            SensorMoveOutcome.Conflict => "conflict",
            SensorMoveOutcome.SensorNotFound => NotFound,
            SensorMoveOutcome.DestinationNotFound => NotFound,
            SensorMoveOutcome.InvalidPath => "invalidPath",
            _ => "failed",
        };

        private static SensorMoveItemDto ToItem(Guid sensorId, SensorMoveResult result) => new()
        {
            SensorId = sensorId,
            Outcome = OutcomeName(result.Outcome),
            OldPath = result.OldPath,
            NewPath = result.NewPath,
            OtherSensorId = result.MergedSensorId,
            // Present whenever a merge happened — also when the move after it
            // failed: the absorbed sensor is gone either way.
            MergedValues = result.MergedSensorId is not null ? result.MergedValues : null,
            MergeCollisions = result.MergedSensorId is not null ? result.MergeCollisions : null,
            Error = result.Error,
        };

        private static SensorMovePreviewItemDto ToPreviewItem(Guid sensorId, SensorMovePreview preview) => new()
        {
            SensorId = sensorId,
            Outcome = OutcomeName(preview.Outcome),
            OldPath = preview.OldPath,
            NewPath = preview.NewPath,
            OtherSensorId = preview.OccupantId,
            DisplacesAliasOf = preview.DisplacesAliasOf,
            CreatedNodes = preview.CreatedNodes,
            TemplatesDetached = preview.TemplatesDetached.Select(t => new SensorMoveTemplateDto { Id = t.Id, Name = t.Name }).ToList(),
            TemplatesApplied = preview.TemplatesApplied.Select(t => new SensorMoveTemplateDto { Id = t.Id, Name = t.Name }).ToList(),
            SettingsChanged = preview.SettingsChanged.Select(s => new SensorMoveSettingDto { Name = s.Name, Before = s.Before, After = s.After }).ToList(),
            Error = preview.Error,
        };
    }
}
