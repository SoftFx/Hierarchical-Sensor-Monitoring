# Feature: Change Sets (undo journal with rollback)

> Owner: server | Last reviewed: 2026-10-19 | Canonical: yes
> Scope: the undo journal of #1570 — change-set grouping of REST/MCP configuration writes, before/after images, rollback with conflict detection, the `/api/v1/change-sets` surface, the `list/get/rollback_change_set` MCP tools, and the web UI page. Storage is a dedicated LevelDB table (`Databases/ChangeSets`), separate from the sharded presentation Journal.

---

## Overview

Every configuration write through the alert-administration services (sensor and product data/TTL policies, alert templates, alert schedules) applies immediately and is recorded in the undo journal with structured before/after images. Clients group related writes under a client-supplied `changeSetId` (REST header `X-HSM-Change-Set`, MCP `changeSetId` argument); a write without an id forms its own single-entry change set, and every successful write echoes the effective id (REST: the response header; MCP: the `changeSetId` field of the structured content). A rollback replays compensating full-list writes in reverse journal order through the same administration services, write gates and authorization the original writes used. The decision record is ADR-0010 (undo journal, not staged transactions).

## Invariants

- **Hook point is the administration service layer** — the transport-agnostic choke point both REST and MCP delegate to. Web-editor writes bypass these services and are NOT journaled (accepted scope), but the rollback conflict check detects them by content comparison.
- **Images are entity rows, never secrets**: policy lists as `PolicyEntity` rows (ids, template linkage, intervals), templates and schedules as their entities. Snapshots of a sensor's lists are taken under the write gate — the state the write serializes against. A template fan-out is ONE entry carrying per-sensor list images (story 19); the schedule-delete detach carries per-node images plus the template rows it touched.
- **Conflict rule is content, never timestamps**: at rollback, each affected entity's current content is compared to the recorded after-image (order-insensitive by policy id, chat sets as sets). Any mismatch blocks the rollback with a per-entity diff (409 `details` keyed by path) unless `force` is set. A web-editor full-list re-assert of identical content is NOT a conflict.
- **Rollback authorization equals the original write's**: the pre-flight demands write access on every pending entry (the evaluator's per-boundary Manager checks on the token side; the cookie-`User` twin `CookieUserAccess` on the web UI). Rollback of another principal's change set is allowed with those rights (story 12). Listing and details filter to entries the caller can SEE; unknown and invisible change-set ids answer the same 404.
- **Rollback is journaled as its own change set** (before = state at rollback time, after = restored state, operation inverted) — redo is a rollback of the rollback. The result echoes `rollbackChangeSetId`.
- **Partial retries**: each entry records `RestoredBy`; a partially failed rollback marks the entries it restored, and a retry skips them instead of tripping the conflict check on restored content. A change set goes terminal (`rolledBack`) only when every entry is restored.
- **Dry-run everywhere**: `dryRun` on a regular write validates and reports the affected entities (a template fan-out names every matched sensor and every stale holder it would prune) applying nothing; `dryRun` on a rollback reports conflicts plus the restore plan. The web UI's confirmation dialog runs the rollback dry-run first.
- **Statuses**: `applied`, `partial` (entity-deletion cleanup dropped some entries/images; survivors stay rollbackable), `rolledBack` (terminal — further writes under the id are rejected).
- **Deletion cleanup mirrors the Journal lifecycle**: removing a sensor, product or folder purges its undo records (sensor/product entries and fan-out images; folder-bound template entries); a change set left entry-less is removed entirely. Cleanup failures are logged, never fatal to the deletion.
- **Durability trade-off (ADR-0010)**: journal appends run AFTER the primary write succeeds and a journal storage failure is logged loudly but does not fail a write that already applied (a client retry would duplicate the change). The ChangeSets table's writes propagate storage failures — only the hook swallows them.
- **Rollback restores configuration only** — notifications already sent, alerts already fired and sensor history are untouched.

## Surfaces

- **REST**: `GET /api/v1/change-sets` (author/from/to/status/entityId filters, limit ≤ 200 + offset), `GET /api/v1/change-sets/{id}` (per-entry before/after images as JSON rows), `POST /api/v1/change-sets/{id}/rollback?force=&dryRun=`. Every journaled write endpoint reads `X-HSM-Change-Set`/`X-HSM-Dry-Run` headers and echoes `X-HSM-Change-Set` on success (dry-runs answer 200 with `WritePreviewDto`).
- **MCP**: `list_change_sets` (paged envelope), `get_change_set`, `rollback_change_set` (force/dryRun). Every journaled write tool gains optional `changeSetId`/`dryRun` arguments; write tools return a union (the DTO echo with `changeSetId`, or the preview) and therefore carry a permissive (`true`) output schema — pinned by the registration and wire tests.
- **Web UI** (`/ChangeSets`, Alerts menu): list with status badges and touched entities, details modal with before/after images (fan-out sensors collapsed per node), admin-only rollback with the dry-run-backed confirmation dialog and conflict report.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Write with a client `changeSetId` grouping several configuration writes | API/MCP client |
| 2 | Dry-run a template update to preview the fan-out target set | API/MCP client |
| 3 | List/filter change sets; inspect before/after images | Operator (UI/API/MCP) |
| 4 | Roll back a change set (refuse on conflicts with a diff; force overrides) | Operator (admin/Manager) |
| 5 | Redo a mistaken rollback via the rollback's own change set | Operator |
| 6 | Audit what an agent/script did (author, time, status filters) | Operator/Viewer |

## Known Limitations

- Web-editor writes are not journaled (detected by the conflict check only).
- Schedule-delete rollback restores detached references via per-node full lists and the touched template rows; a schedule created by a change set is deleted with the full detach walk.
- `ListChangeSets` scans and filters in memory (no server-side paging beyond limit/offset) — bounded by the journal's size, which grows without retention by design (#1570: entity-deletion cleanup only).
- The cookie UI maps visibility through the `User` role predicates (`IChangeSetAccess` seam); the token path uses the evaluator verbatim.
