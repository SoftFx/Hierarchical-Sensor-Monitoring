# ADR-0006: Token usage sensors are keyed by owner login + token EntityId

**Status:** Accepted
**Date:** 2026-09-16
**Supersedes:** —

---

## Context

Issue #1402 adds per-token self-monitoring of management-API access (`/api/v1` + `/mcp`):
request rate and request duration per token, inside the `HSM Server Monitoring`
self-monitoring product. Each token's subtree needs a tree path, and an operator must be
able to correlate a subtree with the token that produced it. The tree is visible to
everyone with read rights on the self-monitoring product, so the key choice is also a
disclosure choice.

Two obvious keys are disqualified:

- **Token name** — not unique (a user may mint two tokens named "ci") and mutable (rename
  exists): paths can collide, and a rename orphans the accumulated history.
- **TokenId** — the public half of the credential and the authentication lookup key that
  management responses deliberately never disclose (anti-enumeration, ADR-0003). Keying
  the tree by it would leak the lookup key to everyone with sight on the self-monitoring
  product.

## Decision

A token's usage sensors live under `API tokens/<owner-login>/<token-EntityId>/…`:
collision-free (logins and EntityIds are each unique) and stable across token renames.
Rotation mints a fresh EntityId by design (ADR-0005: rotation is a pure credential swap),
so a rotated-away subtree is a new identity rather than a move — it retires through the
sensors' `SelfDestroy` retention instead of migrating.

The readability cost (an opaque id in the path) is bridged in the UI, not in the key:
the Profile token card displays the EntityId, making the tree-path ↔ token correlation a
glance.

## Consequences

- The subtree discloses each owner's login and token EntityIds to anyone with read
  rights on the self-monitoring product (typically admins) — a wider audience than the
  token's owner, consciously accepted: the login is not a secret inside the operator's
  own monitoring tree, the EntityId is already visible to its owner, and the TokenId
  stays absent. There is no per-user filtering of the subtree — it inherits the
  self-monitoring product's visibility.
- History survives restarts (stable paths, re-registration on the token's next use).
- The token name never appears in the tree, so no rename hooks are needed.

## Alternatives Considered

- Name-based paths: collide (non-unique names) and orphan history on rename.
- TokenId-based paths: leak the authentication lookup key to anyone with sight on the
  self-monitoring product.
- Per-user filtering of the subtree: rejected as scope — it would add a second
  permission model to a product that already has one (access rights on the
  self-monitoring product).

## References

- #1402, PR #1403
- ADR-0003 (scheme isolation, TokenId non-disclosure), ADR-0005 (owner-mirror tokens,
  rotation semantics)
- `aicontext/features/server/token-usage-monitoring/feature.md`
