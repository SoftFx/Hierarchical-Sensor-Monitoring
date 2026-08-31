# Tests: API tokens (authentication foundation)

> Owner: server | Last reviewed: 2026-08-31 | Canonical: yes

Coverage matrix for the token domain/persistence foundation. The HTTP/security matrix (scheme
isolation, listener guards, 401/403/404 mapping, redaction) lands with the authentication
handler PR and extends this file.

## Token material (`ApiTokenMaterialTests`)

- Generated ids/secrets have exact lengths (22/43), Base64URL alphabet, and 16/32 decoded bytes.
- 10k-sample uniqueness for both id and secret.
- Format → strict parse round-trips bytes and pins `hsm_pat_v1_` → version byte 0x01.
- `TokenIdOf` returns the canonical TokenId text (the index key) of a parsed token.
- Rejected before any lookup: null/empty, wrong version prefix, missing/duplicated separator, wrong part lengths, padding, `+`/`/`/space characters.
- Non-canonical aliases (last char with non-zero trailing bits) rejected for both id and secret.
- `IsValidTokenId` checks shape + canonical encoding.

## Verifier (`ApiTokenVerifierTests`)

- `ComputeVerifier` matches an independently re-computed `SHA-256("HSM-API-TOKEN" || 0x00 || version || id[16] || secret[32])` — pins domain separation, ordering, and lengths.
- Changed version/id/secret produce different verifiers; wrong input lengths throw.
- `Verify` is constant-time compare: correct passes, tampered fails, wrong lengths fail.
- `DummyVerifier` is CSPRNG-drawn and never equals a real generated verifier, nor the verifier of the all-zero id+secret credential (which parses canonically as 22 'A' + 43 'A') — the dummy must not be derivable from any presentable token.

## Grants (`ApiTokenGrantsTests`)

- Valid grants canonicalize: Guid ids to canonical form, deterministic (operation, boundary) order; same input in different order → same canonical list.
- Empty/null grant list is valid (a token that allows nothing); lists above `MaxGrants` (1024) fail closed, exactly at the bound still canonicalize.
- Fail closed: unknown operations (including `*`, `admin`, case variants, credential capabilities), unknown boundary kind, Global with a boundary id, resource boundary without a valid Guid, duplicate pairs, null entries.

## Store (`ApiTokenStoreTests`, worker level)

- `TryInsertApiToken` persists a readable-back row; same TokenId twice → false with the original row intact.
- Atomic rotation batch writes revoked-old + replacement together; replacement TokenId collision → false with both rows untouched.
- Prefix scan returns only token rows (never generation rows); a scan failure propagates so boot fails the index closed (an empty result means fresh install, not outage); removal deletes the row and reports the outcome (`true` = row gone incl. already absent, `null` id throws); every write path rejects a null TokenId with nothing written.
- Generations: missing state reads 0; advances are monotonic and durable across reopen; corrupt state (unparsable or negative) throws.

## Manager (`ApiTokenManagerTests`, DatabaseCore level)

- Persist-first: create/rotate/revoke/advance publish only after the durable write; injected write failures (via `FailingDatabaseCore`) leave neither durable nor live state.
- Create: disclosed full token parses; stored verifier matches the presented secret; restart-safe reload; bad input (empty owner/name, invalid grants, past expiry) rejected; 50 tokens all unique.
- Create normalizes inputs: `Kind.Unspecified` expiry is read as UTC (no local-zone shift); name/description/reason/actor fields are control-character-sanitized and length-bounded; unpaired surrogates become U+FFFD and truncation neither splits a surrogate pair nor ends in the space of a replaced control character (the live entity stays identical to the reloaded row); input that sanitizes to nothing normalizes to null.
- Revoke: immediate, idempotent, revoked tokens leave the quota count.
- Restrict: removes grants and shortens expiry (unlimited → finite allowed); null grants keep the current grants (empty list strips all); a no-op request (grants unchanged, expiry unchanged) succeeds without a rewrite or audit stamp; expansion of pairs/boundaries and expiry extension rejected with the token unchanged; a revoked or generation-invalidated (emergency-revoked) token is rejected as terminal.
- Rotate: fresh EntityId/TokenId/secret, grants and finite expiry preserved (never expanded, never made unlimited), old revoked atomically, quota slot replaced 1:1; the original creator survives rotation and the rotating actor lands in `RotatedBy`; a past requested or inherited expiry is refused; rotation after a global or owner emergency revoke is refused — no live replacement is minted from a generation-invalidated source (checked in-memory and after reopen).
- Authenticate (`TryAuthenticate`): a valid credential returns the live record; every fail-closed reason returns false — garbage/unknown id/wrong secret (tampered but canonical), revoked, expired, generation-invalidated by global or owner advance, and unhealthy boot state refusing even valid credentials.
- Generations: global advance invalidates every owner's quota immediately; owner advance invalidates only that owner; an owner with a durable generation but no cached value (post-retention) gets it read and cached on create, staying consistent across restart.
- Minting fails closed: create/rotate return false (never throw) while generation state is unhealthy or when the owner-generation fallback read hits an unreadable row; no durable or live state is left.
- Fail closed at load: an unreadable token-row scan marks the index unhealthy (empty scan ≠ fresh install); regressed generation state marks the index unhealthy; unloadable records (bad TokenId shape, null grants, foreign version byte) are skipped and never authenticate; a row with a non-canonical boundary id loads canonicalized and still restricts.
- `TryRemoveToken` removes the durable row and the live index together (fresh index does not resurrect the record; idempotent false); a failed durable removal unpublishes nothing; null/absent ids report false.

## Operations catalog (`ApiTokenOperationsTests`)

- `All` has no duplicates and every member is accepted by `IsValid` (the management UI renders grant pickers from it); the exposed collection is a snapshot — mutating it cannot alter the catalog or `IsValid`.
- `IsValid` rejects null/empty, whitespace and case variants, and plausible-but-absent operations.
- Concurrency: parallel creates for one owner all publish while enumeration of the owner index never throws; revoke racing restrict/rotate on one entity never loses the revocation (in-memory and after reopen); parallel generation advances return each durable value exactly once and leave the in-memory values equal to the durable counters.

## Negative coverage checklist

- [x] Malformed/oversized credentials rejected before database access
- [x] Collision never overwrites; retry uses a completely new pair
- [x] Write failure leaves neither durable nor live state
- [x] Grant expansion impossible in place (restriction and rotation)
- [x] Emergency-revoked (generation-invalidated) tokens cannot be rotated or restricted
- [x] Unknown operations/boundaries/ids fail closed (validation and load)
- [x] Corrupt/regressed generation state fails the whole index closed
- [x] Concurrent lifecycle mutations cannot lose or resurrect a revocation
