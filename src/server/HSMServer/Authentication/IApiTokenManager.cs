using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Authentication
{
    // Authoritative in-memory index over durable API token records. Dedicated on purpose:
    // token creation must never go through the generic ConcurrentStorage.TryAdd, which
    // publishes to memory before persistence.
    public interface IApiTokenManager : IAsyncStorage
    {
        // False when revocation generation state is missing, corrupt or regressed. Every API
        // token authentication must fail closed while this is false.
        bool IsGenerationStateHealthy { get; }

        long GlobalRevocationGeneration { get; }

        long GetOwnerRevocationGeneration(Guid ownerUserId);

        // Authentication-path lookup by the public TokenId; null when unknown.
        ApiTokenEntity GetToken(string tokenId);

        // Lifecycle-route lookup by the stable entity id; null when unknown.
        ApiTokenEntity GetTokenByEntityId(Guid entityId);

        List<ApiTokenEntity> GetTokensByOwner(Guid ownerUserId);

        // Creates a token with the explicit grants (canonicalized; empty means a token that
        // allows nothing). Persists first; publishes to the authentication index only after
        // the write. fullToken carries the secret exactly once and is never stored or logged.
        bool TryCreateToken(Guid ownerUserId, string name, string description, List<ApiTokenGrantEntity> grants,
            DateTime? expiresAtUtc, string createdBy, out ApiTokenEntity entity, out string fullToken);

        // Restriction only removes grant pairs and/or shortens expiry; returns false on any
        // expansion attempt. Idempotently records RestrictedAtUtc/RestrictedBy.
        bool TryRestrictToken(Guid entityId, List<ApiTokenGrantEntity> remainingGrants, DateTime? shortenedExpiryUtc,
            string restrictedBy, out ApiTokenEntity entity);

        // Rotation issues a completely fresh EntityId/TokenId/secret with the same or a
        // strict subset of grants and an expiry no later than the source, and atomically
        // revokes the source token in the same durable write. Never expands grants and never
        // turns a finite expiry into an unlimited one.
        bool TryRotateToken(Guid entityId, DateTime? shortenedExpiryUtc, string rotatedBy,
            out ApiTokenEntity entity, out string fullToken);

        // Revocation is immediate and idempotent.
        bool TryRevokeToken(Guid entityId, string revokedBy, string reason, out ApiTokenEntity entity);

        // Advances the durable generation (persist first), then publishes it to
        // authentication. Throws on storage failure: no partial state is published.
        long AdvanceGlobalRevocationGeneration();

        long AdvanceOwnerRevocationGeneration(Guid ownerUserId);

        // Tokens counted by MaxTokensPerUser: unexpired, individually active (not revoked),
        // and issued at the current global and owner revocation generations. Revoked,
        // expired, orphaned and generation-invalidated records never count.
        int CountQuotaEligibleTokens(Guid ownerUserId);
    }
}
