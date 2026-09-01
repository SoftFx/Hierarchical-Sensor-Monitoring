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
        // False when the durable state was not fully provable at boot: the token-row scan
        // failed, or revocation generation state is missing, corrupt or regressed. Every
        // API token authentication must fail closed while this is false. Per-token
        // lifecycle results are also not trustworthy while this is false — after a failed
        // boot scan the in-memory index is empty, so TryRevokeToken reports false for
        // every token even though the durable rows survive and would authenticate again
        // once the storage problem clears. The management layer distinguishes the cases
        // on this flag: false + unhealthy = "do not trust per-token results, use the
        // emergency revoke"; false + healthy = "no such token".
        bool IsGenerationStateHealthy { get; }

        long GlobalRevocationGeneration { get; }

        // Current durable generation for the owner, missing-as-zero. An owner absent from
        // the index (no loadable records, e.g. after retention removed them) can hold a
        // durable value this accessor does not report; create/rotate read and cache the
        // durable value through a fallback, so newly minted tokens are always stamped
        // correctly regardless.
        long GetOwnerRevocationGeneration(Guid ownerUserId);

        // Lookup by the public TokenId; null when unknown. Results are verifier-free
        // projections (ApiTokenInfo): the stored verifier never crosses this interface.
        ApiTokenInfo GetToken(string tokenId);

        // Lifecycle-route lookup by the stable entity id; null when unknown.
        ApiTokenInfo GetTokenByEntityId(Guid entityId);

        List<ApiTokenInfo> GetTokensByOwner(Guid ownerUserId);

        // The single authentication decision for a presented bearer credential: strict
        // parse, index lookup, stored-or-dummy constant-time verifier compare (and still
        // false when no record was found — the compare result alone is never the
        // decision), then revoked/expired/both-generation-stamps and boot health. Use
        // this from the handler instead of reassembling the checks from GetToken and the
        // generation accessors: every omitted predicate there is an authentication bypass.
        bool TryAuthenticate(string presentedToken, out ApiTokenInfo entity);

        // Creates a token with the explicit grants (canonicalized; empty means a token that
        // allows nothing). Persists first; publishes to the authentication index only after
        // the write. fullToken carries the secret exactly once and is never stored or logged.
        // Returns false — never throws — while generation state is unhealthy or unreadable:
        // no token is minted against unproven generation values.
        bool TryCreateToken(Guid ownerUserId, string name, string description, List<ApiTokenGrantEntity> grants,
            DateTime? expiresAtUtc, string createdBy, out ApiTokenInfo entity, out string fullToken);

        // Restriction only removes grant pairs and/or shortens expiry; returns false on any
        // expansion attempt and for a dead record — a revoked or generation-invalidated
        // (emergency-revoked) token cannot be restricted. Null remainingGrants keeps the
        // current grants, symmetric with null shortenedExpiryUtc keeping the current
        // expiry; an explicit empty list strips every grant. A no-op request (grants
        // unchanged, expiry unchanged) succeeds without a rewrite. Changing requests
        // record RestrictedAtUtc/RestrictedBy.
        bool TryRestrictToken(Guid entityId, List<ApiTokenGrantEntity> remainingGrants, DateTime? shortenedExpiryUtc,
            string restrictedBy, out ApiTokenInfo entity);

        // Rotation issues a completely fresh EntityId/TokenId/secret with the same grants
        // (narrowing a token is what restriction is for) and an expiry no later than the
        // source, and atomically revokes the source token in the same durable write. Never
        // turns a finite expiry into an unlimited one; the resulting expiry must not
        // already be in the past (requested or inherited). Refused, like creation, while
        // generation state is unhealthy or unreadable, and for a dead source token —
        // revoked, or invalidated by an emergency revoke generation.
        bool TryRotateToken(Guid entityId, DateTime? shortenedExpiryUtc, string rotatedBy,
            out ApiTokenInfo entity, out string fullToken);

        // Revocation is immediate and idempotent — for any token visible in the index.
        // False means "not live in the index": either genuinely unknown, or the index is
        // untrustworthy (IsGenerationStateHealthy false after a failed boot scan — the
        // durable row still exists). In the latter case the operator's lever is the
        // emergency revoke (AdvanceGlobalRevocationGeneration /
        // AdvanceOwnerRevocationGeneration), which bypasses the index entirely and
        // invalidates the token durably.
        bool TryRevokeToken(Guid entityId, string revokedBy, string reason, out ApiTokenInfo entity);

        // Advances the durable generation (persist first), then publishes it to
        // authentication — every token issued at an older generation stops authenticating
        // immediately. Throws on storage failure: no partial state is published, and a
        // throw means the emergency revoke DID NOT happen — in-memory and durable values
        // stay consistent at the old generation and every previously valid token keeps
        // authenticating. Callers must not swallow the exception.
        long AdvanceGlobalRevocationGeneration();

        // Retention removal, atomic across the durable row and the live index (both steps
        // under the manager's state lock, so a concurrent revoke/rotate cannot rewrite a
        // row the removal just deleted and resurrect it after restart). A TokenId absent
        // from the live index still gets its durable row deleted — rows rejected at load
        // (future EntityVersion, foreign VersionByte, ...) are exactly the orphans
        // retention exists to clear. False when no durable row was removed.
        bool TryRemoveToken(string tokenId);

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
