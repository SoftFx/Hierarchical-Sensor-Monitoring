# ADR-0005: An API token is an eternal owner mirror with a read-only flag

**Status:** Accepted
**Date:** 2026-09-10
**Supersedes:** ADR-0002, ADR-0004

---

## Context

The fine-grained API-token initiative (#1356, epic #1347) shipped a personal-credential
model where each token carried explicit `operation + boundary` grant pairs (Global /
Product / Folder, canonicalized, duplicates rejected) and an optional per-token expiry.
The evaluator answered `allowed = ownerCurrentlyAllows AND tokenGrantAllows` per request.

Two facts made the grants layer's cost exceed its value:

1. **The token side could never exceed the owner anyway.** The owner half of the
   conjunction was recomputed from the live stores on every request — downgrade, role
   removal and resource moves shrank a token immediately. The grants layer added no
   security boundary the owner did not already control; it added complexity (an
   operations catalog, canonicalization, a mint-side owner filter, a grants picker UI)
   and kept producing bugs rooted in the model itself (#1382: Global-boundary grants
   never matched scoped resources — a bug class that exists only because grants have
   shape).
2. **Nothing had shipped.** The last release predates the entire initiative, so the
   model could be replaced without compatibility burden — only internal dogfood tokens
   existed.

The expiry machinery had already been eroded in the same direction (#1373 made
no-expiration the default; ADR-0004), and the follow-up surface for it (#1377) was
still open when the reversal was decided.

## Decision

A personal API token is now a simple personal access token:

- **Full mirroring.** A token can do everything its owner can, across the whole
  `/api/v1` surface, including admin-only resources when the owner is an admin. Owner
  rights are recomputed per request, so the mirror shrinks with the owner instantly.
  Token management endpoints stay cookie-only.
- **One power knob.** A binary read-only flag, fixed at creation, default read-write.
  A read-only token reads exactly what its owner reads; every write operation is
  denied (403 on reachable targets). Changing a token's power = revoke + mint.
  Enforcement is belt-and-braces: the evaluator's per-resource `AuthorizeWrite` is
  primary; the management policy's requirement additionally denies unsafe HTTP
  methods for read-only credentials, method-shaped so it cannot leak per-target
  information.
- **Eternal.** No expiry anywhere. Remedies for a leaked credential: manual revoke,
  rotation (a pure credential swap — fresh ids/secret, same name and flag), admin
  emergency revokes, plus last-used tracking and per-user quotas.
- **Fail-closed load.** The durable schema keeps the grant/expiry/restriction fields
  but always writes them empty; a row carrying any of them is a pre-simplification
  record and never loads — loading it as a full mirror would silently *widen* a
  deliberately narrow credential.

## Consequences

- The operation catalog, grant canonicalization, the mint-side owner filter, the
  grants picker UI, the expiry pickers and the `AllowNoExpiration`/`DefaultLifetime`/
  `MaxLifetime` config knobs are deleted (ADR-0002's subject no longer exists;
  ADR-0004's default is moot — nothing expires).
- A leaked read-write token equals its owner's full write access until revoked — the
  consciously accepted trade, mitigated by last-used visibility, quotas, rotation and
  the emergency revokes.
- Pre-simplification dogfood tokens stop authenticating at boot after the upgrade and
  must be re-minted; the operator-visible signal is a boot-time `LogWarning`.
- If fine-grained scoping is ever wanted again, it is a new design on top of the
  mirror (e.g. per-token narrowing added back deliberately), not a restoration of the
  old grant model.
