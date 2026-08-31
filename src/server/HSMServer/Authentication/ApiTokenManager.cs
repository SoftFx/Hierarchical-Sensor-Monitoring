using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class ApiTokenManager : IApiTokenManager
    {
        private const int MaxInsertAttempts = 3;
        private const int MaxReasonLength = 256;

        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<ApiTokenManager> _logger;

        // Guards consistent snapshot reads of the authoritative generations while a token
        // candidate captures its at-issue values.
        private readonly object _generationLock = new();

        private readonly ConcurrentDictionary<string, ApiTokenEntity> _tokensByTokenId = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<Guid, string> _tokenIdByEntityId = new();
        private readonly ConcurrentDictionary<Guid, HashSet<string>> _tokenIdsByOwner = new();
        private readonly ConcurrentDictionary<Guid, long> _ownerGenerations = new();

        // Volatile by convention: read on every authentication, flipped only during load or
        // advance. False means every API token authentication must fail closed.
        private volatile bool _isGenerationStateHealthy;


        public ApiTokenManager(IDatabaseCore databaseCore, ILogger<ApiTokenManager> logger)
        {
            _databaseCore = databaseCore ?? throw new ArgumentNullException(nameof(databaseCore));
            _logger = logger;
        }


        public bool IsGenerationStateHealthy => _isGenerationStateHealthy;

        public long GlobalRevocationGeneration => _globalGeneration;

        private long _globalGeneration;


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
                ? tokenIds.Select(GetToken).Where(token => token is not null).ToList()
                : [];

        public long GetOwnerRevocationGeneration(Guid ownerUserId) => _ownerGenerations.GetValueOrDefault(ownerUserId);


        public bool TryCreateToken(Guid ownerUserId, string name, string description, List<ApiTokenGrantEntity> grants,
            DateTime? expiresAtUtc, string createdBy, out ApiTokenEntity entity, out string fullToken)
        {
            entity = null;
            fullToken = null;

            if (ownerUserId == Guid.Empty || string.IsNullOrWhiteSpace(name))
                return false;

            if (!ApiTokenGrants.TryCanonicalize(grants, out var canonicalGrants))
                return false;

            if (expiresAtUtc.HasValue && expiresAtUtc.Value.ToUniversalTime() <= DateTime.UtcNow)
                return false;

            for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
            {
                var material = ApiTokenMaterial.Generate();

                ApiTokenEntity candidate;

                lock (_generationLock)
                {
                    candidate = new ApiTokenEntity
                    {
                        EntityVersion = 1,
                        EntityId = Guid.NewGuid(),
                        TokenId = material.TokenId,
                        VersionByte = ApiTokenMaterial.CurrentVersionByte,
                        Verifier = ApiTokenVerifier.ComputeVerifier(ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes),
                        OwnerUserId = ownerUserId,
                        GlobalRevocationGenerationAtIssue = _globalGeneration,
                        OwnerRevocationGenerationAtIssue = GetOwnerRevocationGeneration(ownerUserId),
                        Name = name.Trim(),
                        Description = description?.Trim(),
                        Grants = canonicalGrants,
                        CreatedAtUtc = DateTime.UtcNow.Ticks,
                        CreatedBy = createdBy,
                        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime().Ticks,
                    };
                }

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

            _logger.LogError("API token creation gave up after {Attempts} TokenId collisions", MaxInsertAttempts);

            return false;
        }


        public bool TryRestrictToken(Guid entityId, List<ApiTokenGrantEntity> remainingGrants, DateTime? shortenedExpiryUtc,
            string restrictedBy, out ApiTokenEntity entity)
        {
            entity = null;

            if (!TryGetTrackedToken(entityId, out var current))
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


        public bool TryRotateToken(Guid entityId, DateTime? shortenedExpiryUtc, string rotatedBy,
            out ApiTokenEntity entity, out string fullToken)
        {
            entity = null;
            fullToken = null;

            if (!TryGetTrackedToken(entityId, out var current))
                return false;

            if (current.RevokedAtUtc is not null)
                return false;

            if (!TryShortenExpiry(current.ExpiresAtUtc, shortenedExpiryUtc, out var newExpiry))
                return false;

            var now = DateTime.UtcNow.Ticks;

            for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
            {
                var material = ApiTokenMaterial.Generate();

                ApiTokenEntity replacement;
                ApiTokenEntity revokedOld;

                lock (_generationLock)
                {
                    replacement = new ApiTokenEntity
                    {
                        EntityVersion = 1,
                        EntityId = Guid.NewGuid(),
                        TokenId = material.TokenId,
                        VersionByte = ApiTokenMaterial.CurrentVersionByte,
                        Verifier = ApiTokenVerifier.ComputeVerifier(ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes),
                        OwnerUserId = current.OwnerUserId,
                        GlobalRevocationGenerationAtIssue = _globalGeneration,
                        OwnerRevocationGenerationAtIssue = GetOwnerRevocationGeneration(current.OwnerUserId),
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
                    revokedOld = current with { RevokedAtUtc = now, RevokedBy = rotatedBy, RevocationReason = "rotated" };
                }

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

            _logger.LogError("API token rotation gave up after {Attempts} TokenId collisions", MaxInsertAttempts);

            return false;
        }


        public bool TryRevokeToken(Guid entityId, string revokedBy, string reason, out ApiTokenEntity entity)
        {
            entity = null;

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
                RevocationReason = Sanitize(reason),
            };

            return TryPersistAndPublish(revoked, out entity);
        }


        public long AdvanceGlobalRevocationGeneration()
        {
            // Durable advance first; the in-memory value publishes only after it persisted.
            var next = _databaseCore.AdvanceGlobalRevocationGeneration();

            _globalGeneration = next;

            return next;
        }

        public long AdvanceOwnerRevocationGeneration(Guid ownerUserId)
        {
            var next = _databaseCore.AdvanceOwnerRevocationGeneration(ownerUserId);

            _ownerGenerations[ownerUserId] = next;

            return next;
        }


        public int CountQuotaEligibleTokens(Guid ownerUserId)
        {
            if (!_tokenIdsByOwner.TryGetValue(ownerUserId, out var tokenIds))
                return 0;

            var now = DateTime.UtcNow.Ticks;
            var ownerGeneration = GetOwnerRevocationGeneration(ownerUserId);

            return tokenIds.Count(tokenId =>
                _tokensByTokenId.TryGetValue(tokenId, out var token) &&
                IsQuotaEligible(token, now, ownerGeneration));
        }


        public void Dispose()
        {
        }


        private bool IsQuotaEligible(ApiTokenEntity token, long nowTicks, long ownerGeneration) =>
            token.RevokedAtUtc is null &&
            (token.ExpiresAtUtc is null || token.ExpiresAtUtc.Value > nowTicks) &&
            token.GlobalRevocationGenerationAtIssue == _globalGeneration &&
            token.OwnerRevocationGenerationAtIssue == ownerGeneration;

        private void LoadTokens()
        {
            foreach (var candidate in _databaseCore.GetAllApiTokens())
            {
                if (!IsLoadable(candidate))
                {
                    // Fail closed: an unloadable record is simply never published to the
                    // authentication index and can never authenticate.
                    _logger.LogWarning("Skipping unloadable API token record (entity {EntityId}, version {Version})",
                        candidate?.EntityId, candidate?.EntityVersion);
                    continue;
                }

                Publish(candidate);
            }
        }

        private void LoadGenerations()
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

        private static bool IsLoadable(ApiTokenEntity entity) =>
            entity is not null &&
            entity.EntityVersion == 1 &&
            ApiTokenMaterial.IsValidTokenId(entity.TokenId) &&
            entity.Verifier is { Length: 32 } &&
            entity.OwnerUserId != Guid.Empty &&
            ApiTokenGrants.AreValid(entity.Grants);

        private bool TryGetTrackedToken(Guid entityId, out ApiTokenEntity entity)
        {
            entity = GetTokenByEntityId(entityId);

            return entity is not null;
        }

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
            _tokenIdsByOwner.GetOrAdd(entity.OwnerUserId, _ => new HashSet<string>()).Add(entity.TokenId);
        }

        // Expiry can only be shortened: from an unlimited token to any finite value, or from
        // a finite value to an earlier one. Passing null keeps the current expiry.
        private static bool TryShortenExpiry(long? currentExpiryTicks, DateTime? requestedUtc, out long? newExpiryTicks)
        {
            newExpiryTicks = currentExpiryTicks;

            if (!requestedUtc.HasValue)
                return true;

            var requestedTicks = requestedUtc.Value.ToUniversalTime().Ticks;

            if (currentExpiryTicks.HasValue && requestedTicks > currentExpiryTicks.Value)
                return false;

            newExpiryTicks = requestedTicks;

            return true;
        }

        private static string Sanitize(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return null;

            var trimmed = reason.Trim();

            return trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength];
        }
    }
}
