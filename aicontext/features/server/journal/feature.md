# Feature: Journal (unified configuration journal with rollback)

> Owner: server | Last reviewed: 2026-10-27 | Canonical: yes
> Scope: the single journal of every monitoring-tree configuration change (#1588) — the global Journal page, the per-node Journal tabs, the `/api/v1/journal` surface, the `list/get/rollback_journal_record` MCP tools, the entity-kind/product/undo-group tags on journal records, and the admin-only rollback by record. The undo mechanics of #1570 (before/after images, compensating writes, conflict detection) live on as internal machinery of this feature, addressed through journal records. Decision record: ADR-0013 (supersedes the surface decisions of ADR-0010).

---

## Overview

Every monitoring-tree configuration change — from the web editor, the REST management API and the MCP tools alike — lands in ONE journal with an entity kind, the root product and, for the rollbackable kinds, a link to its request's undo group. One HTTP request / MCP call / form post journals under exactly one server-minted undo group, so one rollback undoes the whole request. The web editor rides the same administration services as REST/MCP (the single write path); the cache layer keeps journaling settings and tree mutations it always did, now kind-tagged. Rollback is ADMIN-ONLY on every transport.

## Invariants

- **Single write path** — sensor data/TTL policy lists, alert templates and alert schedules from the web editor go through the administration services (per-node gates, authorization seam with the cookie twin, undo journaling), exactly like REST/MCP writes. Node settings and tree CRUD remain cache-layer writes (journal-visible, not rollbackable).
- **Per-request undo groups (#1588)** — group ids are SERVER-minted (minted once per HTTP request and cached in `HttpContext.Items`; one per MCP call; one per form post). There is no client-supplied id, no `X-HSM-Change-Set` header, no `changeSetId` MCP argument, and the #1570 fork rule is gone: an append always lands in the id it was given. `X-HSM-Dry-Run` stays as the write-preview switch.
- **Kinds** — every record carries one: `sensorDataPolicies`, `sensorTtlPolicies`, `productTtlPolicies`, `alertTemplate`, `alertSchedule` (rollbackable); `nodeSettings` (node general info/settings), `treeCrud` (product/sensor/folder create/delete/move), `folder` (folder edits). Legacy and non-configuration rows have null kind and filter as "other".
- **Rollbackable kinds stay the #1570 five, from all sources** — sensor data/TTL policies, product TTL, alert templates, alert schedules. The web editor's writes journal like REST/MCP writes. Tree CRUD and node settings are journal-visible only: sensor/product deletion irreversibly purges data, rollback is impossible by design.
- **Record-to-group links** — policy writes link through the write initiator (stamped with the group id; the cache-layer records copy it); templates and schedules have no cache journal hook, so the recorder IS their journal writer; every rollback entry leaves its own record (the redo handle is visible in the journal).
- **Rollback by record, admin-only everywhere** — the record's undo group replays through the internal engine. Web: admin-only (`[AuthorizeIsAdmin]`). REST/MCP: the token's OWNER must be `IsAdmin` (breaking change — Manager tokens are denied); per-resource write pre-flight stays as the second line of defense. A record without a group answers Conflict ("journal-visible only").
- **Conflict rule is content, never timestamps** (inherited from #1570) — unchanged, internal.
- **Rollback restores configuration only** — notifications already sent, alerts already fired and sensor history are untouched.
- **Durability trade-off (ADR-0010)** — journal appends run AFTER the primary write succeeds; storage failures are logged loudly and never fail an applied write.
- **No backfill** — records written before #1588 deserialize with null tags and show as "other"; pre-unification change sets are unreachable inert history in the internal store.

## Surfaces

- **Web** (`/Journal`, Configuration menu): the global page with author/time/kind/product filters, details modal (the record plus its undo group's before/after images), admin-only Rollback with the dry-run-backed confirmation dialog; the per-node Journal tabs show the same records for the node, with a Rollback button on rollbackable rows for admins.
- **REST**: `GET /api/v1/journal` (author/fromUtc/toUtc/kind/productId/entityId filters, limit ≤ 200 + offset), `GET /api/v1/journal/{recordId}` (record + undo-group details), `POST /api/v1/journal/{recordId}/rollback?force=&dryRun=` (admin owner only).
- **MCP**: `list_journal_records` (paged envelope), `get_journal_record`, `rollback_journal_record` (force/dryRun, admin owner only).
- **Internal (no external surface)**: the change-set store (`Databases/ChangeSets` LevelDB folder), `ChangeSetJournal`, `ChangeSetRecorder`, the rollback engine — reachable only through journal records.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Change configuration from any source; audit it in one journal | Operator/agent |
| 2 | Filter the global journal by author, time, kind or product | Operator/Viewer |
| 3 | Inspect a record: the change plus its undo group's before/after images | Operator (UI/API/MCP) |
| 4 | Roll back a record (refuses on conflicts with a diff; force overrides) | Admin (UI/API/MCP) |
| 5 | Redo a mistaken rollback via the rollback's own journal record | Admin |
| 6 | Audit what an agent/script did (author, time, kind filters) | Operator/Viewer |

## Known Limitations

- The global list scans the Changes-type records of the overlapping time shards and filters in memory (limit ≤ 200); the default window is 30 days.
- Records keyed by deleted entities resolve to the admin-only Global scope for sight filtering.
- The product filter matches the record's root product: folder-scoped and global entities (alert templates, alert schedules, folders) carry no product and do not match a product filter — filter by kind or time to audit them.
- The MCP list tool pages client-side over the first 200 visible records (the deleted change-sets list's semantics); deep pagination uses the REST offset.
- Schedule-delete rollback semantics are the #1570 ones (per-node full lists plus the touched template rows), now reached through the journal record.
