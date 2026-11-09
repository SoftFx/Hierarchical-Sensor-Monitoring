using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The deletion write engine of the sensor surface (#1637): remove a sensor
    /// — the token-side twin of the web UI's remove-node. Transport-agnostic —
    /// the REST controller and the MCP write tool render its
    /// <see cref="PolicyWriteResult"/> through the same envelopes every
    /// other administration service uses.
    /// </summary>
    // DESIGN: the removal semantics stay in the core — this service only
    // decides admission (the evaluator's 404/403 anti-enumeration split) and
    // delegates the write to TreeValuesCache.RemoveSensorAsync, the SAME entry
    // the web UI drives: cache removal, policy/mute detach, the TreeCrud
    // journal record with the initiator, RemoveSensorWithMetadata (the sensor
    // entity AND its whole history), the undo change-set purge and the change
    // event all live there. Deletion is IRREVERSIBLE by design (#1570) — the
    // dry run is the only preview, and nothing here journals an undo entry.
    //
    // No per-node gate (unlike PolicyAdministrationService) and no state
    // filter (unlike the mute engine): a deletion has no read-merge-write
    // window to serialize against, and the web UI removes a Blocked sensor
    // just as freely — decommissioning a rogue sensor is a legitimate reason
    // to delete one. Stateless over singletons -> Scoped registration.
    public sealed class SensorDeletionAdministrationService
    {
        // RemoveSensorAsync swallows its failures (the queue's handler logs
        // and drops) and returns void — the honest-reporting rule needs an
        // observable outcome, so the post-write read verifies the removal.
        private const string NotRemovedMessage = "The sensor could not be removed.";

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;

        public SensorDeletionAdministrationService(ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, IUserManager users)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
        }


        /// <summary>
        /// Delete the sensor: the cache entry, its policies and mute state, its
        /// whole stored history and its undo change-set references — the exact
        /// semantics of the web UI's remove-node, initiated by the token's
        /// owner. A dry run answers a preview of everything that would be
        /// removed and touches nothing.
        /// </summary>
        public async Task<PolicyWriteResult> DeleteSensorAsync(Guid sensorId,
            ClaimsPrincipal user, ChangeSetWriteOptions write = null)
        {
            var failure = AuthorizeExistingSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult.Fail(failure.Outcome, failure.Errors, failure.Message);

            var sensor = _cache.GetSensor(sensorId);

            // A sensor removed between admission and here (a concurrent
            // delete) is already gone — the mapper's defensive-null rule: the
            // preview and the write both answer the area's 404, never an NRE.
            if (sensor is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (write is { DryRun: true })
                return PolicyWriteResult.OkPreview(PreviewOf(sensor));

            await _cache.RemoveSensorAsync(sensorId, ChangeSetRecorder.InitiatorOf(user, _users));

            // The queue completes the in-memory removal before its task
            // resolves, so this read observes the outcome — a sensor still
            // present after the write (a swallowed core failure) answers an
            // explicit conflict instead of a ghost success, the same
            // honest-reporting rule the policy deletes apply inverted.
            return _cache.GetSensor(sensorId) is null
                ? PolicyWriteResult.Ok()
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: NotRemovedMessage);
        }

        // The everything-the-write-would-destroy report. The history numbers
        // come from the core's own statistics accessor (the same scan the web
        // UI's node info runs) — the preview must not grow new core read
        // paths. Kind/operation are the journaled surface's literals; a
        // sensor deletion has no ChangeSetEntryKind because it journals as a
        // TreeCrud record and PURGES change-set references instead of writing
        // one (#1570).
        private SensorDeletePreviewDto PreviewOf(BaseSensorModel sensor)
        {
            var history = _cache.GetSensorHistoryInfo(sensor.Id);

            return new SensorDeletePreviewDto
            {
                DryRun = true,
                Kind = "sensor",
                Operation = "delete",
                EntityId = sensor.Id.ToString(),
                Path = sensor.FullPath,
                State = sensor.State.ToString(),
                EndOfMuting = SensorTreeDtoMapper.MutedUntilOf(sensor),
                LastValueTime = sensor.LastValue is { } last ? last.Time.ToUtcInstant() : null,
                HistoryValues = history?.DataCount ?? 0,
                HistorySizeBytes = history?.TotalSizeBytes ?? 0,
                Product = sensor.Parent?.Root is { } product ? SensorTreeDtoMapper.ToNodeRef(product) : null,
                Affected =
                [
                    new PreviewAffectedEntityDto { Id = sensor.Id.ToString(), Path = sensor.FullPath, Change = "remove sensor" },
                    .. sensor.Policies.Select(p => new PreviewAffectedEntityDto
                    {
                        Id = p.Id.ToString(), Path = sensor.FullPath, Change = "remove data policy",
                    }),
                    .. sensor.Policies.TTLPolicies.Select(p => new PreviewAffectedEntityDto
                    {
                        Id = p.Id.ToString(), Path = sensor.FullPath, Change = "remove ttl policy",
                    }),
                ],
            };
        }

        // Authorization FIRST (the evaluator owns the 404/403 split), then the
        // live lookup — an unknown id never reaches the write. Null = the
        // write may proceed.
        private PolicyWriteFailure AuthorizeExistingSensor(Guid sensorId, ClaimsPrincipal user)
        {
            var decision = _authorization.AuthorizeWrite(user, new ApiTokenResource(ApiTokenResourceKind.Sensor, sensorId));

            if (decision != ApiTokenAuthorization.Allowed)
            {
                return decision == ApiTokenAuthorization.Forbidden
                    ? new PolicyWriteFailure { Outcome = PolicyWriteOutcome.Forbidden, Message = PolicyWriteResult.WriteDeniedMessage }
                    : new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound };
            }

            return _cache.GetSensor(sensorId) is null
                ? new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound }
                : null;
        }
    }
}
