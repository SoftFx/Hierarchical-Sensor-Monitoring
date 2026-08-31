using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

        // Revocation reasons and actor fields (createdBy/restrictedBy/rotatedBy/revokedBy).
        private const int MaxFreeTextLength = 256;

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

            // A null logger would NRE inside the catch blocks that make Try* return false
            // — the failure would escape as an exception from a never-throws contract.
            _logger = logger ?? NullLogger<ApiTokenManager>.Instance;
        }


        public bool IsGenerationStateHealthy => _isGenerationStateHealthy;

        public long GlobalRevocationGeneration => Volatile.Read(ref _globalGeneration);


        public Task Initialize()
        {
            // Fail closed until the whole durable state — token rows and generation rows —
            // is proven readable and consistent. A swallowed scan failure would present an
            // empty index as a fresh install: every existing token would silently stop
            // authenticating while health reports true.
            _isGenerationStateHealthy = false;

            var tokensLoaded = LoadTokens();
            var generationsLoaded = LoadGenerations();

            if (tokensLoaded && generationsLoaded)
                _isGenerationStateHealthy = true;

            _logger.LogInformation(
                "API token index initialized: {TokenCount} tokens, global generation {Generation}, healthy = {Healthy}",
                _tokensByTokenId.Count, GlobalRevocationGeneration, _isGenerationStateHealthy);

            return Task.CompletedTask;
        }

        public ApiTokenEntity GetToken(string tokenId) =>
            tokenId is not null && _tokensByTokenId.TryGetValue(tokenId, out var entity) ? entity : null;

        public ApiTokenEntity GetTokenByEntityId(Guid entityId) =>
            _tokenIdByEntityId.TryGetValue(entityId, out var tokenId) ? GetToken(tokenId) : null;

        public List<ApiTokenEntity> GetTokensByOwner(Guid ownerUserId)
        {
            if (!_tokenIdsByOwner.TryGetValue(ownerUserId, out var tokenIds))
                return [];

            var tokens = new List<ApiTokenEntity>();

            // Direct enumeration of the concurrent set: .Keys would take every internal
            // lock and materialize a snapshot list just to be walked once.
            foreach (var entry in tokenIds)
                if (GetToken(entry.Key) is { } token)
                    tokens.Add(token);

            return tokens;
        }

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

            var sanitizedCreatedBy = Sanitize(createdBy, MaxFreeTextLength);

            lock (_stateLock)
            {
                // A token minted against unproven generation state would work until the
                // operator repairs the generation rows and restarts — then be silently
                // generation-invalidated forever. Refuse instead of minting it.
                if (!IsGenerationStateHealthy)
                {
                    _logger.LogWarning("API token creation refused: revocation generation state is not healthy");
                    return false;
                }

                // Both stamps are captured before the retry loop and inside a try: an
                // unreadable generation row must fail the Try* contract with false, not
                // escape as an exception from the middle of candidate construction.
                if (!TryCaptureGenerations(ownerUserId, out var globalGenerationAtIssue, out var ownerGenerationAtIssue))
                    return false;

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
                        GlobalRevocationGenerationAtIssue = globalGenerationAtIssue,
                        OwnerRevocationGenerationAtIssue = ownerGenerationAtIssue,
                        Name = sanitizedName,
                        Description = sanitizedDescription,
                        Grants = canonicalGrants,
                        CreatedAtUtc = DateTime.UtcNow.Ticks,
                        CreatedBy = sanitizedCreatedBy,
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

                // A generation-invalidated record (emergency revoke advances the generation
                // without touching the row) is exactly as dead even though RevokedAtUtc
                // stays null on it.
                if (!TryCaptureGenerations(current.OwnerUserId, out var globalGeneration, out var ownerGeneration))
                    return false;

                if (!IsIssuedAtCurrentGenerations(current, globalGeneration, ownerGeneration))
                    return false;

                // Null keeps the current grants — symmetric with shortenedExpiryUtc == null
                // keeping the current expiry — while an explicit empty list strips every
                // grant. The copy also keeps the restricted record from sharing the grant
                // list instance with its predecessor.
                List<ApiTokenGrantEntity> canonicalRemaining;

                if (remainingGrants is null)
                    canonicalRemaining = [.. current.Grants];
                else if (!ApiTokenGrants.TryCanonicalize(remainingGrants, out canonicalRemaining))
                    return false;

                // Restriction can only remove pairs; any pair not in the current set is
                // expansion. Set lookup, not List.Contains: with MaxGrants on both sides
                // the linear scan would be ~1M record comparisons inside _stateLock.
                var currentGrants = new HashSet<ApiTokenGrantEntity>(current.Grants);

                foreach (var grant in canonicalRemaining)
                    if (!currentGrants.Contains(grant))
                        return false;

                if (!TryShortenExpiry(current.ExpiresAtUtc, shortenedExpiryUtc, out var newExpiry))
                    return false;

                // A no-op request (same grants, unchanged expiry) succeeds without a durable
                // write or an audit stamp — nothing changed, so there is nothing to persist.
                if (newExpiry == current.ExpiresAtUtc && canonicalRemaining.SequenceEqual(current.Grants))
                {
                    entity = current;

                    return true;
                }

                var restricted = current with
                {
                    Grants = canonicalRemaining,
                    ExpiresAtUtc = newExpiry,
                    RestrictedAtUtc = DateTime.UtcNow.Ticks,
                    RestrictedBy = Sanitize(restrictedBy, MaxFreeTextLength),
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
                // Same refusal as create: a replacement minted against unproven generation
                // state would be silently invalidated after repair and restart.
                if (!IsGenerationStateHealthy)
                {
                    _logger.LogWarning("API token rotation refused: revocation generation state is not healthy");
                    return false;
                }

                if (!TryGetTrackedToken(entityId, out var current))
                    return false;

                if (current.RevokedAtUtc is not null)
                    return false;

                // Captured under _stateLock, so a concurrent restrict cannot wedge a
                // just-removed grant into the replacement; inside a try, so an unreadable
                // generation row fails the Try* contract with false instead of throwing.
                if (!TryCaptureGenerations(current.OwnerUserId, out var globalGenerationAtIssue, out var ownerGenerationAtIssue))
                    return false;

                // A generation-invalidated source (an emergency revoke advanced the
                // generation without touching the row) is as dead as a revoked one.
                // Without this check the replacement would be stamped with the current
                // generations — a live credential re-disclosing the killed token's
                // grants and silently undoing the emergency revoke.
                if (!IsIssuedAtCurrentGenerations(current, globalGenerationAtIssue, ownerGenerationAtIssue))
                {
                    _logger.LogWarning("API token rotation refused: token entity {EntityId} is invalidated by a revocation generation",
                        current.EntityId);

                    return false;
                }

                if (!TryShortenExpiry(current.ExpiresAtUtc, shortenedExpiryUtc, out var newExpiry))
                    return false;

                // Mirror of the create-time rule, on the resulting value: disclosing a
                // replacement secret that is already expired (requested or inherited)
                // would hand the caller a dead credential.
                if (newExpiry.HasValue && newExpiry.Value <= DateTime.UtcNow.Ticks)
                    return false;

                var sanitizedRotatedBy = Sanitize(rotatedBy, MaxFreeTextLength);

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
                        GlobalRevocationGenerationAtIssue = globalGenerationAtIssue,
                        OwnerRevocationGenerationAtIssue = ownerGenerationAtIssue,
                        Name = current.Name,
                        Description = current.Description,
                        // Own list instance: the replacement must not share grants with
                        // the revoked source record still held in the index.
                        Grants = [.. current.Grants],
                        CreatedAtUtc = now,
                        // The original creator survives rotation for the audit trail; the
                        // rotating actor is recorded separately. Once retention removes the
                        // source row, RotatedFromEntityId alone cannot answer "who minted
                        // this lineage".
                        CreatedBy = current.CreatedBy,
                        ExpiresAtUtc = newExpiry,
                        RotatedAtUtc = now,
                        RotatedBy = sanitizedRotatedBy,
                        RotatedFromEntityId = current.EntityId,
                    };

                    // The source token is revoked in the same atomic write that inserts the
                    // replacement; both take effect together or not at all.
                    var revokedOld = current with { RevokedAtUtc = now, RevokedBy = sanitizedRotatedBy, RevocationReason = "rotated" };

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
                    RevokedBy = Sanitize(revokedBy, MaxFreeTextLength),
                    RevocationReason = Sanitize(reason, MaxFreeTextLength),
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


        // Retention removal, atomic across both states: the durable delete and the index
        // unpublish happen inside ONE _stateLock hold. Splitting them (deleting the row
        // outside the lock, then unpublishing) would let a concurrent revoke/rotate —
        // blind writes that do not check the row still exists — rewrite the just-deleted
        // row between the two steps and resurrect it after the next restart. False when
        // no live record existed, or when the durable removal failed (the row may still
        // exist, so nothing is unpublished).
        public bool TryRemoveToken(string tokenId)
        {
            if (tokenId is null)
                return false;

            lock (_stateLock)
            {
                if (!_tokensByTokenId.TryGetValue(tokenId, out var entity))
                    return false;

                if (!_databaseCore.RemoveApiToken(tokenId))
                    return false;

                _tokensByTokenId.TryRemove(tokenId, out _);
                _tokenIdByEntityId.TryRemove(entity.EntityId, out _);

                if (_tokenIdsByOwner.TryGetValue(entity.OwnerUserId, out var tokenIds))
                {
                    tokenIds.TryRemove(tokenId, out _);

                    // Safe only because every Publish also holds _stateLock: no publisher
                    // can refill the bucket between the emptiness check and the removal.
                    if (tokenIds.IsEmpty)
                        _tokenIdsByOwner.TryRemove(entity.OwnerUserId, out _);
                }

                return true;
            }
        }


        // The single authentication decision, assembled once so a handler cannot get the
        // order or the set wrong: strict parse (no database access for garbage) → index
        // lookup by the canonical TokenId text → stored-or-dummy constant-time verifier
        // compare → and STILL fail when no record was found (the compare result alone is
        // never the decision) → liveness (revoked/expired/both generation stamps) → boot
        // health. Decoded buffers are zeroed on every path.
        public bool TryAuthenticate(string presentedToken, out ApiTokenEntity entity)
        {
            entity = null;

            if (!ApiTokenMaterial.TryParse(presentedToken, out var tokenIdBytes, out var secretBytes))
                return false;

            try
            {
                var candidateVerifier = ApiTokenVerifier.ComputeVerifier(
                    ApiTokenMaterial.CurrentVersionByte, tokenIdBytes, secretBytes);

                var token = GetToken(ApiTokenMaterial.TokenIdOf(presentedToken));

                if (token is null)
                {
                    // Equal work for the unknown-id path, then fail regardless: no
                    // presentable credential can authenticate without a stored record.
                    _ = ApiTokenVerifier.Verify(candidateVerifier, ApiTokenVerifier.DummyVerifier);

                    return false;
                }

                if (!ApiTokenVerifier.Verify(candidateVerifier, token.Verifier))
                    return false;

                if (!IsGenerationStateHealthy || !IsLive(token, DateTime.UtcNow.Ticks,
                        GlobalRevocationGeneration, GetOwnerRevocationGeneration(token.OwnerUserId)))
                    return false;

                entity = token;

                return true;
            }
            finally
            {
                ApiTokenMaterial.Clear(secretBytes);
                ApiTokenMaterial.Clear(tokenIdBytes);
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

            var count = 0;

            // Direct enumeration of the concurrent set: .Keys would take every internal
            // lock and materialize a snapshot list just to be walked once.
            foreach (var entry in tokenIds)
                if (_tokensByTokenId.TryGetValue(entry.Key, out var token) &&
                    IsLive(token, now, globalGeneration, ownerGeneration))
                    count++;

            return count;
        }


        public void Dispose()
        {
        }


        // The one liveness rule, shared by authentication and quota counting (callers
        // snapshot the generations once and pass them in): unrevoked, unexpired, issued
        // at exactly the current generations. TryRestrictToken/TryRevokeToken deliberately
        // do NOT gate on IsGenerationStateHealthy the way create/rotate do: revoking must
        // always work, and a narrowing persisted onto an already-dead row grants nothing.
        private static bool IsLive(ApiTokenEntity token, long nowTicks, long globalGeneration, long ownerGeneration) =>
            token.RevokedAtUtc is null &&
            (token.ExpiresAtUtc is null || token.ExpiresAtUtc.Value > nowTicks) &&
            token.GlobalRevocationGenerationAtIssue == globalGeneration &&
            token.OwnerRevocationGenerationAtIssue == ownerGeneration;

        // True when the token rows were scanned successfully (individual corrupt records
        // are skipped, but the scan itself must succeed). False fails the whole index
        // closed: an unreadable token region is not a fresh install.
        private bool LoadTokens()
        {
            List<ApiTokenEntity> candidates;

            try
            {
                candidates = _databaseCore.GetAllApiTokens();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "API token rows are unreadable; all API tokens will fail authentication until the server is restarted");

                return false;
            }

            lock (_stateLock)
            {
                foreach (var candidate in candidates)
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

            return true;
        }

        private bool LoadGenerations()
        {
            lock (_stateLock)
            {
                long globalGeneration;

                try
                {
                    globalGeneration = _databaseCore.GetGlobalRevocationGeneration();

                    // Direct enumeration of the owner map: .Keys would take every internal
                    // lock and materialize a snapshot list just to be walked once.
                    foreach (var owner in _tokenIdsByOwner)
                        _ownerGenerations[owner.Key] = _databaseCore.GetOwnerRevocationGeneration(owner.Key);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "API token revocation generation state is unreadable; all API tokens will fail authentication until the server is restarted");

                    return false;
                }

                Volatile.Write(ref _globalGeneration, globalGeneration);

                // Regressed state (a record issued at a generation newer than the authoritative
                // one) can only mean damaged generation storage — fail closed as well. ALL
                // offending records are logged, so an operator repairs in one pass instead of
                // restarting after each first offender.
                List<Guid> regressed = null;

                foreach (var token in _tokensByTokenId.Values)
                    if (token.GlobalRevocationGenerationAtIssue > globalGeneration ||
                        token.OwnerRevocationGenerationAtIssue > GetOwnerRevocationGeneration(token.OwnerUserId))
                        (regressed ??= []).Add(token.EntityId);

                if (regressed is not null)
                {
                    _logger.LogError("API token revocation generation state is regressed (token entities {EntityIds}); all API tokens will fail authentication until the server is restarted",
                        regressed);

                    return false;
                }
            }

            return true;
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


        // Callers must hold _stateLock: the capture must be atomic with the mutation that
        // stamps or checks the values. Fails the Try* contract with false when a
        // generation row is unreadable — the failure must not escape as an exception.
        private bool TryCaptureGenerations(Guid ownerUserId, out long globalGeneration, out long ownerGeneration)
        {
            globalGeneration = 0;
            ownerGeneration = 0;

            try
            {
                globalGeneration = GlobalRevocationGeneration;
                ownerGeneration = GetOrLoadOwnerGeneration(ownerUserId);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "API token revocation generation state is unreadable; no token state was changed");

                return false;
            }

            return true;
        }


        // A record issued at exactly the current generations. Emergency revoke advances a
        // generation instead of writing RevokedAtUtc on every row, so a generation-invalidated
        // record is as dead as a revoked one — lifecycle operations must refuse it.
        private static bool IsIssuedAtCurrentGenerations(ApiTokenEntity token, long globalGeneration, long ownerGeneration) =>
            token.GlobalRevocationGenerationAtIssue == globalGeneration &&
            token.OwnerRevocationGenerationAtIssue == ownerGeneration;


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
        // characters (log forging, UI rendering) become spaces, unpaired surrogates are
        // replaced with U+FFFD, then the value is trimmed and truncated to the per-field
        // limit. Truncation backs off one char when the cut would split a surrogate pair,
        // and re-trims so the result never ends in the space of a replaced control char —
        // both keep the live entity identical to the JSON row it round-trips through
        // (System.Text.Json substitutes U+FFFD for ill-formed UTF-16). Input that
        // sanitizes to nothing (whitespace/control-only) normalizes to null, so "empty"
        // has exactly one persisted shape.
        private static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var chars = value.Trim().ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];

                if (char.IsControl(c))
                {
                    chars[i] = ' ';
                    continue;
                }

                // An unpaired surrogate is ill-formed UTF-16: only its replacement survives
                // the JSON round-trip, so replace it here and keep both sides identical.
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= chars.Length || !char.IsLowSurrogate(chars[i + 1]))
                        chars[i] = '�';
                }
                else if (char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(chars[i - 1])))
                {
                    chars[i] = '�';
                }
            }

            var sanitized = new string(chars).Trim();

            if (sanitized.Length == 0)
                return null;

            if (sanitized.Length <= maxLength)
                return sanitized;

            var cut = char.IsHighSurrogate(sanitized[maxLength - 1]) ? maxLength - 1 : maxLength;

            var truncated = sanitized[..cut].TrimEnd();

            return truncated.Length == 0 ? null : truncated;
        }
    }
}
