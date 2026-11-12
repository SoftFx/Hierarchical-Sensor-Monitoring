# Feature: Sensor move

> Owner: server | Last reviewed: 2026-11-12 | Canonical: yes
> Scope: moving a sensor to another path (same or another product) and the aliases its old paths leave behind.

---

## Overview

A sensor can be moved to another path — inside its root product or into another one — and keeps its id. Everything keyed by the id follows it with no migration: history, journal records, policies (own and template-derived), TTL policies, mute state, dashboard panel sources, undo references. A dashboard panel subscription (a path template) that covers the new path adds the moved sensor, as it would add a new one. Its old path becomes a **sensor alias**: values and registrations sent there still land in the moved sensor, so a collector that has not been updated loses nothing and the old node does not come back empty.

Decisions and their reasons: [ADR-0018](../../../../docs/decisions/0018-sensor-move-keeps-id.md); the owner's design decisions and the delivery plan: [initiative](../../../../docs/initiatives/sensor-move.md), epic #1718.

Delivered so far: the core move and aliases (#1719), merge into an occupied destination (#1720). The `/api/v1` and MCP surfaces (#1721), the Web UI (#1722), rollback (#1723) and collector `PreviousPaths` (#1724) follow.

## Invariants

- **Only sensors move.** Nodes and products are never moved; a move creates missing destination nodes exactly as ingestion does, and leaves empty source nodes to the empty-node sweep (`RunProductsSelfDestroyAsync` → `ClearEmptyNodesAsync`).
- **The id never changes.** A move writes one row: the sensor entity, with its new parent (`SensorEntity.ProductId`), name (`DisplayName`) and aliases. History rows (`DbKey(SensorId, ticks)`) are never touched.
- **The destination must be free, unless a merge is asked.** A real sensor at the destination path makes the move a `Conflict` and changes nothing — with `SensorMoveConflict.Merge` (and only between sensors of the same type) the moved sensor absorbs it instead (see Merge). An alias at the destination never blocks: the move displaces it.
- **Paths are root-relative** and normalized like ingestion paths (`BaseUpdateRequest`): empty segments are dropped, a leading `/` is ignored, `\` and tabs are invalid, at most 10 segments. The destination product must be a root product.
- **Settings resolve "as if the sensor had always been there".** Explicit settings and own policies travel with the sensor; settings left "From parent" (TTL, KeepHistory, SelfDestroy) resolve against the new parent chain; alert templates are re-evaluated — templates that stopped matching are detached, templates that started matching are applied, attached templates that still match are left untouched (no journal churn). The decision is made from the templates actually attached, so a lost re-evaluation is repaired by the next one. Across products the re-evaluation is dispatched to the destination's queue, so it completes shortly after `MoveSensorAsync` returns.
- **A move is serialized with every structural change.** Sensor creation, node/product removal and the move take one tree-structure lock; sensor creation re-checks the ingestion index under it. The lock covers only validation, node creation, the model and index mutation and the entity write; template re-evaluation, the journal record and UI subscribers run after it. The move runs on the queue of the sensor's current root product and publishes the sensor in the destination index only after every model mutation.
- **Follow-up work runs on the queue that owns the sensor.** Template re-evaluation after a cross-product move, a registration through a cross-product alias, alias removals and the alias pass all carry the root they were routed to; a handler that finds the sensor moved meanwhile re-dispatches to the new owner (at most 3 hops) or, for an awaited call, retries. This covers the move's own follow-ups only; other sensor-scoped requests routed before a move are a known limit below.
- **An alias never shadows a real sensor.** The alias index is consulted only when the real index misses, and an alias is dropped (at startup) or displaced (at a move) wherever a real sensor holds the path.
- **The sensor model stays single-writer.** A value arriving through an alias of another product is re-dispatched to the queue of the product that owns the sensor; a sensor that moved again while the value was in flight is followed at most 3 hops, then the value is dropped and counted (`Aliased value dropped`, rate-limited `Warn`).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Move a sensor (`ITreeValuesCache.MoveSensorAsync`) | operator through a management surface (#1721, #1722) |
| 2 | Value or registration arrives at an old path → lands in the moved sensor | collector |
| 3 | Value of another type, or a registration with an explicit other type, arrives at an old path → displaces the alias and creates a new sensor there | collector |
| 4 | Alias expires (`RunSensorsSelfDestroyAsync` → alias pass) | background worker, hourly |
| 5 | Alias removed by hand (`RemoveSensorAliasAsync`) | operator through a management surface |

### Move outcomes

`SensorMoveResult.Outcome`: `Moved`, `Unchanged` (destination equals the current path), `SensorNotFound`, `DestinationNotFound` (unknown or non-root product), `InvalidPath`, `Conflict` (a real sensor holds the destination; the error names its id), `Failed` (the sensor moved or was removed concurrently, or the entity write failed — the in-memory model is restored, a destination node created for it stays and is swept when empty). `OldPath`/`NewPath` are product-prefixed full paths.

### Merge

`MoveSensorAsync(..., SensorMoveConflict.Merge)` onto a path held by a real sensor of the same type (owner decision 3 of #1718):

- **The moved sensor survives**: its id, settings, policies, journal and dashboard references. The occupant's settings, policies, journal and dashboard panels go away with it.
- **History**: every row of the occupant is copied under the survivor's id at the same tick (`IDatabaseCore.MergeSensorValues`) — the one place a history row changes its sensor id. On a same-tick collision the survivor keeps its own row; collisions are counted in `SensorMoveResult.MergeCollisions`, logged at `Warn` and journaled.
- **Newest value**: when the survivor's history is loaded, its newest stored row (possibly a copied one) becomes its cached last value and `From` widens to the oldest row. A bar survivor keeps its cache (only `From` widens): a bar storage's add would close its in-progress bar. Status, TTL/expired state and alerts are not re-evaluated from copied rows — they are history, not deliveries; the next real value recomputes them.
- **The occupant's in-progress bar** (memory only) is stored before the copy, so it is merged too.
- **Aliases**: the occupant's path and its aliases become aliases of the survivor, so everything that reached the occupant keeps landing in the survivor.
- **Three steps, ordered so a failure never strands data.**
  1. `SensorAbsorbRequest` on the occupant's own queue (the destination's): no value of the occupant is being added meanwhile. Store its in-progress bar, copy its rows (`MergeSensorValues`), then — under the tree-structure lock — take it off the index, make its path and aliases aliases of the survivor, journal "Merge sensor" and remove the occupant. A failure before the index flip leaves the occupant where it was (copied rows only fill ticks the survivor did not have, and a re-run is idempotent apart from counting them as collisions).
  2. The plain move, on the survivor's queue: the destination is now only the survivor's own alias, which the move retires.
  3. `SensorMergeRefreshRequest` on the queue that owns the survivor afterwards: refresh its cache from the merged rows.
  Steps run back to back and are not cancellable once step 1 started. Values queued behind step 1 for the occupant's path resolve through the new alias; a value already forwarded to the occupant by id from another product's alias follows the merge to the survivor (the cache remembers absorbed id → survivor id).
- **Copy failures are not silent.** The copy uses a write that throws (`PutSensorValueOrThrow`); a failed write aborts step 1 before the occupant is touched, so its rows are never deleted uncopied.
- **Races with the caller's look.** An occupant that appears after the caller checked turns a would-be `Conflict` into a second pass that merges it; one that disappears before step 1 turns the merge into a plain move.
- **Adopted aliases are persisted by the survivor's own queue**: by the move's entity write, or — if the move fails after step 1 — by a forced alias-persist request on the survivor's queue. They then expire by the survivor's SelfDestroy interval, measured from their own last hit.
- **Outcome**: `Merged` with `MergedSensorId`, `MergedValues`, `MergeCollisions`. Different types → `Conflict` (checked before step 1 and again inside it). A free destination under `Merge` is a plain `Moved`. If the move fails after step 1, the result is `Failed` with the merge counts set: the occupant is already merged and its path already routes to the survivor; retry the move.
- **Irreversible**: a merge cannot be rolled back (#1723 covers plain moves only).

### Alias lifetime

- Created by every move (old root product + old root-relative path + creation time). Moving back onto an own alias retires that alias.
- **Hit**: every value or registration through the alias stamps its last-hit time (lock-free). Hits are persisted at most hourly, by the alias pass; a hit counts as persisted only once the entity write returned, so a failed write is retried by the next pass.
- **Expiry**: the alias pass removes an alias when nothing came through it for longer than the sensor's effective SelfDestroy interval (measured from the newest of creation and last hit). A sensor whose SelfDestroy is `Never`/`None` keeps its aliases until they are removed by hand. An alias whose product was deleted is removed by the same pass.
- **Displacement**: a sensor moved onto the path, a value of another type, or a registration with an explicit other type. A registration without a type (the core defaults it to Boolean) never displaces.
- **Removal with the sensor**: removing a sensor removes its aliases from the index.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `MoveSensorAsync`, `RemoveSensorAliasAsync`, `SensorMovedEvent` | `src/server/HSMServer.Core/Cache/ITreeValuesCache.cs` | Core entry used by every surface; no surface re-implements move semantics. |
| `SensorMoveOutcome`, `SensorMoveResult`, `SensorMoveConflict` | `src/server/HSMServer.Core/Model/Requests/SensorMoveRequest.cs` | |
| `IDatabaseCore.MergeSensorValues` | `src/database/HSMDatabase/DatabaseWorkCore/DatabaseCore.cs` | Copies rows under another id; target rows win. |
| `SensorAddOrUpdateRequest.TypeIsExplicit` | `src/server/HSMServer.Core/Model/Requests/SensorAddOrUpdateRequest.cs` | Set by `SensorIngestionService` and the self-monitoring `DataCollectorWrapper` when the registration carries a type. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/Cache/TreeValuesCache.SensorMove.cs` | Move, alias resolution, alias pass, startup indexing |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.cs` | `CachedValue.Aliases`; alias fallback in `TryAddNewSensorValue` / `TryAddOrUpdateSensor`; `TryAddSensor` under the tree-structure lock |
| `src/server/HSMServer.Core/Model/Sensors/SensorAlias.cs` | Alias model (hit stamping, persistence throttle) |
| `src/server/HSMServer.Core/Model/BaseNodeModel.cs` | `MoveTo` — re-parent and rename without rebuilding policies |
| `src/server/HSMServer.Core/Model/ProductModel.cs` | `DetachMovedSensor` / `AttachMovedSensor` — product policy groups |
| `src/server/HSMServer/Model/TreeViewModels/TreeViewModel.cs` | Relocates the sensor's view model on `SensorMovedEvent` |
| `src/server/HSMServer/Dashboards/Dashboard.cs` | Offers a moved sensor to the panel subscriptions on `SensorMovedEvent` |

## Storage / Persistence

- `SensorEntity.Aliases` (`List<SensorAliasEntity>`: root product id, root-relative path, creation ticks, last-hit ticks) — an appended optional JSON field; null when the sensor has none. An older server ignores it.
- No key format changes. History and journal are untouched by a move.

## UI / Operator Visibility

- Journal: a `treeCrud` record "Move sensor" on the sensor (`OldValue` → `NewValue` full paths, initiator); "Remove sensor alias" when an alias is removed by hand or displaced.
- Log: `Sensor moved: ...` (Info); alias expiry / startup drop (Info / Warn); dropped aliased values (rate-limited Warn).
- The tree view moves the same sensor node under its new parent. The UI action and the alias list on the sensor page are #1722.

## Known limits

- Requests already queued on the old root product's queue when a cross-product move runs (a settings update from the UI, which routes by the sensor's root at enqueue time) still execute there once; the window is one queue drain. Between re-parenting and publishing (one entity write) the sensor's root already reads as the destination, so a request routed in that instant lands on the destination's queue while the source queue finishes the move.
- Other sensor-scoped requests (a UI settings or policy edit, a template fan-out item, a history clear) are routed by the sensor's root when enqueued and carry no owner check; one queued on the old root before a cross-product move runs there once after it. A dispatcher-level owner check for every sensor-scoped request would close this; it is outside #1718.
- Removing a node or product holds the tree-structure lock for the whole removal (its sensors' history wipes included), so sensor creation and moves on every product wait for a large product's deletion. Value ingestion into existing sensors is not affected.
- Template re-evaluation after a cross-product move runs on the destination's queue right after the move; values already queued there ahead of it are evaluated with the sensor's previous template policies.
- A destination path may name an existing node: the sensor then sits next to a node of the same name, exactly as ingestion would create it.
- A cross-product alias lets the old product's key keep feeding the moved sensor for as long as values arrive (an alias expires only after a SelfDestroy interval without hits). Only a Manager of both products can create one.
- Merge step 1 runs on the occupant's product queue: copying a very large history holds every value of that product until the copy ends. Merges are rare operator actions; a background copy with a final delta pass would lift this.
- A crash between merge steps 1 and 2 keeps the copied rows and removes the occupant, but the survivor's new aliases were not yet persisted: after a restart the survivor is still at its old path and the occupant's path is free (a collector re-creates it). Moving again completes it. A crash inside step 1's copy leaves the occupant intact and some rows already copied.
- Path-based external references (Grafana queries, scripts using the Sensor API by path) see the new path; the alias keeps only ingestion and registration working on the old one.
- A same-product alias resolves on the product's own queue; a cross-product one costs one extra queue hop per value.
- A dashboard panel source that a subscription added stays on the panel when the sensor moves out of the subscription's template: a panel does not record which subscription added a source.

## Dependencies

- Depends on: `TreeValuesCache` ingestion index and update queues; alert templates; journal.
- Used by: management API / MCP (#1721), Web UI (#1722), collector registration `PreviousPaths` (#1724).

## Tests

`src/tests/HSMServer.Core.Tests/TreeValuesCacheTests/SensorMoveTests.cs` — same/cross-product move, history and id preserved, created nodes, conflict, invalid/unknown inputs, unchanged move, "From parent" re-resolution, template re-evaluation, journal record; aliases: same/cross-product redirect, type displacement, registration through an alias (same and cross product), restart, SelfDestroy expiry, move-back retirement, displacement by a move, removal with the sensor and by hand.
`SensorMergeTests` — history takeover and occupant removal, newest value, same-tick collision, type conflict, cross-product merge with alias adoption, merge into a free path. `HSMDatabase.LevelDB.Tests/SensorValuesDBTests/SensorValuesMergeTests` — the copy primitive and `GetLatestValue` for the sensor whose id sorts last in a partition (a pre-existing lookup bug the merge exposed: the newest row of that sensor read as missing).
