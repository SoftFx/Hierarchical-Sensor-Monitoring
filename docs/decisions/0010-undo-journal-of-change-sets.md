# ADR-0010: Undo journal of change sets, not staged transactions

**Status:** Accepted
**Date:** 2026-10-19
**Supersedes:** —

---

## Context

REST management-API and MCP writes (#1500 surface family) apply immediately and
irrevocably. A wrong batch — worst case an alert-template update that fans policies
out onto hundreds of sensors — has no unit-level undo; remediation is manual surgery
that races concurrent editors. The people who notice the damage are often not the
principal that caused it (the agent's token may already be revoked) (#1570).

Two shapes could fix this: staged transactions (begin/commit with invisible
intermediate states) or an undo journal (writes apply immediately, each recorded
with before/after images; a rollback replays compensating writes).

## Decision

The undo journal. Every write through the alert-administration services (sensor and
product data/TTL policies, alert templates, alert schedules) is journaled as a
change-set entry carrying structured before/after images. Clients group writes under
a client-supplied `changeSetId` (REST header / MCP argument); ungrouped writes form
their own single-entry change set. A rollback replays compensating full-list writes
in reverse chronological order through the same administration services, write gates
and authorization the original writes used. Conflict detection is CONTENT
comparison of current state vs the recorded after-image — never timestamps, because
the web editor re-asserts full lists on every form save. A rollback is itself
journaled, which makes redo free. Web-editor writes are not journaled (out of
scope) but are detected by the conflict check.

Storage is a dedicated LevelDB table (`Databases/ChangeSets`), separate from the
sharded presentation Journal, durable across restarts, no time-based retention;
records are removed when their product/folder is deleted and the change set then
reports `partial` status.

Journaling is best-effort AFTER the primary write succeeds: a journal storage
failure is logged loudly but does not fail a write that already applied (failing
it would invite a client retry that duplicates the change). This trades a rare,
logged loss of an undo point for never reporting failure on an applied write.

## Consequences

- Writes remain immediate; no begin/end sessions, no shadow state, no new core
  serialization points beyond the existing per-node write gates.
- One hook layer covers REST and MCP because both transports already delegate to
  the administration services.
- Template fan-out is one entry carrying per-sensor before-images, captured in the
  administration service from the same wildcard/stale-sensor enumeration the cache
  applies (races against concurrent writers are caught by the rollback conflict
  check, mirroring web-editor last-write-wins semantics).
- Rollback restores configuration only: notifications already sent, alerts already
  fired, and sensor history are untouched by design.
- The change-set table grows without retention by design (#1570: entity-deletion
  cleanup only); list reads scan and filter in memory.

## Alternatives Considered

- Staged transactions with begin/commit: rejected — invisible intermediate states
  add core serialization points and a new client contract for marginal benefit on
  config edits.
- Timestamp-based conflict detection: rejected — false positives from web-editor
  full-list re-asserts.
- Journaling inside `TreeValuesCache` (cache layer): rejected — the transport-
  agnostic choke point with authorization context is the administration service
  layer; the cache sees only per-node mutations without the write's intent or
  principal.

## References

- #1570 (spec), ADR-0002 (append-only operation catalog — the token-side precedent
  for a dedicated LevelDB region)
- `aicontext/features/server/change-sets/feature.md`
