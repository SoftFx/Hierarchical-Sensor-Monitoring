using System;
using System.Collections.Generic;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HSMServer.Authentication
{
    // Bounded retention sweep over the API-token durable state (initiative: cleanup in
    // bounded batches, eventually draining eligible records, restart/failure safe).
    // Three independent passes per run:
    //   1. dead token rows older than TokenRecordRetention (revoked or expired at or
    //      before the cutoff) — removed row-by-row through IApiTokenManager.TryRemoveToken,
    //      which is atomic across the durable row and the live index;
    //   2. orphan rows (rejected at load) — same removal, gated by an in-memory
    //      first-observation timestamp because a damaged row carries no trustworthy
    //      clock of its own (a restart re-observes and thus re-waits the window);
    //   3. security events strictly older than SecurityEventRetention, through the
    //      database's bounded prefix-range delete, repeated within the pass until the
    //      eligible backlog drains (capped, like every pass).
    // Every step is idempotent and bounded per run; a failure in one step logs and skips
    // to the next (the next pass retries), so a cleanup failure never wedges the sweep.
    public sealed class ApiTokenRetentionCleaner
    {
        // Bounded batches per pass. The token-row passes scan-and-remove up to
        // TokenRowBatchLimit rows per pass; the security-event pass repeats its bounded
        // delete until a batch comes back short, capped at MaxSecurityEventBatchesPerPass
        // per pass — a full batch means more eligible rows remain, and a fixed one-batch
        // drain (1000 rows/hour) would be slower than plausible ingest, let alone abuse.
        // The caps keep one pass cheap on a very large table while any real backlog
        // drains over consecutive hourly passes.
        public const int TokenRowBatchLimit = 100;
        public const int SecurityEventBatchLimit = 1000;
        public const int MaxSecurityEventBatchesPerPass = 50;

        private readonly IDatabaseCore _databaseCore;
        private readonly IApiTokenManager _tokens;
        private readonly ApiTokensConfig _config;
        private readonly ILogger<ApiTokenRetentionCleaner> _logger;

        // Orphan key -> first UTC moment this cleaner observed it (the manager registry
        // only lists keys; the observation clock lives here, with the retention policy).
        // Bounded by the manager's registry bound plus one pass. Plain Dictionary on
        // purpose: RunOnce has a single-threaded contract (see there), so the map needs
        // no synchronization.
        private readonly Dictionary<string, DateTime> _orphanFirstSeen = new(StringComparer.Ordinal);

        public ApiTokenRetentionCleaner(IDatabaseCore databaseCore, IApiTokenManager tokens,
            ApiTokensConfig config, ILogger<ApiTokenRetentionCleaner> logger)
        {
            _databaseCore = databaseCore ?? throw new ArgumentNullException(nameof(databaseCore));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? NullLogger<ApiTokenRetentionCleaner>.Instance;

            _config.Validate();
        }

        // One retention pass. Single-threaded contract: the orphan first-observation
        // map is a plain Dictionary, so exactly one caller (the retention background
        // service) may run passes at a time.
        public (int TokenRowsRemoved, int OrphanRowsRemoved, int SecurityEventsRemoved) RunOnce(DateTime utcNow)
        {
            var tokenCutoff = utcNow - _config.TokenRecordRetention;
            var eventCutoff = utcNow - _config.SecurityEventRetention;

            var tokensRemoved = RemoveDeadTokenRows(tokenCutoff);
            var orphansRemoved = RemoveOrphanRows(utcNow, tokenCutoff);
            var eventsRemoved = RemoveSecurityEvents(eventCutoff);

            if (tokensRemoved > 0 || orphansRemoved > 0 || eventsRemoved > 0)
                _logger.LogInformation(
                    "API token retention pass at {UtcNow:u}: {TokenRows} dead token rows, {OrphanRows} orphan rows, {SecurityEvents} security events removed (cutoffs {TokenCutoff:u} / {EventCutoff:u})",
                    utcNow, tokensRemoved, orphansRemoved, eventsRemoved, tokenCutoff, eventCutoff);

            return (tokensRemoved, orphansRemoved, eventsRemoved);
        }

        // Eligible: revoked at or before the cutoff, or expired at or before the cutoff.
        // Never a live record, whatever its age; a generation-invalidated record without
        // a per-row RevokedAtUtc (pre-reconciliation) is not eligible either — it keeps
        // its row until reconciliation stamps it (emergency-revoke reconciliation is the
        // management endpoint's job, a later step).
        private int RemoveDeadTokenRows(DateTime tokenCutoff)
        {
            var cutoffTicks = tokenCutoff.Ticks;
            var removed = 0;
            var scanned = 0;

            try
            {
                foreach (var (keyTokenId, entity) in _databaseCore.GetAllApiTokens())
                {
                    if (removed >= TokenRowBatchLimit)
                        break;

                    scanned++;

                    if (entity is null)
                        continue;

                    var deadAt = DeadAtTicks(entity);

                    if (deadAt is null || deadAt.Value > cutoffTicks)
                        continue;

                    if (_tokens.TryRemoveToken(keyTokenId))
                        removed++;
                    else
                        _logger.LogWarning(
                            "API token retention could not remove the dead row {KeyTokenId} (entity {EntityId}); it stays for the next pass",
                            keyTokenId, entity.EntityId);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "API token retention could not scan token rows this pass");
            }

            _logger.LogDebug("API token retention scanned {Scanned} token rows", scanned);

            return removed;
        }

        // The row died at whichever death came first — a row that expired before it was
        // revoked must not outlive its retention window just because the revoke stamp is
        // newer (feature.md: revoked OR expired at or before the cutoff).
        private static long? DeadAtTicks(HSMDatabase.AccessManager.DatabaseEntities.ApiTokenEntity entity) =>
            entity.RevokedAtUtc is { } revoked && entity.ExpiresAtUtc is { } expired
                ? Math.Min(revoked, expired)
                : entity.RevokedAtUtc ?? entity.ExpiresAtUtc;

        // Orphan rows were rejected at load, so their payloads are not trustworthy as a
        // clock; the window runs from this cleaner's first observation. Removal is the
        // same TryRemoveToken by the storage key. Storage failures are isolated per
        // pass, like everywhere else in the sweep — a throw here must not skip the
        // security-event pass that follows.
        private int RemoveOrphanRows(DateTime utcNow, DateTime tokenCutoff)
        {
            var removed = 0;

            try
            {
                foreach (var keyTokenId in _tokens.GetOrphanTokenIds())
                {
                    if (removed >= TokenRowBatchLimit)
                        break;

                    if (!_orphanFirstSeen.TryGetValue(keyTokenId, out var firstSeen))
                    {
                        firstSeen = utcNow;
                        _orphanFirstSeen[keyTokenId] = utcNow;
                    }

                    if (firstSeen > tokenCutoff)
                        continue;

                    if (_tokens.TryRemoveToken(keyTokenId))
                    {
                        _orphanFirstSeen.Remove(keyTokenId);
                        removed++;
                    }
                    else
                    {
                        _logger.LogWarning("API token retention could not remove the orphan row {KeyTokenId}; it stays for the next pass", keyTokenId);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "API token retention could not remove orphan rows this pass");
            }

            return removed;
        }

        private int RemoveSecurityEvents(DateTime eventCutoff)
        {
            var removed = 0;

            try
            {
                // The database delete is bounded per batch (one atomic write batch stays
                // cheap); a FULL batch means more eligible rows remain, so repeat until a
                // short batch or the per-pass cap — the interface's documented contract.
                for (var batch = 0; batch < MaxSecurityEventBatchesPerPass; batch++)
                {
                    var removedInBatch = _databaseCore.RemoveApiTokenSecurityEventsBefore(eventCutoff.Ticks, SecurityEventBatchLimit);

                    removed += removedInBatch;

                    if (removedInBatch < SecurityEventBatchLimit)
                        break;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "API token retention could not remove security events this pass");
            }

            return removed;
        }
    }
}
