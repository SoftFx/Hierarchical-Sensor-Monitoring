# ADR-0018: A moved sensor keeps its id; its old path becomes an alias

**Status:** Accepted
**Date:** 2026-11-12
**Supersedes:** —

---

## Context

HSM has no way to move a sensor. When a collector changes its layout, the old sensors stay behind with their history and the new ones start empty; the operator's only tool is to delete the old ones and lose the history (#1718).

How the server stores a sensor decides what a move costs:

- A sensor's history is keyed by `DbKey(SensorId, Timestamp)` — the sensor's Guid, never its path. So are its journal records (`JournalKey(Id, Time, Type)`), its snapshot state, its dashboard panel references, its policies (by policy id, owned by the sensor) and its undo change-set references.
- The path exists in exactly two places: the persisted `SensorEntity.ProductId` (the parent node) plus `DisplayName`, and the in-memory ingestion index `_cache[rootProductId].Sensors[path]` that maps an incoming value's product-relative path to the sensor model.
- Storage key formats are compatibility-frozen and a high-risk area (`CLAUDE.md` → Compatibility Rules).

A collector that is not updated keeps sending to the old path. Today a value at an unknown path creates a new sensor there, so the old node would come back empty.

## Decision

1. **A move keeps the sensor's Guid.** It rewrites `SensorEntity.ProductId` and `DisplayName` (one entity write) and re-indexes the in-memory model. No history row, journal row or other id-keyed record is touched. Nodes are never moved — only sensors.
2. **The old path becomes an alias** to the sensor's Guid, persisted as an appended, optional field of the sensor's own entity (`SensorEntity.Aliases`: root product id, product-relative path, creation time, last hit time). An alias lives in a separate in-memory index next to the real one (`CachedValue.Aliases`); values and registrations that miss the real index and hit an alias land in the moved sensor. A cross-product alias hit is re-dispatched to the queue of the product that owns the sensor, so the sensor model keeps a single writer.
3. **An alias expires by the sensor's SelfDestroy interval** measured from its last hit (or creation), swept with the sensor self-destroy sweep; it can be removed by hand and is displaced by a real sensor moved to its path. A sensor `Never` self-destroying keeps its aliases until removed by hand.
4. **A destination occupied by a real sensor is a conflict.** A merge mode (step 2 of #1718) re-keys the *destination's* rows under the moved sensor's Guid — the one place where history is re-keyed, and only for the sensor being absorbed.
5. **Tree-structure changes are serialized by one lock.** A move runs on the source root product's queue. Creating a sensor or node (the ingestion path), removing a node or product, and moving a sensor all take a single tree-structure lock, and sensor creation re-checks the index under it, so a concurrent value at the destination path cannot create a duplicate and a destination node cannot vanish while a cross-product move is inserting there. The lock covers only the structural mutation and the entity write; template re-evaluation, journaling and UI notification run after it, on the queue that owns the sensor.

## Consequences

- A move is cheap and constant-cost regardless of history size, and is crash-safe at the storage level: the only persisted change is one sensor entity row.
- Everything keyed by the Guid (history, journal, dashboards, policies, snapshot, undo) follows the sensor with no migration.
- `SensorEntity` gains an optional JSON field. An older server reading a newer database ignores it (the aliases stop working, nothing else changes); a newer server reading an older database sees no aliases.
- Path-based external references (Grafana queries, scripts using the Sensor API by path) see the new path; the alias keeps only *ingestion and registration* working on the old path.
- Alias hits cost one extra dictionary lookup only on a miss of the real index, and a cross-product hit costs one extra queue hop.
- Sensor creation, previously lock-free across products, now takes the tree-structure lock — creation is rare (first value of a new path), so the contention is negligible.
- Requests already queued on the old root's queue when a cross-product move runs still execute there once; this window is one queue drain long and is documented as a known limit.

## Alternatives Considered

- **Re-key the history under a new Guid at the new path:** cost proportional to history size, a crash mid-copy leaves two half-histories, and every id-keyed reference (dashboards, journal, undo, snapshot) would need a migration. Rejected.
- **Keep a path → id indirection table instead of aliases on the entity:** a second persisted structure with its own consistency problems; aliases belong to exactly one sensor and die with it, so the sensor entity is their natural owner.
- **Tombstone the old path (reject values there):** loses the data of a collector that has not been updated yet. The owner chose redirect (#1718, 2026-11-12).
- **A rendezvous of both product queues for a cross-product move:** blocks one queue reader on another and risks deadlock with any handler that writes to another queue; the tree-structure lock serializes only the rare structural changes.
- **Moving whole nodes:** rejected by the owner; sensors only.

## References

- Epic #1718, step #1719; initiative `docs/initiatives/sensor-move.md`
- ADR-0013 (unified journal) — a move is journaled but has no rollback entry: the undo of a plain move is a move back (#1723 closed as not planned)
