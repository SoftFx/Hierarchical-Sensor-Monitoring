using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using Microsoft.Extensions.Logging;

namespace HSMServer.Authentication
{
    // Authoritative in-memory authentication index over durable ApiTokenEntity rows.
    //
    // Publication discipline (initiative: fine-grained API token authentication): every
    // mutation persists through IDatabaseCore FIRST and publishes to the in-memory index
    // only after a successful write; a failed write leaves neither durable nor live state.
    // Creation and rotation never go through the generic ConcurrentStorage.TryAdd — the
    // database worker serializes the TokenId existence check with the write, and a collision
    // retries with a completely new id/secret pair.
    //
    // Thread safety: the manager is a singleton reached from request threads. Every
    // lifecycle mutation and every generation advance runs its whole read -> persist ->
    // publish sequence under _stateLock, so a restrict/rotate derived from a
    // pre-revocation snapshot can never durably overwrite a revocation. Readers take no
    // lock and walk lock-free snapshot-safe structures only.
    public sealed class ApiTokenManager : IApiTokenManager
    {
        private const int MaxInsertAttempts = 3;
        private const int MaxNameLength = 256;
        private const int MaxDescriptionLength = 1024;
        private const int MaxReasonLength = 256;

        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<ApiTokenManager> _logger;

        // Serializes the whole read -> persist -> publish sequence of lifecycle mutations
        // and generation advances. One lock instead of per-entity striping: these are
        // low-frequency administrative operations, and a single gate also makes the
        // generation snapshot reads of create/rotate consistent with advances.
        private readonly object _stateLock = new();

        private readonly ConcurrentDictionary<string, ApiTokenEntity> _tokensByTokenId = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<Guid, string> _tokenIdByEntityId = new();

        // Owner side of the index. The values are concurrent sets rather than HashSet:
        // readers enumerate them on request threads while a mutation publishes concurrently.
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _tokenIdsByOwner = new();
        private readonly ConcurrentDictionary<Guid, long> _ownerGenerations = new();

        // Volatile by convention: read on every authentication, flipped only during load or
        // advance. False means every API token authentication must fail closed.
        private volatile bool _isGenerationStateHealthy;

        // Written under _stateLock, read lock-free on the authentication and quota paths:
        // Volatile.Read/Write give the visibility and the atomic 64-bit read that a plain
        // long field cannot guarantee on every runtime (volatile long is not legal C#).
        private long _globalGeneration;


        public ApiTokenManager(IDatabaseCore databaseCore, ILogger<ApiTokenManager> logger)
        {
            _databaseCore = databaseCore ?? throw new ArgumentNullException(nameof(databaseCore));
            _logger = logger;
        }


        public bool IsGenerationStateHealthy => _isGenerationStateHealthy;

        public long GlobalRevocationGeneration => Volatile.Read(ref _globalGeneration);


        public Task Initialize()
        {
            // Fail closed until generation state is proven authoritative.
            _isGenerationStateHealthy = false;

            LoadTokens();
            LoadGenerations();

            _logger.LogInformation(
                "API token index initialized: {TokenCount} tokens, global generation {Generation}, healthy = {Healthy}",
                _tokensByTokenId.Count, _globalGeneration, _isGenerationStateHealthy);

            return Task.CompletedTask;
        }

        public ApiTokenEntity GetToken(string tokenId) =>
            tokenId is not null && _tokensByTokenId.TryGetValue(tokenId, out var entity) ? entity : null;

        public ApiTokenEntity GetTokenByEntityId(Guid entityId) =>
            _tokenIdByEntityId.TryGetValue(entityId, out var tokenId) ? GetToken(tokenId) : null;

        public List<ApiTokenEntity> GetTokensByOwner(Guid ownerUserId) =>
            _tokenIdsByOwner.TryGetValue(ownerUserId, out var tokenIds)
                ? tokenIds.Keys.Select(GetToken).Where(token => token is not null).ToList()
                : [];

        public long GetOwnerRevocationGeneration(Guid ownerUserId) => _ownerGenerations.GetValueOrDefault(ownerUserId);


        public bool TryCreateToken(Guid ownerUserId, string name, string description, List<ApiTokenGrantEntity> grants,
            DateTime? expiresAtUtc, string createdBy, out ApiTokenEntity entity, out string fullToken)
        {
            entity = null;
            fullToken = null;

            var sanitizedName = Sanitize(name, MaxNameLength);
            var sanitizedDescription = Sanitize(description, MaxDescriptionLength);

            if (ownerUserId == Guid.Empty || string.IsNullOrEmpty(sanitizedName))
                return false;

            if (!ApiTokenGrants.TryCanonicalize(grants, out var canonicalGrants))
                return false;

            var expiryTicks = NormalizeUtcTicks(expiresAtUtc);

            if (expiryTicks.HasValue && expiryTicks.Value <= DateTime.UtcNow.Ticks)
                return false;

            lock (_stateLock)
            {
                for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
                {
                    var material = ApiTokenMaterial.Generate();

                    var candidate = new ApiTokenEntity
                    {
                        EntityVersion = 1,
                        EntityId = Guid.NewGuid(),
                        TokenId = material.TokenId,
                        VersionByte = ApiTokenMaterial.CurrentVersionByte,
                        Verifier = ApiTokenVerifier.ComputeVerifier(ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes),
                        OwnerUserId = ownerUserId,
                        GlobalRevocationGenerationAtIssue = GlobalRevocationGeneration,
                        OwnerRevocationGenerationAtIssue = GetOrLoadOwnerGeneration(ownerUserId),
                        Name = sanitizedName,
                        Description = sanitizedDescription,
                        Grants = canonicalGrants,
                        CreatedAtUtc = DateTime.UtcNow.Ticks,
                        CreatedBy = createdBy,
                        ExpiresAtUtc = expiryTicks,
                    };

                    ApiTokenMaterial.Clear(material.SecretBytes);

                    try
                    {
                        // Collision returns false: discard the whole candidate, new id/secret pair.
                        if (!_databaseCore.TryInsertApiToken(candidate))
                            continue;

                        // Published only after the durable write succeeded.
                        Publish(candidate);

                        entity = candidate;
                        fullToken = ApiTokenMaterial.FormatToken(material.TokenId, material.Secret);

                        return true;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "API token persistence failed during create; no token was created");

                        return false;
                    }
                }
            }

            _logger.LogError("API token creation gave up after {Attempts} TokenId collisions", MaxInsertAttempts);

            return false;
        }


        public bool TryRestrictToken(Guid entityId, List<ApiTokenGrantEntity> remainingGrants, DateTime? shortenedExpiryUtc,
            string restrictedBy, out ApiTokenEntity entity)
        {
            entity = null;

            lock (_stateLock)
            {
                if (!TryGetTrackedToken(entityId, out var current))
                    return false;

                // A revoked token is terminal; "restrict succeeded" on a dead record would be
                // a misleading result for the management layer.
                if (current.RevokedAtUtc is not null)
                    return false;

                if (!ApiTokenGrants.TryCanonicalize(remainingGrants, out var canonicalRemaining))
                    return false;

                // Restriction can only remove pairs; any pair not in the current set is expansion.
                foreach (var grant in canonicalRemaining)
                    if (!current.Grants.Contains(grant))
                        return false;

                if (!TryShortenExpiry(current.ExpiresAtUtc, shortenedExpiryUtc, out var newExpiry))
                    return false;

                var restricted = current with
                {
                    Grants = canonicalRemaining,
                    ExpiresAtUtc = newExpiry,
                    RestrictedAtUtc = DateTime.UtcNow.Ticks,
                    RestrictedBy = restrictedBy,
                };

                return TryPersistAndPublish(restricted, out entity);
            }
        }


        public bool TryRotateToken(Guid entityId, DateTime? shortenedExpiryUtc, string rotatedBy,
            out ApiTokenEntity entity, out string fullToken)
        {
            entity = null;
            fullToken = null;

            lock (_stateLock)
            {
                if (!TryGetTrackedToken(entityId, out var current))
                    return false;

                if (current.RevokedAtUtc is not null)
                    return false;

                if (!TryShortenExpiry(current.ExpiresAtUtc, shortenedExpiryUtc, out var newExpiry))
                    return false;

                for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
                {
                    var material = ApiTokenMaterial.Generate();

                    // Stamped per attempt so a collision retry never persists a pre-retry timestamp.
                    var now = DateTime.UtcNow.Ticks;

                    var replacement = new ApiTokenEntity
                    {
                        EntityVersion = 1,
                        EntityId = Guid.NewGuid(),
                        TokenId = material.TokenId,
                        VersionByte = ApiTokenMaterial.CurrentVersionByte,
                        Verifier = ApiTokenVerifier.ComputeVerifier(ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes),
                        OwnerUserId = current.OwnerUserId,
                        // Captured under _stateLock, so a concurrent restrict cannot wedge a
                        // just-removed grant into the replacement.
                        GlobalRevocationGenerationAtIssue = GlobalRevocationGeneration,
                        OwnerRevocationGenerationAtIssue = GetOrLoadOwnerGeneration(current.OwnerUserId),
                        Name = current.Name,
                        Description = current.Description,
                        Grants = current.Grants,
                        CreatedAtUtc = now,
                        CreatedBy = rotatedBy,
                        ExpiresAtUtc = newExpiry,
                        RotatedAtUtc = now,
                        RotatedFromEntityId = current.EntityId,
                    };

                    // The source token is revoked in the same atomic write that inserts the
                    // replacement; both take effect together or not at all.
                    var revokedOld = current with { RevokedAtUtc = now, RevokedBy = rotatedBy, RevocationReason = "rotated" };

                    ApiTokenMaterial.Clear(material.SecretBytes);

                    try
                    {
                        if (!_databaseCore.TryRotateApiToken(revokedOld, replacement))
                            continue;

                        Publish(revokedOld);
                        Publish(replacement);

                        entity = replacement;
                        fullToken = ApiTokenMaterial.FormatToken(material.TokenId, material.Secret);

                        return true;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "API token persistence failed during rotation; the source token is unchanged");

                        return false;
                    }
                }
            }

            _logger.LogError("API token rotation gave up after {Attempts} TokenId collisions", MaxInsertAttempts);

            return false;
        }


        public bool TryRevokeToken(Guid entityId, string revokedBy, string reason, out ApiTokenEntity entity)
        {
            entity = null;

            lock (_stateLock)
            {
                if (!TryGetTrackedToken(entityId, out var current))
                    return false;

                // Idempotent: revoking an already revoked token succeeds without a rewrite.
                if (current.RevokedAtUtc is not null)
                {
                    entity = current;
                    return true;
                }

                var revoked = current with
                {
                    RevokedAtUtc = DateTime.UtcNow.Ticks,
                    RevokedBy = revokedBy,
                    RevocationReason = Sanitize(reason, MaxReasonLength),
                };

                return TryPersistAndPublish(revoked, out entity);
            }
        }


        public long AdvanceGlobalRevocationGeneration()
        {
            // Durable advance first; the in-memory value publishes only after it persisted.
            // Both steps under _stateLock: the worker serializes its read+write, so the
            // durable counter is monotonic, and serialized publication can never regress
            // the in-memory value below the durable one.
            lock (_stateLock)
            {
                var next = _databaseCore.AdvanceGlobalRevocationGeneration();

                Volatile.Write(ref _globalGeneration, next);

                return next;
            }
        }

        public long AdvanceOwnerRevocationGeneration(Guid ownerUserId)
        {
            lock (_stateLock)
            {
                var next = _databaseCore.AdvanceOwnerRevocationGeneration(ownerUserId);

                _ownerGenerations[ownerUserId] = next;

                return next;
            }
        }


        public int CountQuotaEligibleTokens(Guid ownerUserId)
        {
            if (!_tokenIdsByOwner.TryGetValue(ownerUserId, out var tokenIds))
                return 0;

            // One snapshot of both generations for the whole count, so every token is
            // judged against the same pair of values.
            var now = DateTime.UtcNow.Ticks;
            var globalGeneration = GlobalRevocationGeneration;
            var ownerGeneration = GetOwnerRevocationGeneration(ownerUserId);

            return tokenIds.Keys.Count(tokenId =>
                _tokensByTokenId.TryGetValue(tokenId, out var token) &&
                IsQuotaEligible(token, now, globalGeneration, ownerGeneration));
        }


        public void Dispose()
        {
        }


        private static bool IsQuotaEligible(ApiTokenEntity token, long nowTicks, long globalGeneration, long ownerGeneration) =>
            token.RevokedAtUtc is null &&
            (token.ExpiresAtUtc is null || token.ExpiresAtUtc.Value > nowTicks) &&
            token.GlobalRevocationGenerationAtIssue == globalGeneration &&
            token.OwnerRevocationGenerationAtIssue == ownerGeneration;

        private void LoadTokens()
        {
            lock (_stateLock)
            {
                foreach (var candidate in _databaseCore.GetAllApiTokens())
                {
                    if (!IsLoadable(candidate) || !ApiTokenGrants.TryCanonicalize(candidate.Grants, out var canonicalGrants))
                    {
                        // Fail closed: an unloadable record is simply never published to the
                        // authentication index and can never authenticate.
                        _logger.LogWarning("Skipping unloadable API token record (entity {EntityId}, version {Version})",
                            candidate?.EntityId, candidate?.EntityVersion);
                        continue;
                    }

                    // Publish the canonical grant list, not the raw row: a record written
                    // with a non-canonical boundary id must still restrict cleanly.
                    Publish(candidate with { Grants = canonicalGrants });
                }
            }
        }

        private void LoadGenerations()
        {
            lock (_stateLock)
            {
                try
                {
                    _globalGeneration = _databaseCore.GetGlobalRevocationGeneration();

                    foreach (var ownerUserId in _tokenIdsByOwner.Keys)
                        _ownerGenerations[ownerUserId] = _databaseCore.GetOwnerRevocationGeneration(ownerUserId);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "API token revocation generation state is unreadable; all API tokens will fail authentication until reloaded");

                    return;
                }

                // Regressed state (a record issued at a generation newer than the authoritative
                // one) can only mean damaged generation storage — fail closed as well.
                foreach (var token in _tokensByTokenId.Values)
                {
                    if (token.GlobalRevocationGenerationAtIssue > _globalGeneration ||
                        token.OwnerRevocationGenerationAtIssue > GetOwnerRevocationGeneration(token.OwnerUserId))
                    {
                        _logger.LogError("API token revocation generation state is regressed (token entity {EntityId}); all API tokens will fail authentication until reloaded",
                            token.EntityId);

                        return;
                    }
                }

                _isGenerationStateHealthy = true;
            }
        }

        private static bool IsLoadable(ApiTokenEntity entity) =>
            entity is not null &&
            entity.EntityVersion == 1 &&
            entity.VersionByte == ApiTokenMaterial.CurrentVersionByte &&
            ApiTokenMaterial.IsValidTokenId(entity.TokenId) &&
            entity.Verifier is { Length: 32 } &&
            entity.OwnerUserId != Guid.Empty &&
            entity.Grants is not null;

        private bool TryGetTrackedToken(Guid entityId, out ApiTokenEntity entity)
        {
            entity = GetTokenByEntityId(entityId);

            return entity is not null;
        }

        // Callers must hold _stateLock: the read-modify-write sequences this completes are
        // only atomic when the whole sequence runs under the lock.
        private bool TryPersistAndPublish(ApiTokenEntity entity, out ApiTokenEntity published)
        {
            published = null;

            try
            {
                _databaseCore.PutApiToken(entity);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "API token persistence failed for entity {EntityId}; the in-memory index is unchanged", entity.EntityId);

                return false;
            }

            Publish(entity);
            published = entity;

            return true;
        }

        private void Publish(ApiTokenEntity entity)
        {
            _tokensByTokenId[entity.TokenId] = entity;
            _tokenIdByEntityId[entity.EntityId] = entity.TokenId;
            _tokenIdsByOwner.GetOrAdd(entity.OwnerUserId, static _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(entity.TokenId, 0);
        }

        // Owners absent from the cache — no loadable records, e.g. because retention
        // removed them after an emergency revoke — still have a durable generation that
        // must be stamped on new tokens, not the missing-as-zero default. Read it once and
        // cache it. Only called under _stateLock, so a concurrent advance cannot interleave.
        private long GetOrLoadOwnerGeneration(Guid ownerUserId) =>
            _ownerGenerations.GetOrAdd(ownerUserId, static (id, database) => database.GetOwnerRevocationGeneration(id), _databaseCore);

        // Expiry can only be shortened: from an unlimited token to any finite value, or from
        // a finite value to an earlier one. Passing null keeps the current expiry.
        private static bool TryShortenExpiry(long? currentExpiryTicks, DateTime? requestedUtc, out long? newExpiryTicks)
        {
            newExpiryTicks = currentExpiryTicks;

            if (!requestedUtc.HasValue)
                return true;

            var requestedTicks = NormalizeUtcTicks(requestedUtc).Value;

            if (currentExpiryTicks.HasValue && requestedTicks > currentExpiryTicks.Value)
                return false;

            newExpiryTicks = requestedTicks;

            return true;
        }

        // DateTime inputs are UTC by contract (the parameter names say so). Kind.Local
        // values convert; Kind.Unspecified — an offset-less form or JSON value — is
        // interpreted as UTC rather than converted from the server's local zone, so a
        // stored expiry never shifts silently with the deployment timezone.
        private static long? NormalizeUtcTicks(DateTime? valueUtc) =>
            valueUtc.HasValue
                ? (valueUtc.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(valueUtc.Value, DateTimeKind.Utc)
                    : valueUtc.Value.ToUniversalTime()).Ticks
                : null;

        // Bounds and neutralizes free text before it is persisted or logged: control
        // characters (log forging, UI rendering) become spaces, then the value is trimmed
        // and truncated to the per-field limit.
        private static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var chars = value.Trim().ToCharArray();

            for (var i = 0; i < chars.Length; i++)
                if (char.IsControl(chars[i]))
                    chars[i] = ' ';

            var sanitized = new string(chars).Trim();

            return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
        }
    }
}
