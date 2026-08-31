using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    // Durable record of a personal API token (hsm_pat_v1_*). Persists only the irreversible
    // domain-separated SHA-256 verifier; the secret exists exactly once, in the create/rotate
    // response. Identity: EntityId is stable and used by lifecycle routes, TokenId is the
    // public 128-bit authentication lookup key. Dates are UTC ticks, following AccessKeyEntity.
    public sealed record ApiTokenEntity
    {
        // Current serialization shape version. Records with a higher version must be
        // skipped (fail closed) until an upgrade path exists.
        public int EntityVersion { get; init; } = 1;

        public Guid EntityId { get; init; }

        // Exactly 22 canonical unpadded Base64URL characters.
        public string TokenId { get; init; }

        // 0x01 for the hsm_pat_v1_ token/verifier format.
        public byte VersionByte { get; init; }

        // 32-byte SHA-256 verifier; never the secret or any reversible material. Records
        // are shared with the live authentication index — never mutate in place.
        public byte[] Verifier { get; init; }

        public Guid OwnerUserId { get; init; }

        public long GlobalRevocationGenerationAtIssue { get; init; }

        public long OwnerRevocationGenerationAtIssue { get; init; }

        public string Name { get; init; }

        public string Description { get; init; }

        // Canonical grant list (see ApiTokenGrants; duplicate pairs are rejected before
        // persistence). Read-only at the type level: records are shared with the live
        // authentication index, so a mutating consumer cannot rewrite a live token's
        // grants in place. System.Text.Json deserializes this into a List.
        public IReadOnlyList<ApiTokenGrantEntity> Grants { get; init; }

        public long CreatedAtUtc { get; init; }

        // Who minted the credential; survives rotation (the rotating actor is RotatedBy).
        public string CreatedBy { get; init; }

        public long? RestrictedAtUtc { get; init; }

        public string RestrictedBy { get; init; }

        // Null only when no-expiration was explicitly confirmed at creation/rotation.
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
