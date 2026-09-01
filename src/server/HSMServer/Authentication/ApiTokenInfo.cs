using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Authentication
{
    // Read model returned by every manager query and lifecycle result: the durable
    // ApiTokenEntity minus the stored verifier. The verifier is credential material —
    // this projection makes it structurally impossible for a management controller to
    // serialize it into an HTTP response through a careless Ok(...). Mapping happens
    // inside ApiTokenManager only; the entity itself never crosses the manager boundary.
    public sealed record ApiTokenInfo
    {
        public int EntityVersion { get; init; }

        public Guid EntityId { get; init; }

        // Exactly 22 canonical unpadded Base64URL characters; shown for diagnostics,
        // never required to authenticate (the credential carries it by construction).
        public string TokenId { get; init; }

        public byte VersionByte { get; init; }

        public Guid OwnerUserId { get; init; }

        public long GlobalRevocationGenerationAtIssue { get; init; }

        public long OwnerRevocationGenerationAtIssue { get; init; }

        public string Name { get; init; }

        public string Description { get; init; }

        // ImmutableArray on purpose: a projection must not alias a mutable list a
        // consumer could edit in place (and through it the live index entry).
        public ImmutableArray<ApiTokenGrantEntity> Grants { get; init; }

        public long CreatedAtUtc { get; init; }

        // Who minted the credential; survives rotation (the rotating actor is RotatedBy).
        public string CreatedBy { get; init; }

        public long? RestrictedAtUtc { get; init; }

        public string RestrictedBy { get; init; }

        // Null = no expiration was explicitly confirmed at creation/rotation.
        public long? ExpiresAtUtc { get; init; }

        public long? LastUsedAtUtc { get; init; }

        public long? RotatedAtUtc { get; init; }

        public string RotatedBy { get; init; }

        public Guid? RotatedFromEntityId { get; init; }

        public long? RevokedAtUtc { get; init; }

        public string RevokedBy { get; init; }

        public string RevocationReason { get; init; }
    }
}
