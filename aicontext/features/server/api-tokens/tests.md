# Tests: API tokens (authentication foundation)

> Owner: server | Last reviewed: 2026-08-31 | Canonical: yes

Coverage matrix for the token domain/persistence foundation. The HTTP/security matrix (scheme
isolation, listener guards, 401/403/404 mapping, redaction) lands with the authentication
handler PR and extends this file.

## Token material (`ApiTokenMaterialTests`)

- Generated ids/secrets have exact lengths (22/43), Base64URL alphabet, and 16/32 decoded bytes.
- 10k-sample uniqueness for both id and secret.
- Format → strict parse round-trips bytes and pins `hsm_pat_v1_` → version byte 0x01.
- Rejected before any lookup: null/empty, wrong version prefix, missing/duplicated separator, wrong part lengths, padding, `+`/`/`/space characters.
- Non-canonical aliases (last char with non-zero trailing bits) rejected for both id and secret.
- `IsValidTokenId` checks shape + canonical encoding.

## Verifier (`ApiTokenVerifierTests`)

- `ComputeVerifier` matches an independently re-computed `SHA-256("HSM-API-TOKEN" || 0x00 || version || id[16] || secret[32])` — pins domain separation, ordering, and lengths.
- Changed version/id/secret produce different verifiers; wrong input lengths throw.
- `Verify` is constant-time compare: correct passes, tampered fails, wrong lengths fail.
- `DummyVerifier` is stable and never equals a real generated verifier.

## Grants (`ApiTokenGrantsTests`)

- Valid grants canonicalize: Guid ids to canonical form, deterministic (operation, boundary) order; same input in different order → same canonical list.
- Empty/null grant list is valid (a token that allows nothing).
- Fail closed: unknown operations (including `*`, `admin`, case variants, credential capabilities), unknown boundary kind, Global with a boundary id, resource boundary without a valid Guid, duplicate pairs, null entries.

## Store (`ApiTokenStoreTests`, worker level)

- `TryInsertApiToken` persists a readable-back row; same TokenId twice → false with the original row intact.
- Atomic rotation batch writes revoked-old + replacement together; replacement TokenId collision → false with both rows untouched.
- Prefix scan returns only token rows (never generation rows); removal deletes the row.
- Generations: missing state reads 0; advances are monotonic and durable across reopen; corrupt state throws.

## Manager (`ApiTokenManagerTests`, DatabaseCore level)

- Persist-first: create/rotate/revoke/advance publish only after the durable write; injected write failures (via `FailingDatabaseCore`) leave neither durable nor live state.
- Create: disclosed full token parses; stored verifier matches the presented secret; restart-safe reload; bad input (empty owner/name, invalid grants, past expiry) rejected; 50 tokens all unique.
- Revoke: immediate, idempotent, revoked tokens leave the quota count.
- Restrict: removes grants and shortens expiry (unlimited → finite allowed); expansion of pairs/boundaries and expiry extension rejected with the token unchanged.
- Rotate: fresh EntityId/TokenId/secret, grants and finite expiry preserved (never expanded, never made unlimited), old revoked atomically, quota slot replaced 1:1.
- Generations: global advance invalidates every owner's quota immediately; owner advance invalidates only that owner.
- Fail closed at load: regressed generation state marks the index unhealthy; unloadable records are skipped and never authenticate.

## Negative coverage checklist

- [x] Malformed/oversized credentials rejected before database access
- [x] Collision never overwrites; retry uses a completely new pair
- [x] Write failure leaves neither durable nor live state
- [x] Grant expansion impossible in place (restriction and rotation)
- [x] Unknown operations/boundaries/ids fail closed (validation and load)
- [x] Corrupt/regressed generation state fails the whole index closed
