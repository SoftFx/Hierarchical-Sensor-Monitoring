using System;
using System.Collections.Generic;

namespace HSMServer.Model.Profile
{
    // View models of the profile page (#1356 step 4): a read-only card about the
    // signed-in user plus the metadata-only projection of their API tokens. The token
    // view model is built from ApiTokenInfo and never carries credential material —
    // the full token exists exactly once, in the create/rotate mutation response.
    public sealed class ProfilePageViewModel
    {
        public Guid UserId { get; init; }

        public string UserName { get; init; }

        public bool IsAdmin { get; init; }

        // Products the user has a role on (name + whether the role is Manager),
        // rendered as badges on the card the same way the Users page renders them.
        public IReadOnlyList<ProfileProductRoleViewModel> Products { get; init; }

        public IReadOnlyList<ProfileTokenViewModel> Tokens { get; init; }

        // Structured grants keyed by entity id (string): the restrict modal preloads its
        // picker from this map without scraping badges out of the rendered DOM.
        public IReadOnlyDictionary<string, IReadOnlyList<ProfileTokenGrantViewModel>> GrantsByTokenId { get; init; }

        // Degraded-mode and form state consumed by the page script.
        public bool TokensEnabled { get; init; }

        public bool GenerationStateHealthy { get; init; }

        public bool AllowNoExpiration { get; init; }

        public int QuotaUsed { get; init; }

        public int QuotaMax { get; init; }

        public int DefaultLifetimeDays { get; init; }
    }

    public sealed record ProfileProductRoleViewModel(string Name, bool IsManager);

    // One grant of a listed token: the canonical pair plus the boundary's display name
    // ("removed boundary" when the anchored product/folder no longer exists — the grant
    // still resolves server-side, it just matches nothing).
    public sealed record ProfileTokenGrantViewModel(string Operation, string BoundaryKind, string BoundaryId, string BoundaryName);

    public sealed class ProfileTokenViewModel
    {
        public Guid EntityId { get; init; }

        public string Name { get; init; }

        public string Description { get; init; }

        public IReadOnlyList<ProfileTokenGrantViewModel> Grants { get; init; }

        // "active", "expired", "revoked" or "invalidated" (an emergency-revoke
        // generation passed the at-issue stamps) — computed from the record's
        // timestamps and generation comparison.
        public string Status { get; init; }

        // Unix milliseconds (UTC); null ExpiresAt means no expiration.
        public long CreatedAtUnixMs { get; init; }

        public long? ExpiresAtUnixMs { get; init; }

        public long? LastUsedAtUnixMs { get; init; }
    }

    // Wire shape of one requested grant pair: a catalog operation plus the boundary it
    // is anchored at ("global" carries no id, "product"/"folder" carry a Guid text).
    public sealed class ProfileGrantRequest
    {
        public string Operation { get; set; }

        public string BoundaryKind { get; set; }

        public string BoundaryId { get; set; }
    }

    public sealed class CreateTokenRequest
    {
        public string Name { get; set; }

        public string Description { get; set; }

        // Null = explicitly confirmed "No expiration" (gated by AllowNoExpiration).
        public DateTime? ExpiresAtUtc { get; set; }

        public List<ProfileGrantRequest> Grants { get; set; }
    }

    public sealed class RestrictTokenRequest
    {
        public Guid EntityId { get; set; }

        // The full remaining grant set; an empty list strips every grant. Null expiry
        // keeps the current one.
        public List<ProfileGrantRequest> Grants { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }
    }

    public sealed class RotateTokenRequest
    {
        public Guid EntityId { get; set; }

        // Null inherits the source expiry.
        public DateTime? ExpiresAtUtc { get; set; }
    }

    public sealed class RevokeTokenRequest
    {
        public Guid EntityId { get; set; }

        public string Reason { get; set; }
    }

    // One JSON answer shape for every mutation: Ok tells the script whether to show the
    // one-time secret modal (Token is present only in create/rotate successes); Error is
    // a stable machine code the script can branch on; Message is display text.
    public sealed class ProfileMutationResponse
    {
        public bool Ok { get; init; }

        public string Error { get; init; }

        public string Message { get; init; }

        public string Token { get; init; }

        public Guid? EntityId { get; init; }
    }
}
