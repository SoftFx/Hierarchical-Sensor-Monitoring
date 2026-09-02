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
