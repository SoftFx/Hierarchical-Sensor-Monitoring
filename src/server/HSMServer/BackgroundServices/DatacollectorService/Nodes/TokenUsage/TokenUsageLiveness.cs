using System;
using HSMServer.Authentication;

namespace HSMServer.BackgroundServices;

// The eviction sweep's liveness predicate (#1403 review):
// IsTokenLive is the sanctioned RECORD-liveness rule (revocation, rotation,
// generations) but says nothing about the OWNER — and owner deletion
// invalidates the credential (the auth handler rejects it) without touching
// the token row: nothing revokes an owner's tokens on deletion, and the
// retention cleaner never removes rows whose owner simply disappeared. The
// sweep must therefore compose the same way authentication composes: record
// live AND owner still exists. The owner rides the SAME lookup —
// TryGetLiveOwner is one index walk with no ApiTokenInfo projection per
// token per tick (#1403 review). Unit-tested directly with mocks.
internal static class TokenUsageLiveness
{
    // The ABSTENTION clause comes first: while IsGenerationStateHealthy is
    // false the index answers "not live" for EVERY token (Initialize clears
    // it for the whole duration of a reload), and a tombstone is IRREVERSIBLE
    // for the process lifetime — an unproven global state is not evidence
    // that any particular token died, so the sweep keeps everything that
    // tick instead. Unreachable today (Initialize runs once, from
    // InitStorages, before the collector's start delay) — the clause is the
    // cheap guard against any future re-init path turning one sweep tick
    // during an unhealthy window into permanent monitoring loss (#1403
    // review).
    public static Func<Guid, bool> Compose(IApiTokenManager tokens, IUserManager users) =>
        entityId => !tokens.IsGenerationStateHealthy
                    || (tokens.TryGetLiveOwner(entityId, out var ownerId)
                        && users[ownerId] is not null);
}
