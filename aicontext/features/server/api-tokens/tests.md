# Tests: API tokens (authentication foundation)

> Owner: server | Last reviewed: 2026-09-01 | Canonical: yes

Coverage matrix for the token domain/persistence foundation (steps 1–2) and the HTTP
authentication/authorization surface (step 3).

## Token material (`ApiTokenMaterialTests`)

- Generated ids/secrets have exact lengths (22/43), Base64URL alphabet, and 16/32 decoded bytes.
- 10k-sample uniqueness for both id and secret.
- Format → strict parse round-trips bytes and pins `hsm_pat_v1_` → version byte 0x01.
- `TokenIdOf` returns the canonical TokenId text (the index key) of a parsed token.
- Rejected before any lookup: null/empty, wrong version prefix, missing/duplicated separator, wrong part lengths, padding, `+`/`/`/space characters.
- Non-canonical aliases (last char with non-zero trailing bits) rejected for both id and secret.
- `IsValidTokenId` checks shape + canonical encoding.
- `Redact` keeps the public id and drops the secret (also truncated and repeated credentials — pinning the forward-only scan against an infinite loop); a separator that is not a literal `'.'` at offset 22 (percent-encoded `%2E`, a short id) still loses the whole tail; ordinary text passes unchanged.

## Verifier (`ApiTokenVerifierTests`)

- `ComputeVerifier` matches an independently re-computed `SHA-256("HSM-API-TOKEN" || 0x00 || version || id[16] || secret[32])` — pins domain separation, ordering, and lengths.
- Changed version/id/secret produce different verifiers; wrong input lengths throw.
- `Verify` is constant-time compare: correct passes, tampered fails, wrong lengths fail.
- `DummyVerifier` is CSPRNG-drawn and never equals a real generated verifier, nor the verifier of the all-zero id+secret credential (which parses canonically as 22 'A' + 43 'A') — the dummy must not be derivable from any presentable token.

## Grants (`ApiTokenGrantsTests`)

- Valid grants canonicalize: Guid ids to canonical form, deterministic (operation, boundary) order; same input in different order → same canonical list.
- Empty/null grant list is valid (a token that allows nothing); lists above `MaxGrants` (1024) fail closed, exactly at the bound still canonicalize; a server-wide operation (`system-health:read`) at a Product/Folder boundary and an empty-guid resource id fail closed.
- Fail closed: unknown operations (including `*`, `admin`, case variants, credential capabilities), unknown boundary kind, Global with a boundary id, resource boundary without a valid Guid, duplicate pairs, null entries.

## Store (`ApiTokenStoreTests`, worker level)

- `TryInsertApiToken` persists a readable-back row; same TokenId twice → false with the original row intact.
- Atomic rotation batch writes revoked-old + replacement together; replacement TokenId collision → false with both rows untouched.
- Prefix scan returns only token rows (never generation rows) with the key's token id next to each row (so the loader can detect a key/payload mismatch); a scan failure propagates so boot fails the index closed (an empty result means fresh install, not outage); removal deletes the row and reports the outcome (`true` = row gone incl. already absent, `null` id throws); every write path rejects a null TokenId with nothing written.
- Generations: missing state reads 0; advances are monotonic and durable across reopen; corrupt state (unparsable or negative) throws.

## Manager (`ApiTokenManagerTests`, DatabaseCore level)

- Persist-first: create/rotate/revoke/advance publish only after the durable write; injected write failures (via `FailingDatabaseCore`) leave neither durable nor live state.
- Create: disclosed full token parses; stored verifier matches the presented secret; restart-safe reload; bad input (empty owner/name, invalid grants, past expiry) rejected; 50 tokens all unique.
- Create normalizes inputs: `Kind.Unspecified` expiry is read as UTC (no local-zone shift); over-long name/description is rejected; reason/actor fields are control-character-sanitized and truncated without splitting a surrogate pair or ending in the space of a replaced control character (the live entity stays identical to the reloaded row); input that sanitizes to nothing normalizes to null. Public results carry no verifier — the persisted verifier is read from the store when a test needs it.
- Revoke: immediate, idempotent, revoked tokens leave the quota count.
- Restrict: removes grants and shortens expiry (unlimited → finite allowed); null grants keep the current grants (empty list strips all); a no-op request (grants unchanged, expiry unchanged) succeeds without a rewrite or audit stamp; expansion of pairs/boundaries and expiry extension rejected with the token unchanged; a revoked or generation-invalidated (emergency-revoked) token is rejected as terminal.
- Rotate: fresh EntityId/TokenId/secret, grants and finite expiry preserved (never expanded, never made unlimited), old revoked atomically, quota slot replaced 1:1; the original creator survives rotation and the rotating actor lands in `RotatedBy`; a past requested or inherited expiry is refused; rotation after a global or owner emergency revoke is refused — no live replacement is minted from a generation-invalidated source (checked in-memory and after reopen).
- Authenticate (`TryAuthenticate`): a valid credential returns the live record; every fail-closed reason returns false — garbage/unknown id/wrong secret (tampered but canonical), revoked, expired, generation-invalidated by global or owner advance, and unhealthy boot state refusing even valid credentials.
- Generations: global advance invalidates every owner's quota immediately; owner advance invalidates only that owner; an owner with a durable generation but no cached value (post-retention) gets it read and cached on create, staying consistent across restart.
- Minting fails closed: create/rotate return false (never throw) while generation state is unhealthy or when the owner-generation fallback read hits an unreadable row; no durable or live state is left.
- Fail closed at load: an unreadable token-row scan marks the index unhealthy (empty scan ≠ fresh install); regressed generation state marks the index unhealthy; unloadable records (bad TokenId shape, foreign version byte) are skipped and never authenticate; a row whose key disagrees with its payload TokenId is skipped (not republished, key logged) while the index stays healthy; a grants-less JSON row cannot become a loadable record (the deserializer rejects it or it lands as a default array the loadable check refuses — the entity type itself can no longer represent it); two rows sharing an EntityId publish exactly one; a row with a non-canonical boundary id loads canonicalized and still restricts.
- `TryRemoveToken` removes the durable row and the live index together (fresh index does not resurrect the record; an already-absent row reports true — "gone" — and null ids false); an orphan row rejected at load (future `EntityVersion`) is still removed durably; a failed durable removal unpublishes nothing.

## Operations catalog (`ApiTokenOperationsTests`)

- `All` has no duplicates and every member is accepted by `IsValid` (the management UI renders grant pickers from it); the exposed collection is a snapshot — mutating it cannot alter the catalog or `IsValid`.
- Naming discipline: every member ends with `:read` or `:write`, and `IsWrite` matches the suffix exactly — a member added outside the pattern would fail open as a Viewer-executable read, so the test fails the addition instead.
- `IsValid` rejects null/empty, whitespace and case variants, and plausible-but-absent operations.
- Concurrency: parallel creates for one owner all publish while enumeration of the owner index never throws; revoke racing restrict/rotate on one entity never loses the revocation (in-memory and after reopen); parallel generation advances return each durable value exactly once and leave the in-memory values equal to the durable counters.

## Authentication handler (`HsmApiTokenHandlerTests`)

- A valid bearer authenticates with exactly one minimal identity: authenticated, of the HsmApiToken scheme, owner + token id claims, `Identity.Name` never set.
- No/foreign credentials (missing header, Basic, bare Bearer, non-hsm bearer) are `NoResult` with no manager lookup — another scheme's business.
- Duplicated `Authorization` values are `NoResult` with no manager lookup (the `", "`-joined string would parse as the first value's scheme and hide the bearer).
- A credential claiming the `hsm_pat_` prefix but failing the shape check (short, no separator, foreign alphabet, wrong secret length) fails closed with no manager lookup.
- Failure events carry a TokenId only when it is canonical: a shape-valid credential with an attacker-chosen id alphabet records the failure with a null TokenId; a canonical-shaped failure records the public id.
- Manager rejection and deleted-owner both fail closed; challenge is a generic 401 with `WWW-Authenticate: Bearer` and no redirect.
- Success marks the token used exactly once; every failure path never marks it.

## Scheme isolation (`HsmApiTokenSchemeIsolationTests`)

- Cookie remains the default authenticate AND challenge scheme; the DefaultPolicy behind bare `[Authorize]` is pinned to cookie only.
- The HsmApiToken scheme is registered (handler type pinned) and never a default.
- The management policy accepts exactly the single-identity token principal and rejects: a cookie-only principal, a mixed cookie+token principal (fail closed as denial, not an exception), and an identity that merely claims the scheme name without the handler's claims.

## Route guards (`ApiTokenRouteGuardsTests`)

- Legacy bearer guard: an hsm_pat bearer outside `/api/v1` gets a plain non-redirecting 401 and never reaches the pipeline behind it — including when the credential hides in duplicated `Authorization` values (each value is inspected on its own); every other credential shape passes through; an hsm_pat bearer inside `/api/v1` passes to the area guard.
- Area guard: a fully marked endpoint passes on SitePort; the same endpoint is 404 on SensorPort; no matched endpoint, a missing `[ManagementApi]` marker, an anonymous endpoint, and a marker without the management policy are all 404 (unavailable by default); the reserved cookie-only `/api/v1/api-tokens` family passes with a cookie `[Authorize]` — but a reserved route with no `[Authorize]` at all (anonymous: no fallback policy exists), with the management policy, or with a scheme-bearing bare-policy `[Authorize]` is 404, and still SitePort-only; paths outside the area pass through untouched.

## Cookie login redirect (`MyCookieAuthenticationEventsApiTokenTests`)

- Inside `/api/v1` a failed cookie authorization is a plain non-redirecting 401 (the reserved family keeps the area's no-login-redirect contract); outside the area the LoginPath 302 redirect is preserved for browser flows.

## UserProcessor middleware (`UserProcessorMiddlewareApiTokenTests`)

- A token principal passes through UNCHANGED (strict mock proves no user resolution is attempted) — also when the token identity is not the principal's primary identity; a cookie principal is still replaced by the stored HSM user.

## Effective-rights evaluator (`ApiTokenAuthorizationServiceTests`)

The design's privilege-reduction matrix, recomputed per call:
- IsAdmin + explicit read grant → allowed; IsAdmin owner alone grants nothing (no grant covering the boundary → 404).
- Boundary covered but operation not granted → 403; manager owner + write grant on own product → allowed; cross-product → 404 (never a confirming 403).
- Viewer owner with a (forged) write grant → 403; owner downgrade manager→viewer flips write to 403 while read stays allowed — no token change.
- Deleted owner or a token record missing at authorization time → 404; a token whose liveness re-check fails (revoked between authentication and authorization) → 404.
- Folder grant covers the product currently in the folder; a product moved out → 404; a Global grant is never a wildcard over scoped targets.
- The owner side has NO folder fallback (HSM materialises folder roles into per-product entries; per-product narrowing wins): folder Manager + per-product Viewer downgrade → write 403, read allowed; per-product role removal under a folder role → 404.
- Global operations are admin-only; a sensor resolves through its product's current boundary (a parentless sensor fails closed to 404, not a cast exception); a deleted product → 404.
- `IsVisible` (list filtering) requires owner sight plus any grant at the boundary; a materialised folder-manager role enables product write.
- Denial security events preserve the decision: 404 denials are recorded as `AuthorizationNotFound`, 403 denials as `AuthorizationDenied` — the enumeration-probe signal stays visible in the stored trail.

## Pipeline order (`ManagementPipelineOrderTests`)

- ConfigureMiddleware registers the guards after `UseRouting` and before `UseAuthentication`/`UseAuthorization`/`UserProcessorMiddleware` — the ordering the per-middleware unit tests cannot see; a reorder fails this pin.

## Last-used coalescing (`ApiTokenLastUsedCoalescingTests`, DatabaseCore level)

- `MarkUsed` lands durably via the (Dispose-drained) flush and survives reload; unknown/null ids are ignored without throwing.
- `IsTokenLive` follows the lifecycle: live after create, false for unknown/null ids, false after revoke.
- A revocation recorded after the use but before the flush survives it, with the timestamp merged into the revoked row.

## Credential redaction at the log sink (`ApiTokenRedactionLayoutRendererTests`)

- The `${hsm-redacted}` wrapper (what every `nlog.config` target wraps message and exception text in) renders a line containing the public token id and the redaction marker, never the credential — including when the secret sits in an inner exception (the path where middleware-level wrapping used to leak it).
- Credential-free text renders unchanged.

## Security-event sink (`ApiTokenSecurityEventSinkTests`, DatabaseCore level)

- Failures and authorization denials persist and round-trip with their safe identifiers (kind, token id, owner, operation).
- Successes are sampled (16 recorded events → exactly 1 row); failures always recorded.
- Events are chronological and collision-free (distinct event ids); a failed write drops and counts (`DroppedCount` asserted) — never throws on the request path.
- A full queue drops and counts: with the background writer stalled inside the database call, capacity+3 records leave exactly 3 counted drops and never block the caller (`FullMode.Wait` makes `TryWrite` return false instead of silently evicting).

## Negative coverage checklist

- [x] Malformed/oversized credentials rejected before database access
- [x] Collision never overwrites; retry uses a completely new pair
- [x] Write failure leaves neither durable nor live state
- [x] Grant expansion impossible in place (restriction and rotation)
- [x] Emergency-revoked (generation-invalidated) tokens cannot be rotated or restricted
- [x] Unknown operations/boundaries/ids fail closed (validation and load)
- [x] Corrupt/regressed generation state fails the whole index closed
- [x] Concurrent lifecycle mutations cannot lose or resurrect a revocation
- [x] Cookie-only principal rejected by the management policy; mixed identities fail closed
- [x] hsm_pat bearer on legacy routes: generic non-redirecting 401, no token lookup
- [x] /api/v1 unavailable on SensorPort and for unmarked/anonymous/policy-less endpoints
- [x] The hsm_pat_ credential never reaches a log: sink-level redaction covers the catch logger, inner exceptions and the outer exception handlers
- [x] Token principal never replaced by UserProcessorMiddleware
- [x] Owner downgrade/deletion and resource moves take effect on the next request
- [x] Global grants never act as wildcards over scoped resources
