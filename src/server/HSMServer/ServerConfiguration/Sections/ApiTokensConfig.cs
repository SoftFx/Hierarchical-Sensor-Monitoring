using System;

namespace HSMServer.ServerConfiguration
{
    // Retention and abuse bounds of the API-token channel (#1356; initiative section
    // "Configuration"), plus the issuance-side knobs that land with the token-management
    // UI (step 4). Defaults are upgrade-safe: a deployment with no ApiTokens section in
    // config gets tokens fully DISABLED and must opt in explicitly, never the other way
    // round.
    public sealed class ApiTokensConfig
    {
        // Emergency authentication/issuance kill switch (initiative: "ApiTokens.Enabled =
        // false ... all API-token authentication plus create/rotate/restrict is denied
        // immediately. Cookie-authenticated list/revoke and IsAdmin emergency
        // revoke-user/revoke-all remain available for cleanup"). Default false: tokens
        // are a new channel and an upgraded deployment must enable them deliberately.
        public bool Enabled { get; set; }

        // Quota of LIVE tokens per user (unexpired, not revoked, issued at the current
        // global and owner revocation generations — exactly what
        // IApiTokenManager.CountQuotaEligibleTokens counts). Dead records never block
        // issuance; rotation replaces the source slot atomically.
        public int MaxTokensPerUser { get; set; } = 10;

        // Whether the "No expiration" option may be offered at all. Default true since
        // #1373: the create form preselects "No expiration", and the typical token
        // (an agent/integration credential) outlives any preset — forcing a renewal
        // date on it was the worse default. The knob remains the operator's opt-OUT
        // for deployments that do not accept unlimited credentials. Together with
        // MaxLifetime this is still a real policy bound: every finite lifetime is
        // capped at create time, and switching this off removes the only way past
        // the cap.
        public bool AllowNoExpiration { get; set; } = true;

        // Hard upper bound on the lifetime of a newly created token (expiry minus the
        // create instant), enforced by the create endpoint alongside the past-expiry
        // rule — without it, switching AllowNoExpiration off would only hide the
        // explicit option while a year-9999 custom date still minted a practically
        // permanent credential. One year by default: the longest preset the create
        // form offers. AllowNoExpiration is the only override, and it is the
        // operator's explicit decision.
        public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromDays(365);

        // Expiry preselected by the create form (the "recommended preset" the initiative
        // names). A preselect, not the bound: the create endpoint enforces MaxLifetime,
        // and a custom date may be shorter or longer than this value within it. Must be
        // at least one day — the form's presets are day-granular and a sub-day default
        // would render as "1 days".
        public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromDays(90);

        // Upper bound for both retention windows: the retention sweep computes
        // utcNow - retention, and a window large enough to underflow DateTime would throw
        // from RunOnce outside every per-pass try block — validated here instead, with
        // the key named. Ten years is far beyond any operational audit window.
        private static readonly TimeSpan MaxRetention = TimeSpan.FromDays(3650);

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
            if (MaxTokensPerUser < 1)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(MaxTokensPerUser)} must be at least 1 (was {MaxTokensPerUser}).");

            if (DefaultLifetime < TimeSpan.FromDays(1) || DefaultLifetime > MaxRetention)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(DefaultLifetime)} must be between 1 and {MaxRetention.TotalDays:0} days (was {DefaultLifetime.TotalDays:0.##} days).");

            if (MaxLifetime < TimeSpan.FromDays(1) || MaxLifetime > MaxRetention)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(MaxLifetime)} must be between 1 and {MaxRetention.TotalDays:0} days (was {MaxLifetime.TotalDays:0.##} days).");

            // The form preselects DefaultLifetime; a default the create endpoint would
            // refuse as over-cap would make the form's own preset a guaranteed error.
            if (DefaultLifetime > MaxLifetime)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(DefaultLifetime)} must not exceed {nameof(MaxLifetime)} (was {DefaultLifetime.TotalDays:0.##} days, cap {MaxLifetime.TotalDays:0.##} days).");

            if (TokenRecordRetention < TimeSpan.Zero || TokenRecordRetention > MaxRetention)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(TokenRecordRetention)} must be between 0 and {MaxRetention.TotalDays:0} days (was {TokenRecordRetention}).");

            if (SecurityEventRetention < TimeSpan.Zero || SecurityEventRetention > MaxRetention)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(SecurityEventRetention)} must be between 0 and {MaxRetention.TotalDays:0} days (was {SecurityEventRetention}).");

            if (InvalidAttemptRateLimit < 1)
                throw new InvalidOperationException(
                    $"ApiTokens.{nameof(InvalidAttemptRateLimit)} must be at least 1 (was {InvalidAttemptRateLimit}).");
        }
    }
}
