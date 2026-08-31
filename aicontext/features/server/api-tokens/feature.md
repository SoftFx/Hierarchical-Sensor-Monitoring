# Feature: API tokens (authentication foundation)

> Owner: server | Last reviewed: 2026-08-31 | Canonical: yes
> Scope: durable personal API tokens (hsm_pat_v1_*) — material, verifier, grants, store, and the authoritative in-memory index with token lifecycle semantics.

---

## Overview

Personal API tokens are opaque bearer credentials for non-interactive management clients (AI agents, scheduled scripts, resident services). A user creates a token through the (cookie-authenticated, not yet built) management flow; the server generates the entire credential, persists only an irreversible verifier, and discloses the full token exactly once.

Status: this feature currently delivers the **persistence and domain foundation** (initiative `docs/initiatives/fine-grained-api-token-authentication.md`, issue #1356): token material, strict parsing, the domain-separated SHA-256 verifier, grant canonicalization, the LevelDB store, revocation generations, and the authoritative in-memory index with create/restrict/rotate/revoke semantics. The ASP.NET authentication scheme (`HsmApiToken`), `/api/v1` routes, cookie-only management endpoints, and the `ApiTokens.*` configuration section land in the follow-up PRs of the same sequence; until they land, nothing consumes `IApiTokenManager` at runtime except its boot-time `Initialize()`.

## Invariants

- Only the verifier `SHA-256(ASCII("HSM-API-TOKEN") || 0x00 || versionByte || tokenId[16] || secret[32])` is persisted; never a plaintext secret, pepper, or reversible material. The dummy verifier used on the unknown-TokenId path is drawn from the CSPRNG per process, so no presentable credential hashes to it — callers must still fail when no record was found, never treat the compare result alone as the decision.
- Token format is strict: `hsm_pat_v1_` + exactly 22 canonical unpadded Base64URL chars (128-bit TokenId) + `.` + exactly 43 canonical chars (256-bit secret). Padding, wrong lengths, foreign alphabet, and non-canonical aliases are rejected before any database access; the canonicality check inspects the trailing bits of the final character directly, so parsing allocates no strings that could hold an uncleared copy of the secret.
- Persist-first publication: every mutation writes durably before it enters the in-memory authentication index. A failed write leaves **neither durable nor live state**; token creation never goes through the generic `ConcurrentStorage.TryAdd`.
- Thread safety: the manager is a singleton reached from request threads. Every lifecycle mutation and generation advance runs its whole read → persist → publish sequence under one state lock, so a restrict/rotate derived from a pre-revocation snapshot can never durably overwrite a revocation, and in-memory generation values never regress below the durable counters. Read paths are lock-free and walk snapshot-safe concurrent structures only.
- `TryInsertApiToken`/`TryRotateApiToken` serialize the TokenId existence check with the write inside the database worker; a collision (false) retries with a **completely new** id/secret pair and never overwrites.
- Rotation is one atomic LevelDB batch: the source token is revoked and the replacement inserted together, or neither happens. Rotation copies the source grants verbatim (narrowing a token is what restriction is for) and never turns a finite expiry into an unlimited one, and the resulting expiry (requested or inherited) must not already be in the past — no one-time secret is disclosed for a dead credential. A dead source — revoked, already expired, or invalidated by a revocation generation — is refused: rotating an emergency-revoked token must not mint a live replacement that silently undoes the revoke.
- Restriction can only remove grant pairs or shorten expiry (unlimited → finite is allowed); any expansion attempt fails, and dead records are terminal — restriction of a revoked or generation-invalidated token is rejected. Null `remainingGrants` keeps the current grants (symmetric with null expiry); an explicit empty list strips every grant.
- Revocation is immediate and idempotent.
- Grants are explicit operation + boundary pairs (Global / Product id / Folder id), canonicalized and deduplicated before persistence; unknown operations, boundary kinds, or resource ids fail closed. Pairs are never recombinable. Rows are re-canonicalized at load, so the in-memory grant shape always matches the persisted contract.
- Revocation generations: every token captures the global and per-owner generation at issue. Emergency advances persist the new generation before publishing it; missing-as-zero is the fresh-install baseline, corrupt or regressed generation state makes the whole index fail closed (`IsGenerationStateHealthy = false`). An owner absent from the in-memory generation cache (no loadable records, e.g. after retention removed them) has the durable value read once on the next create/rotate and cached, so a post-cleanup token never carries a generation the durable state would invalidate after restart.
- Minting fails closed: `TryCreateToken`/`TryRotateToken` return `false` — never throw — while `IsGenerationStateHealthy` is false and when a generation row is unreadable at capture time. No credential is stamped with unproven generation values, which would work only until the rows are repaired and the server restarts and then be silently invalidated forever.
- Quota counting (`CountQuotaEligibleTokens`) counts only unexpired, unrevoked tokens issued at current generations — generation-invalidated tokens stop counting immediately, before any per-record reconciliation. Both generations are snapshotted once per count.
- Records with a future `EntityVersion`, a foreign version byte, invalid TokenId shape, malformed verifier, null grants, or invalid grants are skipped at load and can never authenticate (fail closed).
- All `DateTime` inputs are UTC by contract: `Kind.Local` converts, `Kind.Unspecified` (an offset-less form/JSON value) is interpreted as UTC — never converted from the server's local zone, so stored expiries do not shift with the deployment timezone.
- Free text is bounded and control-character-sanitized before persistence, so records cannot forge log lines: name ≤ 256, description ≤ 1024, revocation reason and the actor fields (created/restricted/rotated/revoked-by) ≤ 256 chars each. Truncation never splits a UTF-16 surrogate pair, keeping the live entity byte-identical to the JSON row it round-trips through.
- The live index has an `Unpublish` counterpart to the store's `RemoveApiToken`: retention removes the durable row first, then unpublishes — the reverse order would resurrect the record after restart.
- The full token string is returned by create/rotate exactly once and is never logged or persisted.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Boot: rebuild authentication index from LevelDB, load generations, mark health | server startup (`InitStorages`) |
| 2 | Create token: generate pair → canonicalize grants → persist-first insert → one-time disclosure | management service (cookie flow, later PR) |
| 3 | Restrict / rotate / revoke token with non-expansion guarantees | management service (later PR) |
| 4 | Emergency revoke-all / revoke-user: advance durable generation → publish | IsAdmin cookie flow (later PR) |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `IApiTokenManager` | `src/server/HSMServer/Authentication/IApiTokenManager.cs` | Lifecycle + index surface; no HTTP consumers yet |
| `ApiTokenEntity` / `ApiTokenGrantEntity` / `ApiTokenBoundaryKind` | `src/database/HSMDatabase.AccessManager/DatabaseEntities/` | LevelDB row shape; `EntityVersion` gates future upgrades |
| `IDatabaseCore` Api tokens region | `src/database/HSMDatabase.AccessManager/DatabaseSettings/IDatabaseCore.cs` | Store facade; token writes propagate failures (unlike neighboring regions) |
| `ApiTokenMaterial` / `ApiTokenVerifier` | `src/server/HSMServer/Authentication/` | Pure crypto: generation, strict parse, pinned verifier, dummy verifier |
| `ApiTokenOperations` / `ApiTokenGrants` | `src/server/HSMServer/Authentication/` | v1 permission catalog (illustrative until capability-inventory approval) and grant canonicalization |

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.LevelDB/DatabaseImplementations/EnvironmentDatabaseWorker.cs` (Api tokens region) | Durable rows (`ApiToken_<tokenId>`), generations, persist-first insert, atomic rotate batch |
| `src/database/HSMDatabase.LevelDB/Database.cs` (`PutBatch`) | Single-batch multi-row write primitive |
| `src/server/HSMServer/Authentication/ApiTokenManager.cs` | Authoritative index, lifecycle semantics, generation health, quota counting |
| `src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs` | `AddAsyncStorage<IApiTokenManager, ApiTokenManager>` registration (boot-time `Initialize`) |

## Data Flow

```
create:  Generate() ──► canonicalize grants ──► TryInsertApiToken (worker lock: exists? ──► Put)
                                        │ collision ──► new pair, retry (≤3)
                                        └ persisted ──► publish to _tokensByTokenId/_tokenIdByEntityId/_tokenIdsByOwner
auth (later PR): parse header ──► GetToken(tokenId) ──► stored-or-dummy verifier compare ──► revocation/expiry/generation/owner checks
```

## Storage / Persistence

- LevelDB `EnvironmentData`, rows keyed `ApiToken_<TokenId>` (UTF-8); JSON serialized, records with `init`-only properties, UTC-tick dates.
- Generation state: `ApiTokenGeneration_Global` and `ApiTokenGeneration_Owner_<ownerUserId>` hold plain long values.
- The `ApiToken_` / `ApiTokenGeneration_` prefixes differ before the first separator, so the full scan (`ReadAllApiTokens`) never picks up generation rows.
- Retention cleanup (bounded removal of revoked/expired/orphan records after `TokenRecordRetention`) is not yet implemented; it is part of the follow-up configuration PR.
- Last-used tracking: the field exists on the entity; the coalesced write path lands with the authentication handler PR.

## UI / Operator Visibility

Not operator-visible yet. The cookie-only management UI/API (create with one-time disclosure, list, restrict, rotate, revoke, emergency revoke) is a later PR in the sequence.

## Dependencies

- Depends on: `HSMDatabase` LevelDB stack (`IDatabaseCore`), `IAsyncStorage` boot initialization.
- Used by: (planned) `HsmApiToken` authentication handler, `/api/v1` management controllers, alert templates/schedules REST API (#1351/#1352).

## Tests

Coverage lives in `tests.md` next to this file.
