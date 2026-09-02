using System;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    // Append-only per-request security event of the API-token authentication channel
    // (initiative step 3): authentication success/failure and authorization denial. Kept
    // SEPARATE from the entity-keyed lifecycle journal on purpose — per-request volume
    // must never displace lifecycle audit records, and vice versa.
    //
    // Credential invariant: only the PUBLIC TokenId (the authentication lookup key, safe
    // to name — see the glossary) and non-secret metadata may appear here. Never the
    // secret, the verifier, or any reversible credential material.
    public sealed record ApiTokenSecurityEventEntity
    {
        // Random per-event id; also the uniqueness suffix of the storage key.
        public Guid EventId { get; init; } = Guid.NewGuid();

        // ApiTokenSecurityEventKind value.
        public byte Kind { get; init; }

        // Public token id; null when the credential never parsed.
        public string TokenId { get; init; }

        public Guid? OwnerUserId { get; init; }

        // Required permission for authorization denials; null for authentication events.
        public string Operation { get; init; }

        // Safe target identifier (resource kind + id) for authorization denials.
        public string TargetId { get; init; }

        // Request correlation (HttpContext.TraceIdentifier) when the event has a request.
        public string CorrelationId { get; init; }

        // Source endpoint of the request (remote ip:port), when permitted context exists.
        public string Source { get; init; }

        // UTC ticks.
        public long TimestampUtc { get; init; }
    }
}
