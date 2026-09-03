using System;
using System.Collections.Generic;
using System.Threading;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HSMServer.Authentication
{
    // Volume bound for failed-authentication security events (initiative: "Token
    // authentication failed, rate-limited/coalesced"). Per remote source, per aligned
    // one-minute window: up to ApiTokens.InvalidAttemptRateLimit failures are recorded,
    // the rest are dropped. The source identity is the CALLER's choice — the handler
    // passes the remote IP (the port is ephemeral and must not widen the bound); a
    // null/empty source shares one "?" bucket. Only the EVENT is throttled —
    // authentication itself is never delayed or rejected here, valid users are
    // unaffected, and one abusive source does not consume another source's budget
    // (nothing global is denied; the bound exists so an unauthenticated party cannot
    // drive unbounded growth of the append-only table).
    public sealed class ApiTokenInvalidAttemptLimiter
    {
        // Registry bound per window: active sources cannot exceed this, so a spoofed-
        // source flood cannot grow the map without limit either.
        private const int MaxTrackedSources = 1024;

        private readonly int _limit;
        private readonly ILogger<ApiTokenInvalidAttemptLimiter> _logger;
        private readonly Func<DateTime> _utcNow;
        private readonly object _windowLock = new();

        // Current aligned window id and its per-source counters; replaced wholesale on
        // rollover, so stale sources die with the window instead of being swept.
        private long _windowId;
        private Dictionary<string, int> _windowCounts = new(StringComparer.Ordinal);

        // Two drop causes, counted and logged separately: an over-budget drop is one
        // noisy source (benign, the bound doing its job), a registry-full drop means
        // untracked sources are being denied outright this window — an operator needs
        // to tell those apart, so they cannot share one counter and log cadence.
        private long _droppedOverBudget;
        private long _droppedRegistryFull;


        public ApiTokenInvalidAttemptLimiter(ApiTokensConfig config, ILogger<ApiTokenInvalidAttemptLimiter> logger)
            : this(config, logger, utcNow: null)
        {
        }

        // Test seam for the window boundary (deterministic rollover tests).
        internal ApiTokenInvalidAttemptLimiter(ApiTokensConfig config, ILogger<ApiTokenInvalidAttemptLimiter> logger,
            Func<DateTime> utcNow)
        {
            // Null means a wiring bug, not a default config: silently substituting one
            // would hide it (the cleaner takes the same stance for the same section).
            ArgumentNullException.ThrowIfNull(config);

            // One validation site (names the offending config key); the limiter only
            // reads InvalidAttemptRateLimit but the same section drives the cleaner.
            config.Validate();

            _limit = config.InvalidAttemptRateLimit;
            _logger = logger ?? NullLogger<ApiTokenInvalidAttemptLimiter>.Instance;
            _utcNow = utcNow ?? (static () => DateTime.UtcNow);
        }

        // Events dropped before reaching the security-event sink (over the per-source
        // limit, or an untracked new source beyond the registry bound).
        public long DroppedCount => Volatile.Read(ref _droppedOverBudget) + Volatile.Read(ref _droppedRegistryFull);

        // True when this failed attempt may be recorded. Null/empty source (no remote
        // endpoint observed) shares a single "?" bucket — it must not bypass the bound.
        public bool TryAcquire(string source)
        {
            var key = string.IsNullOrEmpty(source) ? "?" : source;
            var windowId = _utcNow().Ticks / TimeSpan.TicksPerMinute;

            int countAfter;

            lock (_windowLock)
            {
                if (windowId != _windowId)
                {
                    _windowId = windowId;
                    _windowCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                if (!_windowCounts.TryGetValue(key, out var count))
                {
                    if (_windowCounts.Count >= MaxTrackedSources)
                    {
                        var dropped = Interlocked.Increment(ref _droppedRegistryFull);

                        if (dropped == 1 || dropped % MaxTrackedSources == 0)
                            _logger.LogWarning(
                                "API token invalid-attempt limiter source registry is full ({Capacity}); events from new sources are dropped ({Dropped} dropped so far)",
                                MaxTrackedSources, dropped);

                        return false;
                    }

                    count = 0;
                }

                countAfter = count + 1;
                _windowCounts[key] = countAfter;
            }

            if (countAfter > _limit)
            {
                var dropped = Interlocked.Increment(ref _droppedOverBudget);

                if (dropped == 1 || dropped % (_limit * (long)MaxTrackedSources) == 0)
                    _logger.LogWarning(
                        "API token invalid-attempt events dropped over the per-source limit ({Limit}/min): {Dropped} dropped so far",
                        _limit, dropped);

                return false;
            }

            return true;
        }
    }
}
