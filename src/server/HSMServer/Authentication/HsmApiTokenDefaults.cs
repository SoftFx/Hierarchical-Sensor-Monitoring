namespace HSMServer.Authentication
{
    // Names shared by the authentication scheme, its policies and the middleware guards.
    // The scheme is deliberately NOT the default authenticate/challenge scheme: cookie keeps
    // both defaults, and HsmApiToken runs only from the explicit management policy.
    public static class HsmApiTokenDefaults
    {
        public const string AuthenticationScheme = "HsmApiToken";

        // Policy for /api/v1 data-management endpoints: authenticates through the HsmApiToken
        // scheme only (a cookie-only principal never satisfies it) and requires a principal
        // with exactly one HsmApiToken identity.
        public const string ManagementPolicy = "HsmApiTokenManagement";

        // Route area served by the management API. Listener-guarded: SitePort only.
        public const string ManagementAreaPath = "/api/v1";
    }

    // Claim types carried by a token principal. Minimal by design: enough to re-resolve the
    // authoritative token record and current owner state on every request, and nothing that
    // could pass the principal off as an interactive user.
    public static class HsmApiTokenClaims
    {
        public const string OwnerUserId = "hsm-token-owner";

        // The public TokenId (authentication lookup key), not the stable EntityId: security
        // events name tokens by the same id the credential holder derived.
        public const string TokenId = "hsm-token-id";
    }
}
