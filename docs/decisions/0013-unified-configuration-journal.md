# ADR-0013: One journal — Change Sets absorbed into the Journal, single write path, admin-only rollback

**Status:** Accepted
**Date:** 2026-10-27
**Supersedes:** ADR-0010 (in part: client change-set ids, per-source journaling scope, rollback authorization)

---

## Context

ADR-0010 introduced the undo journal of change sets as a SEPARATE surface
from the regular per-node Journal: REST/MCP writes through the alert
administration services were journaled with before/after images, web-editor
writes deliberately bypassed journaling (accepted limitation), and clients
grouped writes under client-supplied `changeSetId`s. Two journals meant two
pages, two APIs, two MCP tool families — and the audit trail split by
transport: the web editor's configuration changes were invisible to the undo
journal, and the Journal tab showed only textual traces with no undo.

#1588 decided to unify: one journal as the single surface for ALL
configuration changes, with the undo mechanics as an internal implementation
detail.

## Decision

1. **Single surface.** The regular Journal is the only journal UI: a global
   "Journal" page in the Configuration menu (author/time/entity-kind/product
   filters) plus the existing per-node Journal tabs as a local view of the
   same records. The Change Sets page, `ChangeSetsController`,
   `GET /api/v1/change-sets`, the `list/get/rollback_change_set` MCP tools,
   the `X-HSM-Change-Set` header and the `changeSetId` MCP arguments are all
   REMOVED.
2. **Single write path.** The web editor stops writing policy lists,
   templates and schedules to the cache directly; those writes ride the same
   administration services as REST/MCP (per-node gates, the authorization
   seam with its cookie twin, undo journaling). This revises ADR-0010's
   rejection of transport convergence: the services, not the cache layer,
   remain the hook point — the cache layer still journals every settings/
   tree mutation it always did, now with entity-kind tags.
3. **Per-request undo groups.** There are no client change-set ids anymore.
   The server mints one group id per HTTP request / MCP tool call / form
   post; every journaled write of that request lands in that group, and one
   rollback undoes the whole request. The #1570 fork rule (foreign author,
   terminal set) is structurally gone with client ids. `X-HSM-Dry-Run`
   stays — a preview, not part of the removed concept.
4. **Journal records link to their undo group.** Every record carries the
   entity kind (`sensorDataPolicies`, `sensorTtlPolicies`,
   `productTtlPolicies`, `alertTemplate`, `alertSchedule`, `nodeSettings`,
   `treeCrud`, `folder`), the root product and, for the rollbackable kinds,
   the undo-group id. Policy writes link through the initiator (the
   cache-layer records stamp it); templates, schedules and rollback entries
   get their journal record from the recorder.
5. **Rollback by journal record, admin-only everywhere.** The web (global
   page and per-node tab) and the new `POST /api/v1/journal/{recordId}/rollback`
   + `rollback_journal_record` MCP tool demand an admin principal (the token
   owner must be `IsAdmin`). Per-resource write pre-flight stays as the
   second line of defense inside the rollback engine. BREAKING: Manager
   tokens lose rollback.
6. **The mechanics are internal.** The change-set store (the `ChangeSets`
   LevelDB folder) remains on disk as an internal, externally inaccessible
   store; before/after images, compensating writes, content-based conflict
   detection, dry-run and the per-request grouping continue to power
   rollback unchanged — addressed through journal records instead of
   client-held ids. No backfill: pre-unification change sets become
   unreachable history; the journal shows only changes made after
   deployment (records written before it lack the kind/group tags).

## Consequences

- The audit trail is complete per request regardless of transport: the web
  editor's saves journal exactly like REST/MCP writes.
- Rollbackable kinds stay the five of ADR-0010 (sensor data/TTL policies,
  product TTL, alert templates, alert schedules); tree CRUD (product/
  sensor/folder create/delete/move) and node settings are journal-visible
  only — a product deletion irreversibly purges data, rollback is
  impossible by design.
- Backup exclusion of the change-set store is unchanged (ADR-0010); the
  presentation Journal store was never part of the backup set either.
- The JournalValues storage gains the unified tags and a global Changes-type
  scan (one contiguous key range per time shard); legacy rows deserialize
  with null tags and filter as "other".

## Alternatives Considered

- Keep both surfaces and add cross-links: rejected — two journals with
  overlapping audit semantics was the problem being fixed.
- Move undo data INTO the JournalValues store: rejected — the sharded,
  per-node presentation store is a poor fit for full before/after images
  and status transitions; the internal store keeps that machinery.

## References

- #1588 (spec), #1570/#1578 (the undo journal this builds on)
- Supersedes in part: ADR-0010
- `aicontext/features/server/journal/feature.md`
