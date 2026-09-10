using System;
using System.Collections.Generic;

namespace HSMServer.Model.Profile
{
    // View models of the profile page (#1356 step 4, simplified by #1384): a read-only
    // card about the signed-in user plus the metadata-only projection of their API
    // tokens. The token view model is built from ApiTokenInfo and never carries
    // credential material — the full token exists exactly once, in the create/rotate
    // mutation response.
    public sealed class ProfilePageViewModel
    {
        public Guid UserId { get; init; }

        public string UserName { get; init; }

        public bool IsAdmin { get; init; }

        // Products the user has a role on (name + whether the role is Manager),
        // rendered as badges on the card the same way the Users page renders them.
        public IReadOnlyList<ProfileProductRoleViewModel> Products { get; init; }

        public IReadOnlyList<ProfileTokenViewModel> Tokens { get; init; }

        // Degraded-mode state consumed by the page script.
        public bool TokensEnabled { get; init; }

        public bool GenerationStateHealthy { get; init; }

        public int QuotaUsed { get; init; }

        public int QuotaMax { get; init; }
    }

    public sealed record ProfileProductRoleViewModel(string Name, bool IsManager);

    public sealed class ProfileTokenViewModel
    {
        public Guid EntityId { get; init; }

        public string Name { get; init; }

        // The token's power: full mirror of the owner's rights, minus every write
        // operation when true. Fixed at creation (#1384).
        public bool ReadOnly { get; init; }

        // "active", "revoked" or "invalidated" (an emergency-revoke generation passed
        // the at-issue stamps) — computed from the record's timestamps and generation
        // comparison.
        public string Status { get; init; }

        // Unix milliseconds (UTC).
        public long CreatedAtUnixMs { get; init; }

        public long? LastUsedAtUnixMs { get; init; }
    }

    public sealed class CreateTokenRequest
    {
        public string Name { get; set; }

        // True mints a read-only credential: every write operation of the management
        // API is denied; reads follow the owner's sight.
        public bool ReadOnly { get; set; }
    }

    public sealed class RenameTokenRequest
    {
        public Guid EntityId { get; set; }

        public string Name { get; set; }
    }

    public sealed class RotateTokenRequest
    {
        public Guid EntityId { get; set; }
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
