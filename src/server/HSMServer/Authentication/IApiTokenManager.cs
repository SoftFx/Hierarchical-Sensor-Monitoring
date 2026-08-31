using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Authentication
{
    // Authoritative in-memory index over durable API token records. Dedicated on purpose:
    // token creation must never go through the generic ConcurrentStorage.TryAdd, which
    // publishes to memory before persistence.
    //
    // Thread safety: the implementation is a singleton used from request threads. Mutation
    // methods serialize their whole read -> persist -> publish sequence, so concurrent
    // lifecycle calls cannot lose a revocation; read methods are lock-free and safe to call
    // concurrently with mutations. All DateTime parameters are UTC by contract: Kind.Local
    // converts, Kind.Unspecified is interpreted as UTC.
    public interface IApiTokenManager : IAsyncStorage
    {
        // False when revocation generation state is missing, corrupt or regressed. Every API
        // token authentication must fail closed while this is false.
        bool IsGenerationStateHealthy { get; }

        long GlobalRevocationGeneration { get; }

        // Current durable generation for the owner, missing-as-zero. An owner absent from
        // the index (no loadable records, e.g. after retention removed them) can hold a
        // durable value this accessor does not report; create/rotate read and cache the
        // durable value through a fallback, so newly minted tokens are always stamped
        // correctly regardless.
        long GetOwnerRevocationGeneration(Guid ownerUserId);

        // Authentication-path lookup by the public TokenId; null when unknown. The
        // returned record is the live index entry — consumers must not mutate its Grants
        // or Verifier.
        ApiTokenEntity GetToken(string tokenId);

        // Lifecycle-route lookup by the stable entity id; null when unknown. Same
        // live-entry contract as GetToken.
        ApiTokenEntity GetTokenByEntityId(Guid entityId);

        List<ApiTokenEntity> GetTokensByOwner(Guid ownerUserId);

        // Creates a token with the explicit grants (canonicalized; empty means a token that
        // allows nothing). Persists first; publishes to the authentication index only after
        // the write. fullToken carries the secret exactly once and is never stored or logged.
        // Returns false — never throws — while generation state is unhealthy or unreadable:
        // no token is minted against unproven generation values.
        bool TryCreateToken(Guid ownerUserId, string name, string description, List<ApiTokenGrantEntity> grants,
            DateTime? expiresAtUtc, string createdBy, out ApiTokenEntity entity, out string fullToken);

        // Restriction only removes grant pairs and/or shortens expiry; returns false on any
        // expansion attempt and for a dead record — a revoked or generation-invalidated
        // (emergency-revoked) token cannot be restricted. Null remainingGrants keeps the
        // current grants, symmetric with null shortenedExpiryUtc keeping the current
        // expiry; an explicit empty list strips every grant. A no-op request (grants
        // unchanged, expiry unchanged) succeeds without a rewrite. Changing requests
        // record RestrictedAtUtc/RestrictedBy.
        bool TryRestrictToken(Guid entityId, List<ApiTokenGrantEntity> remainingGrants, DateTime? shortenedExpiryUtc,
            string restrictedBy, out ApiTokenEntity entity);

        // Rotation issues a completely fresh EntityId/TokenId/secret with the same grants
        // (narrowing a token is what restriction is for) and an expiry no later than the
        // source, and atomically revokes the source token in the same durable write. Never
        // turns a finite expiry into an unlimited one; the resulting expiry must not
        // already be in the past (requested or inherited). Refused, like creation, while
        // generation state is unhealthy or unreadable, and for a dead source token —
        // revoked, or invalidated by an emergency revoke generation.
        bool TryRotateToken(Guid entityId, DateTime? shortenedExpiryUtc, string rotatedBy,
            out ApiTokenEntity entity, out string fullToken);

        // Revocation is immediate and idempotent.
        bool TryRevokeToken(Guid entityId, string revokedBy, string reason, out ApiTokenEntity entity);

        // Advances the durable generation (persist first), then publishes it to
        // authentication — every token issued at an older generation stops authenticating
        // immediately. Throws on storage failure: no partial state is published, and a
        // throw means the emergency revoke DID NOT happen — in-memory and durable values
        // stay consistent at the old generation and every previously valid token keeps
        // authenticating. Callers must not swallow the exception.
        long AdvanceGlobalRevocationGeneration();

        // Drops a record from the live authentication index after its durable row was
        // deleted (retention cleanup). The caller removes the durable row FIRST — the
        // reverse order would let the record rejoin the index after restart. False when
        // no live record existed for the TokenId.
        bool Unpublish(string tokenId);

        // Advances the durable owner generation (persist first), then publishes it to
        // authentication — every token of that owner issued at an older generation stops
        // authenticating immediately. Same throw contract as
        // AdvanceGlobalRevocationGeneration: a throw means the revoke did not happen and
        // must surface.
        long AdvanceOwnerRevocationGeneration(Guid ownerUserId);

        // Tokens counted by MaxTokensPerUser: unexpired, individually active (not revoked),
        // and issued at the current global and owner revocation generations. Revoked,
        // expired, orphaned and generation-invalidated records never count.
        int CountQuotaEligibleTokens(Guid ownerUserId);
    }
}
