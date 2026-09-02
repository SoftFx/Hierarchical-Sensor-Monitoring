using System;

namespace HSMServer.Authentication
{
    // Per-request security events of the API-token channel (initiative step 3). These go
    // to the separate append-only sink, never to the entity-keyed lifecycle journal.
    public enum ApiTokenSecurityEventKind : byte
    {
        AuthSucceeded = 1,
        AuthFailed = 2,
        AuthorizationDenied = 3,

        // A denial that answered 404 (target invisible or outside the token's reach) —
        // kept distinct from a 403 scope denial so the enumeration-probe signal stays
        // visible in the stored trail. Append-only like the rest of the enum (stored byte).
        AuthorizationNotFound = 4,
    }

    // Event payload without storage concerns. Only safe identifiers: the PUBLIC token id,
    // the owner subject id, the required permission, a safe target id, the request
    // correlation id and the request's source endpoint. Never a secret, a verifier, or
    // any reversible credential material.
    public sealed record ApiTokenSecurityEvent(
        ApiTokenSecurityEventKind Kind,
        string TokenId,
        Guid? OwnerUserId,
        string Operation = null,
        string TargetId = null,
        string CorrelationId = null,
        string Source = null);
}
