using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.ManagementApi.Alerts;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The mute write engine of the sensor surface (#1569): suppress or restore a
    /// sensor's alert notifications — the token-side twin of the web UI's
    /// ignore-notifications modal. Transport-agnostic — the REST controller and
    /// the MCP write tools render its <see cref="PolicyWriteResult{T}"/> through
    /// the same envelopes every other administration service uses.
    /// </summary>
    // DESIGN: the mute semantics stay in the core — this service only decides
    // admission (the evaluator's 404/403 anti-enumeration split), surfaces the
    // core's silent no-ops as explicit errors (a Blocked sensor is not mutable;
    // the web flow's fire-and-forget would read as success here) and delegates
    // the write itself to TreeValuesCache.UpdateMutedSensorStateAsync, the SAME
    // entry the web UI drives: the SensorUpdate it builds (State +
    // EndOfMutingPeriod + the caller as InitiatorInfo) and its no-op skip for an
    // unchanged period exist in one place. The muting period's expiry stays
    // core-owned too (the system-initiated auto-unmute sweep) — nothing here
    // runs its own clock.
    //
    // No per-node gate (unlike PolicyAdministrationService): a mute write is a
    // single-field update with no read-merge-write window — last-write-wins per
    // request is the documented semantics, the same race the web modal has.
    // Stateless over singletons -> Scoped registration.
    public sealed class SensorMuteAdministrationService
    {
        private const string BlockedMessage =
            "A Blocked sensor cannot be muted or unmuted.";

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;

        public SensorMuteAdministrationService(ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, IUserManager users)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
        }


        /// <summary>
        /// Mute the sensor's notifications: indefinitely (a null
        /// <paramref name="endOfMuting"/>), or until the given instant — the
        /// core's system-initiated sweep auto-unmutes at expiry. The result
        /// echoes the stored sensor.
        /// </summary>
        public async Task<PolicyWriteResult<SensorDto>> MuteSensorAsync(Guid sensorId,
            DateTime? endOfMuting, ClaimsPrincipal user)
        {
            var failure = AuthorizeMutableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult<SensorDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            // The wire contract is UTC; a Local-Kind instant (what binding can
            // hand over for offset-bearing inputs) converts, not relabels. A
            // past instant is rejected outright: the sweep would auto-unmute it
            // on its next tick, and a write whose observable effect is "not
            // muted" must not answer success.
            var normalized = endOfMuting?.ToUtcInstant();

            if (normalized is { } until && until <= DateTime.UtcNow)
                return PolicyWriteResult<SensorDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["endOfMuting"] = ["endOfMuting must be in the future."] });

            // Indefinite is the core's DateTime.MaxValue ("Forever" — never
            // swept), the same translation the web modal applies.
            await _cache.UpdateMutedSensorStateAsync(sensorId, InitiatorOf(user), normalized ?? DateTime.MaxValue);

            return Echo(sensorId);
        }

        /// <summary>Unmute the sensor: notifications resume; the stored period clears.</summary>
        public async Task<PolicyWriteResult<SensorDto>> UnmuteSensorAsync(Guid sensorId, ClaimsPrincipal user)
        {
            var failure = AuthorizeMutableSensor(sensorId, user);

            if (failure is not null)
                return PolicyWriteResult<SensorDto>.Fail(failure.Outcome, failure.Errors, failure.Message);

            await _cache.UpdateMutedSensorStateAsync(sensorId, InitiatorOf(user));

            return Echo(sensorId);
        }


        // The live sensor after the write — the queue completes the in-memory
        // update before its task resolves, so this observes the stored state. A
        // sensor removed mid-write (a concurrent delete) answers the area's 404.
        private PolicyWriteResult<SensorDto> Echo(Guid sensorId)
        {
            var sensor = _cache.GetSensor(sensorId);

            return sensor is null
                ? PolicyWriteResult<SensorDto>.Fail(PolicyWriteOutcome.NotFound)
                : PolicyWriteResult<SensorDto>.Ok(SensorTreeDtoMapper.ToSensorDto(sensor));
        }

        // Authorization FIRST (the evaluator owns the 404/403 split), then the
        // live lookup — an unknown id never reaches validation. The core's own
        // Blocked no-op is surfaced HERE as an explicit conflict instead of the
        // silent success the web flow settles for. Null = the write may proceed.
        private PolicyWriteFailure AuthorizeMutableSensor(Guid sensorId, ClaimsPrincipal user)
        {
            var decision = _authorization.AuthorizeWrite(user, new ApiTokenResource(ApiTokenResourceKind.Sensor, sensorId));

            if (decision != ApiTokenAuthorization.Allowed)
            {
                return decision == ApiTokenAuthorization.Forbidden
                    ? new PolicyWriteFailure { Outcome = PolicyWriteOutcome.Forbidden, Message = PolicyWriteResult.WriteDeniedMessage }
                    : new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound };
            }

            var sensor = _cache.GetSensor(sensorId);

            if (sensor is null)
                return new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound };

            if (sensor.State is SensorState.Blocked)
                return new PolicyWriteFailure { Outcome = PolicyWriteOutcome.Conflict, Message = BlockedMessage };

            return null;
        }

        // The write initiator mirrors the web UI's CurrentInitiator: the token's
        // OWNER as a user (the token acts as its owner, #1384) — the audit trail
        // names the token owner, like every other management write.
        private InitiatorInfo InitiatorOf(ClaimsPrincipal user)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;

            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;

            return InitiatorInfo.AsUser(owner?.Name ?? "api-token");
        }
    }
}
