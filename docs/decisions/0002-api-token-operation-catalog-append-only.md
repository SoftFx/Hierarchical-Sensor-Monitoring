# ADR-0002: The API token operation catalog is append-only; renames and removals require a migration

**Status:** Accepted
**Date:** 2026-09-01
**Supersedes:** —

---

## Context

Personal API tokens (#1356, epic #1347) persist grants as explicit `operation + boundary` pairs, where the operation must be a member of the `ApiTokenOperations` catalog (`src/server/HSMServer/Authentication/ApiTokenOperations.cs`). At boot, `ApiTokenManager.LoadTokens` re-canonicalizes every stored grant list; `ApiTokenGrants.TryCanonicalize` fails the whole record if a single operation is not in the catalog, and the record is then skipped — it never enters the authentication index, so the token stops authenticating until the catalog matches its grants again.

The catalog is explicitly labelled "v1; illustrative until the API capability inventory is approved by the initiative review" — it is expected to change before step 4/5 of the sequence ship real tokens. Without a rule, a "small cleanup" (renaming `system-health:read`, splitting `sensors:write`, dropping an unused operation) silently bricks every live token that carries the affected operation at the next restart, with no migration path.

## Decision

1. **The catalog is append-only once a release ships tokens that can exist outside a dev deployment.** New operations may be added at any time. An existing operation string must never be renamed, re-scoped, or removed without an explicit migration.
2. **A migration that changes or removes an operation must rewrite affected stored grants** (a persistence migration in the store PR that introduces the change), not rely on load-time rejection. Load-time rejection stays the safety net, not the mechanism.
3. **Load-time rejection is loud and specific**: `TryCanonicalize` reports the first offending grant ("operation 'x' is not in the catalog") and the skip warning logs it, so an operator sees *what* to fix, not just an entity id.
4. **Whole-record skip (not grant-dropping) is deliberate**: a token that silently loses one grant is a credential whose behavior changed invisibly to its owner; a token that fails loudly with a named reason is diagnosable. If the catalog ever must break compatibility without a migration, dropping the unknown grant (still fail-closed, less destructive) is the documented fallback — revisit this ADR before doing it.

Credential-bearing capabilities (`users:*`, `access-keys:*`, `credentials:*`, `server-settings:*`) remain excluded by the existing threat-review rule, which is stricter than append-only: adding one requires a separate review regardless of migration.

## Consequences

- Catalog reviews check for removals/renames the same way compatibility-sensitive DTO changes are reviewed.
- The boundary-kind map (`IsValidBoundary`) follows the same rule: tightening it invalidates stored grants.
- A token skipped for an unknown operation can be repaired by re-adding the operation (it loads again on the next restart) or by revoking it — both operator-actionable with the logged reason.
