using System;

namespace HSMServer.Model.ApiTokensAdmin
{
    // Wire shapes of the IsAdmin emergency-revoke surface (#1356 step 4, PR B): the
    // Users-page row action and the Configuration kill-switch companion both post here.
    // Same conventions as the profile mutations — JSON in/JSON out, cookie session,
    // antiforgery header, stable machine error codes — with the emergency additions of
    // a correlation id (failures) and the new generation + affected count (successes).
    public sealed class UserTokenSummaryResponse
    {
        public bool Ok { get; init; }

        public string UserName { get; init; }

        // Live tokens the confirmation modal warns about — the same advisory number the
        // audit records; not transactional with the eventual revoke.
        public int LiveTokens { get; init; }
    }

    public sealed class RevokeUserTokensRequest
    {
        public Guid UserId { get; set; }

        // The target's username, typed by the admin: names the target the way the
        // initiative's confirmation requirement demands and defeats wrong-row misclicks.
        public string Confirmation { get; set; }

        public string Reason { get; set; }
    }

    public sealed class RevokeAllTokensRequest
    {
        // The literal phrase RevokeAllConfirmationPhrase, typed by the admin.
        public string Confirmation { get; set; }

        public string Reason { get; set; }
    }

    // One JSON answer shape for both emergency mutations: Ok/Error/Message follow the
    // profile mutation contract; CorrelationId is present exactly when the durable
    // generation advance failed (HttpContext.TraceIdentifier — the same key that
    // locates the NLog record); NewGeneration/AffectedTokens are present on success.
    public sealed class EmergencyRevokeResponse
    {
        public bool Ok { get; init; }

        public string Error { get; init; }

        public string Message { get; init; }

        public string CorrelationId { get; init; }

        public long? NewGeneration { get; init; }

        public int? AffectedTokens { get; init; }
    }
}
