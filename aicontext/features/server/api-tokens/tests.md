# Tests: API tokens (authentication foundation)

> Owner: server | Last reviewed: 2026-09-10 | Canonical: yes

Coverage matrix for the token domain/persistence foundation, the HTTP
authentication/authorization surface, and the personal token management UI — all under
the #1384 owner-mirrored model (eternal tokens, read-only flag, no grants/expiry).

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

## Store (`ApiTokenStoreTests`, worker level)

- `TryInsertApiToken` persists a readable-back row; same TokenId twice → false with the original row intact.
- Atomic rotation batch writes revoked-old + replacement together; replacement TokenId collision → false with both rows untouched.
- Prefix scan returns only token rows (never generation rows) with the key's token id next to each row (so the loader can detect a key/payload mismatch); a scan failure propagates so boot fails the index closed (an empty result means fresh install, not outage); removal deletes the row and reports the outcome (`true` = row gone incl. already absent, `null` id throws); every write path rejects a null TokenId with nothing written.
- Generations: missing state reads 0; advances are monotonic and durable across reopen; corrupt state (unparsable or negative) throws.

## Manager (`ApiTokenManagerTests`, DatabaseCore level)

- Persist-first: create/rotate/revoke/advance publish only after the durable write; injected write failures (via `FailingDatabaseCore`) leave neither durable nor live state.
- Create: disclosed full token parses; stored verifier matches the presented secret; the durable row carries the read-only flag, an EMPTY grant list and no expiry; restart-safe reload keeps the flag; bad input (empty owner/name, over-long name) rejected; 50 tokens all unique.
- Create normalizes inputs: reason/actor fields are control-character-sanitized and truncated without splitting a surrogate pair or ending in the space of a replaced control character (the live entity stays identical to the reloaded row); input that sanitizes to nothing normalizes to null. Public results carry no verifier — the persisted verifier is read from the store when a test needs it.
- Revoke: immediate, idempotent, revoked tokens leave the quota count.
- Rename: persists the new name across restart, touches nothing but the name (the flag survives); a no-op rename succeeds without a rewrite; an empty name is rejected with the token unchanged; revoked and generation-invalidated tokens are terminal; a revocation racing a rename is never lost (in-memory and after reopen).
- Rotate: fresh EntityId/TokenId/secret, the name and the read-only flag carried over (a pure credential swap), old revoked atomically, quota slot replaced 1:1; the original creator survives rotation and the rotating actor lands in `RotatedBy`; rotation after a global or owner emergency revoke is refused — no live replacement is minted from a generation-invalidated source (checked in-memory and after reopen).
- Authenticate (`TryAuthenticate`): a valid credential returns the live record; every fail-closed reason returns false — garbage/unknown id/wrong secret (tampered but canonical), revoked, generation-invalidated by global or owner advance, and unhealthy boot state refusing even valid credentials.
- Generations: global advance invalidates every owner's quota immediately; owner advance invalidates only that owner; an owner with a durable generation but no cached value (post-retention) gets it read and cached on create, staying consistent across restart.
- Minting fails closed: create/rotate return false (never throw) while generation state is unhealthy or when the owner-generation fallback read hits an unreadable row; no durable or live state is left.
- Fail closed at load: an unreadable token-row scan marks the index unhealthy (empty scan ≠ fresh install); regressed generation state marks the index unhealthy; unloadable records (bad TokenId shape, foreign version byte) are skipped and never authenticate; a row whose key disagrees with its payload TokenId is skipped (not republished, key logged) while the index stays healthy; a grants-less JSON row cannot become a loadable record (the deserializer rejects it or it lands as a default array the loadable check refuses); two rows sharing an EntityId publish exactly one.
- **Pre-simplification rows (#1384)**: a row carrying a non-empty grant list, or a set expiry, never loads — it is registered as an orphan (retention clears it) and its bearer cannot authenticate; loading such a row as a full owner mirror would silently widen a deliberately narrow credential.
- `TryRemoveToken` removes the durable row and the live index together (fresh index does not resurrect the record; an already-absent row reports true — "gone" — and null ids false); an orphan row rejected at load (future `EntityVersion`) is still removed durably; a failed durable removal unpublishes nothing.
- Concurrency: parallel creates for one owner all publish while enumeration of the owner index never throws; revoke racing rename/rotate on one entity never loses the revocation (in-memory and after reopen); parallel generation advances return each durable value exactly once and leave the in-memory values equal to the durable counters.

## Authentication handler (`HsmApiTokenHandlerTests`)

- A valid bearer authenticates with exactly one minimal identity: authenticated, of the HsmApiToken scheme, owner + token id claims, `Identity.Name` never set.
- No/foreign credentials (missing header, Basic, bare Bearer, non-hsm bearer) are `NoResult` with no manager lookup — another scheme's business.
- Duplicated `Authorization` values are `NoResult` with no manager lookup (the `", "`-joined string would parse as the first value's scheme and hide the bearer).
- A credential claiming the `hsm_pat_` prefix but failing the shape check (short, no separator, foreign alphabet, wrong secret length) fails closed with no manager lookup.
- `ApiTokens.Enabled = false` (kill switch) rejects even a valid credential before any parse or lookup — the manager decision is never reached while the channel is off.
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

## Owner-mirror evaluator (`ApiTokenAuthorizationServiceTests`)

The #1384 privilege matrix, recomputed per call:
- Admin owner + read-write token: reads and writes allowed everywhere, including global (admin-only) scope; the same owner with a read-only token writes global scope → 403.
- Global resources are admin-only sight: a non-admin owner gets 404 regardless of the token.
- Manager owner + read-write token on own product → write allowed; cross-product → 404 (never a confirming 403).
- Viewer owner + read-write token → write 403, read allowed; **read-only token** → write 403 on a visible target, reads follow owner sight exactly (visible product allowed, invisible one 404); a read-only token writing an invisible target gets 404, not 403 (404-first anti-enumeration).
- Owner downgrade manager→viewer flips write to 403 while read stays allowed — no token change; **admin demoted mid-flight**: the token minted by an admin loses scoped targets entirely (404 via the owner-side gate) the moment the admin flag drops.
- Deleted owner, a token record missing at authorization time, or a token whose liveness re-check fails (revoked between authentication and authorization) → 404.
- The owner side has NO folder fallback (HSM materialises folder roles into per-product entries; per-product narrowing wins): folder Manager + per-product Viewer downgrade → write 403, read allowed; per-product role removal under a folder role → 404.
- A sensor resolves through its product's current boundary (a parentless sensor fails closed to 404, not a cast exception); a deleted product → 404.
- `IsVisible` (list filtering) is the owner-sight half of the read decision: the read-only flag never narrows lists, only item writes; a revoked-mid-request token filters everything out.
- The caller-wide gate `CanSeeAnyBoundary` (global resources): admin, product role and folder role owners pass (no event); an owner with no roles at all is denied with exactly one `AuthorizationDenied`; an unresolvable (revoked/missing) token is denied the same way.
- Denial security events preserve the decision: 404 denials are recorded as `AuthorizationNotFound`, 403 denials as `AuthorizationDenied` — the enumeration-probe signal stays visible in the stored trail; the operation field carries the literal `read`/`write` access mode.

## Profile endpoints (`ProfileControllerTests`)

- Create: valid request returns the one-time secret exactly once with the entity id; the read-only flag reaches the manager untouched (dropping or flipping it would silently change the credential's power).
- Create gates: `disabled` (no manager call at all), `unhealthy`, `quota` (at `MaxTokensPerUser`), `invalid_name` (blank and control-only), `create_failed` (manager false surfaces).
- Rename: the new name reaches the manager; blank names are `invalid_name`; foreign entity ids answer `not_found` with no manager call (indistinguishable from unknown); revoked and generation-invalidated tokens are `not_found`; `disabled` while the kill switch is on.
- Rotate: returns the new secret once; foreign ids `not_found`.
- Revoke: own token revoked with the signed-in actor; works with tokens disabled (the kill switch's documented cleanup path); `unhealthy` denies; foreign ids `not_found`.
- Page: lists only the caller's tokens and maps quota/state flags (`TokensEnabled` = `Enabled AND healthy`); the read-only flag surfaces on every row; timestamps are Unix milliseconds (entity ticks minus the .NET-epoch offset) — pinned against a known instant; a generation-invalidated record (emergency-revoke generation above the at-issue stamps, row timestamp unset) lists as `invalidated`, a revoked one as `revoked` — never `active` with live buttons.

## Manager quota (`ApiTokenManagerTests`)

- `TryCreateToken` with a configured `MaxTokensPerUser = 1`: first live token mints, the second is refused inside the same state-locked path (no caller-side check races past the cap); revoking frees the slot immediately without reconciliation; a null config (direct construction) is unlimited.

## Configuration (`ApiTokensConfigTests`)

- Defaults are upgrade-safe in the channel sense: channel disabled, quota 10, 30-day retention windows, invalid-attempt budget 60. The expiry knobs of the fine-granted model are gone with expiry itself (#1384).
- Startup validation: `MaxTokensPerUser` < 1, negative/oversized retention windows and `InvalidAttemptRateLimit` < 1 throw with the key named.

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

- Failures and authorization denials persist and round-trip with their safe identifiers (kind, token id, owner, access mode).
- Successes are sampled (16 recorded events → exactly 1 row); failures always recorded.
- Events are chronological and collision-free (distinct event ids); a failed write drops and counts (`DroppedCount` asserted) — never throws on the request path.
- A full queue drops and counts: with the background writer stalled inside the database call, capacity+3 records leave exactly 3 counted drops and never block the caller (`FullMode.Wait` makes `TryWrite` return false instead of silently evicting).

## Invalid-attempt limiter (`ApiTokenInvalidAttemptLimiterTests`)

- Within the per-source per-minute budget every attempt is recorded; over it, attempts are dropped and counted (`DroppedCount`).
- One source's exhausted budget never consumes another source's (nothing global is denied); a null/absent remote endpoint shares a single `?` bucket and cannot bypass the bound.
- Window rollover resets every source's budget (deterministic-clock seam); the source registry is bounded — the 1025th distinct source in one window is dropped, and a source already tracked in the window keeps the rest of its budget even when the registry is full.
- Constructor throws with the config key named for `InvalidAttemptRateLimit < 1`; a null config section throws (a wiring bug, not a default).
- Handler level: with a budget of 1, two failures from one source record exactly one event, and the authentication RESULT is unchanged either way; two failures from the same IP over different (ephemeral) ports share one budget — the port never widens the bound — while the recorded payload still carries the full `ip:port`.

## Retention (`ApiTokenRetentionCleanerTests`, DatabaseCore level; `ApiTokenStoreTests`, worker level)

- Dead rows (revoked) at or before `utcNow - TokenRecordRetention` are removed from durable storage AND the live index, atomically per row; halfway through the window nothing is eligible; a live row is never eligible. The inclusive cutoff is pinned bit-exactly: the durable `RevokedAtUtc` is read back and `RunOnce(death + retention)` removes while one tick before keeps.
- Orphan rows wait one window from the cleaner's first observation (a damaged row has no trustworthy clock), then are removed and pruned from the manager's orphan registry; the registry lists rejected rows by their STORAGE key and `TryRemoveToken` prunes it (`ApiTokenManagerTests`); a duplicate-EntityId row is deliberately not registered as an orphan (auto-deleting an ambiguous credential row is riskier than leaking it).
- Security events strictly older than `utcNow - SecurityEventRetention` are removed oldest-first in bounded batches; an event exactly at the cutoff survives; a non-positive limit is a no-op (worker-level `RemoveApiTokenSecurityEventsBefore`, including the bytewise-order pin). A backlog larger than one batch drains in repeated batches within a single pass, and a pass is capped at 50 batches — one sweep stays bounded.
- A storage failure in one pass (scan or removal) is isolated: `RunOnce` returns zeros and never throws; the next pass retries. The orphan-pass failure isolation is exercised with the removal actually attempted.
- `ApiTokensConfig.Validate` rejects negative and over-bound retention windows with the config key named (cleaner constructor); the upper bound keeps `utcNow - retention` from underflowing `DateTime` outside the per-pass try blocks.
- Tests share one LevelDB class fixture: the clock anchor is relative to the run (a hardcoded date would rot past the default retention), token-row tests pin the event window off so leftover event rows can never contaminate exact counts, and event-asserting tests drain the event table first.

## Emergency revoke (`ApiTokensAdminControllerTests`; `ApiTokenManagerTests`; `ApiTokenRetentionCleanerTests`)

Controller level (mocked manager/user manager/journal, direct action invocation):

- Surface contract: class-level `[AuthorizeIsAdmin]` (the `AccountController.Users` gate); both mutations are `[HttpPost]` + antiforgery-filtered; the summary GET is a plain GET.
- `UserTokenSummary` answers the live count for a known user and the not-found shape for an unknown one.
- Revoke-user: an unknown `userId` answers `not_found` without advancing; a wrong typed confirmation answers `invalid_confirmation` without advancing; the confirmation comparison is trimmed and case-insensitive (the deliberation is in the typing); empty/control-only/over-256 reasons answer `invalid_reason` without advancing.
- A valid revoke-user advances the owner generation, reports the new generation and the pre-advance affected count, and writes the journal audit-of-record (initiator, scope path with username and id, old/new generation, count, sanitized reason).
- A target with zero live tokens still advances and succeeds (idempotent by effect).
- Control characters in the reason are sanitized before the audit record (log-forging hygiene).
- A throwing generation advance answers a retryable `revoke_failed` with a non-empty `CorrelationId` (the trace identifier), no exception text on the wire, and a "(failed)" audit record carrying the same correlation id; the exception never escapes to the global handler.
- A journaling failure after a successful advance does not fail the response (the revoke already happened).
- Revoke-all: a case-variant or wrong phrase is denied (ordinal match on the literal `revoke-all`); a valid call advances the global generation, uses the global count accessor, and audits with the deployment scope and anchor.
- Both levers work in every degraded mode — the healthy flag flipped either way (the endpoints take no `Enabled`/health dependency at all).

Manager/counter level (`ApiTokenManagerTests`):

- `CountQuotaEligibleTokensGlobally` applies the per-owner IsLive rule across owners: revoked tokens never count; an owner-generation advance removes only that owner's live tokens from the global count; a token minted after an advance counts again; a global advance empties the count and a fresh token after it counts.

Retention/stamping level (`ApiTokenRetentionCleanerTests`):

- An emergency-revoked row (generation advanced, `RevokedAtUtc` still null) is stamped by the sweep's reconciliation pass with `RevokedBy = "emergency"` and a null per-row reason; the token was already dead to `IsTokenLive` before the stamp.
- The stamping pass refuses while the generation state is unhealthy — a manager whose token scan loaded the index but whose generation read failed (in-memory counters at zero, every row LOOKING invalidated) stamps nothing and leaves every `RevokedAtUtc` null, exactly like minting refusing unproven generations.
- A freshly stamped row is never removed by the pass that stamped it; after the retention window from the stamp it is removed (bit-exact readback of the stamp, inclusive boundary).
- Stamping is bounded per pass (limit + 1 invalidated rows → limit stamped, the rest next pass).
- Live rows (another owner's, at current generations) and personally revoked rows are never stamped — the personal revoke's actor and reason survive untouched.

## Negative coverage checklist

- [x] Malformed/oversized credentials rejected before database access
- [x] Collision never overwrites; retry uses a completely new pair
- [x] Write failure leaves neither durable nor live state
- [x] A token never exceeds its owner's CURRENT rights: the owner side is recomputed per request (downgrade, role removal, demotion all shrink the token immediately)
- [x] A read-only token can never write (403 on reachable targets, 404 on invisible ones)
- [x] Pre-simplification grant/expiry rows fail closed at load — no silent widening of old credentials
- [x] Emergency-revoked (generation-invalidated) tokens cannot be renamed or rotated
- [x] Corrupt/regressed generation state fails the whole index closed
- [x] Concurrent lifecycle mutations cannot lose or resurrect a revocation
- [x] Cookie-only principal rejected by the management policy; mixed identities fail closed
- [x] hsm_pat bearer on legacy routes: generic non-redirecting 401, no token lookup
- [x] /api/v1 unavailable on SensorPort and for unmarked/anonymous/policy-less endpoints
- [x] The hsm_pat_ credential never reaches a log: sink-level redaction covers the catch logger, inner exceptions and the outer exception handlers
- [x] Token principal never replaced by UserProcessorMiddleware
- [x] Token management is cookie-only: the endpoints sit behind the cookie-pinned default policy and the legacy bearer guard
- [x] Foreign entity ids are indistinguishable from unknown ones on every lifecycle endpoint
- [x] Kill switch denies authentication and issuance immediately; cookie list/revoke stay available
- [x] The full credential appears exactly once (create/rotate response only), never in list/page payloads
- [x] Emergency revoke is IsAdmin-cookie-only with antiforgery on every mutation, and answers typed-confirmation/reason validation without advancing anything
- [x] Emergency revoke works exactly in the degraded modes (disabled kill switch, unhealthy generation state) and never consults them
- [x] A failed generation advance reports a retryable correlation-id failure and never claims success; a failed audit write never retracts a completed revoke
- [x] Emergency-revoked rows are reconciled by the retention sweep (stamped, then reaped after the window); live and personally revoked rows are never stamped
