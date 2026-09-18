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
// live AND owner still exists. Unit-tested directly with mocks.
internal static class TokenUsageLiveness
{
    public static Func<Guid, bool> Compose(IApiTokenManager tokens, IUserManager users) =>
        entityId => tokens.IsTokenLiveByEntityId(entityId)
                    && tokens.GetTokenByEntityId(entityId)?.OwnerUserId is { } ownerId
                    && users[ownerId] is not null;
}
