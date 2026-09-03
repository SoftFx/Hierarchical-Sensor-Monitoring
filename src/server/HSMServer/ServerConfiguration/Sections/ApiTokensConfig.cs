using System;

namespace HSMServer.ServerConfiguration
{
    // Retention and abuse bounds of the API-token channel (#1356; initiative section
    // "Configuration"). The issuance-side knobs (Enabled, DefaultLifetime,
    // AllowNoExpiration, MaxTokensPerUser) land with the token-management endpoints
    // (step 4); until then nothing creates tokens at runtime and these three bounds are
    // the ones with a live consumer. Defaults are upgrade-safe: no section in config
    // falls back to these values, not the other way round.
    public sealed class ApiTokensConfig
    {
        // Dead token records (revoked with RevokedAtUtc, expired with ExpiresAtUtc, and
        // rows rejected at load as orphans) remain durable and queryable for this window
        // after their death; a bounded background pass then removes them. Zero removes
        // eligible records on every pass.
        public TimeSpan TokenRecordRetention { get; set; } = TimeSpan.FromDays(30);

        // Independent retention window for the append-only per-request security-event
        // table (failures, denials, sampled successes). Independent on purpose: the
        // operational lifetime of an audit trail is a separate decision from the
        // lifecycle lifetime of the records it is about.
        public TimeSpan SecurityEventRetention { get; set; } = TimeSpan.FromDays(30);

        // Max recorded failed-authentication security events per remote source per
        // aligned one-minute window. Beyond the limit the event is dropped (counted and
        // logged by the limiter) — authentication itself is never throttled and valid
        // users are unaffected.
        public int InvalidAttemptRateLimit { get; set; } = 60;


        public void Validate()
        {
            if (TokenRecordRetention < TimeSpan.Zero)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(TokenRecordRetention)} must not be negative (was {TokenRecordRetention}).");

            if (SecurityEventRetention < TimeSpan.Zero)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(SecurityEventRetention)} must not be negative (was {SecurityEventRetention}).");

            if (InvalidAttemptRateLimit < 1)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(InvalidAttemptRateLimit)} must be at least 1 (was {InvalidAttemptRateLimit}).");
        }
    }
}
