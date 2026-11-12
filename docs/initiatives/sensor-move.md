# Initiative: Move a sensor to another path

> Owner: server | Last reviewed: 2026-11-12 | Canonical: yes

## Problem

HSM cannot move a sensor. A collector that changes its layout leaves the old sensors behind with their history and starts new, empty ones; the operator's only tool is deletion, which loses the history. The concrete case: stale `garage-server/...` nodes on the owner's server, deleted by hand after a probe layout change.

## Goals

- Move a sensor to another path — within its product or into another product — keeping its history, settings, alerts/policies, TTL, journal and dashboard references.
- Do not lose the data of a collector that still sends to the old path.
- Offer the move on every management surface: Web UI, `/api/v1`, MCP, and the data collectors (.NET and native).

## Non-Goals

- Moving nodes or products (owner decision: sensors only).
- Rewriting path-based external references (Grafana queries, scripts) — they see the new path.
- An atomic multi-sensor move.

## Current Behavior

- History rows are keyed by the sensor's Guid (`DbKey`), so are journal rows (`JournalKey`); the path is stored only as `SensorEntity.ProductId` + `DisplayName` and indexed in memory by `TreeValuesCache` (`_cache[rootId].Sensors[path]`).
- A value at an unknown path creates the sensor and any missing nodes (`TryAddSensor` → `AddNonExistingProductsAndGetParentProduct`).
- The only path-changing operation is deletion (`RemoveSensorAsync`): it removes the entity and the whole history.

## Decisions (owner, 2026-11-12)

1. **Sensors only.** One sensor → a full new path (product + path, may rename). Several sensors → a destination node, names kept. Missing destination nodes are created as on ingestion; empty source nodes are left to the existing empty-node sweep. A batch is not atomic; every sensor reports its own outcome; a dry run previews.
2. **The sensor keeps its Guid** — ADR-0018. Nothing id-keyed is rewritten.
3. **Destination occupied → fail by default; `onConflict=merge` merges.** The moved sensor survives with its Guid, settings and alerts; the destination's history rows are re-keyed under that Guid and the destination is removed. Different types always fail. Same-tick collision: the moved sensor's row wins; collisions are counted in the result and logged. The last value/state after a merge is the newest of the two.
4. **The old path becomes an alias** of the sensor: values and registrations sent there land in the moved sensor. Visible in UI/API, removable by hand, swept when nothing arrived through it for longer than the sensor's SelfDestroy interval, displaced by a real sensor moved to that path.
5. **Alerts and settings behave as if the sensor had always been there.** Own alerts and explicit settings follow the sensor; "From parent" settings resolve against the new parent; alert templates are re-evaluated (non-matching detached, newly matching applied).
6. **Permissions.** Web UI: Manager of both root products or admin. `/api/v1` and MCP: a read-write token whose owner may write both. Collector key: within its own product only, `CanAddSensors` (+`CanAddNodes` when nodes are created); merge allowed.
7. **Collectors declare `PreviousPaths`** — full product-relative paths on the sensor's options, sent with registration as an optional field of `AddOrUpdateSensorRequest`. The server moves (merging if both exist) while registering. Idempotent; ignored by an old server; works on HTTP and on the WebSocket session (both use `SensorIngestionService.ApplyCommandAsync`). Native: an appended options field (MINOR).
8. **Journal and undo.** Every move journals "Move sensor old → new" with its initiator. There is no separate rollback (revised by the owner on 2026-11-12, #1723 closed as not planned): a plain move is undone by moving the sensor back — its old path is its own alias, so the move back meets no conflict and retires the alias, and it is journaled like any move. A merge is irreversible; its dry run is the preview.

## Proposed Direction

`TreeValuesCache.MoveSensorAsync` is the single core entry: it runs on the source root product's queue, takes the tree-structure lock (also taken by sensor creation), validates the destination, creates missing nodes, detaches the sensor from its old parent (product policy groups, ingestion index), renames and attaches it to the new parent (settings re-parented), re-evaluates alert templates, persists the entity with the new alias, journals, and only then publishes the sensor in the destination index and the alias in the source index. Every surface (REST, MCP, UI, collector registration) calls it; none re-implements move semantics.

## Work Breakdown

| Step | Issue | Scope |
|---|---|---|
| 1 | #1719 | This initiative + ADR-0018 + core move and aliases + xUnit tests |
| 2 | #1720 | Core merge into an occupied destination |
| 3 | #1721 | `/api/v1` endpoint + MCP `move_sensors` |
| 4 | #1722 | Web UI action in the sensor tree; aliases on the sensor page |
| 5 | ~~#1723~~ | ~~Rollback of a plain move~~ — not planned: a move back is the undo (decision 8) |
| 6 | #1724 | `PreviousPaths` in the DTO, both collectors and the conformance corpus |

## Risks

- **Concurrency across product queues.** A cross-product move touches two ingestion indexes; mitigated by the tree-structure lock and by publishing the sensor in the destination index last (ADR-0018).
- **Storage compatibility.** `SensorEntity.Aliases` is an appended optional JSON field; a downgraded server ignores it.
- **Two writers through an alias.** An un-updated collector writing the old path and an updated one writing the new path both feed the same sensor. That is the point of the alias, but it can interleave values; the alias is visible and removable.
- **Cross-product aliases let one product's key feed another product's sensor.** Only a Manager of both products can create one (the move itself), and it expires.

## Verification

- xUnit in `HSMServer.Core.Tests`: same/cross-product move, history and policies intact, settings re-parented, templates re-evaluated, conflict, aliases (redirect, cross-product queue hop, persistence across restart, sweep, displacement), journal records.
- Server tests run locally (they are not gated on PRs).
- Feature doc `aicontext/features/server/sensor-move/feature.md` updated with every step.
