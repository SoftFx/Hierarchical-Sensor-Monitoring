# Feature: Sensor move

> Owner: server | Last reviewed: 2026-11-12 | Canonical: yes
> Scope: moving a sensor to another path (same or another product) and the aliases its old paths leave behind.

---

## Overview

A sensor can be moved to another path — inside its root product or into another one — and keeps its id. Everything keyed by the id follows it with no migration: history, journal records, policies (own and template-derived), TTL policies, mute state, dashboard panel sources, undo references. A dashboard panel subscription (a path template) that covers the new path adds the moved sensor, as it would add a new one. Its old path becomes a **sensor alias**: values and registrations sent there still land in the moved sensor, so a collector that has not been updated loses nothing and the old node does not come back empty.

Decisions and their reasons: [ADR-0018](../../../../docs/decisions/0018-sensor-move-keeps-id.md); the owner's design decisions and the delivery plan: [initiative](../../../../docs/initiatives/sensor-move.md), epic #1718.

Delivered so far: the core move and aliases (#1719), merge into an occupied destination (#1720), the `/api/v1` and MCP surfaces with a dry-run preview (#1721), the Web UI (#1722), collector `PreviousPaths` (#1724). There is no separate rollback: a plain move is undone by moving the sensor back (its old path is its own alias, so the move back meets no conflict and retires the alias); #1723 was closed as not planned.

## Invariants

- **Only sensors move.** Nodes and products are never moved; a move creates missing destination nodes exactly as ingestion does, and leaves empty source nodes to the empty-node sweep (`RunProductsSelfDestroyAsync` → `ClearEmptyNodesAsync`).
- **The id never changes.** A move writes one row: the sensor entity, with its new parent (`SensorEntity.ProductId`), name (`DisplayName`) and aliases. History rows (`DbKey(SensorId, ticks)`) are never touched.
- **The destination must be free, unless a merge is asked.** A real sensor at the destination path makes the move a `Conflict` and changes nothing — with `SensorMoveConflict.Merge` (and only between sensors of the same type) the moved sensor absorbs it instead (see Merge). An alias at the destination never blocks: the move displaces it.
- **Paths are root-relative** and normalized like ingestion paths (`BaseUpdateRequest`): empty segments are dropped, a leading `/` is ignored, `\` and tabs are invalid, at most 10 segments. The destination product must be a root product.
- **Settings resolve "as if the sensor had always been there".** Explicit settings and own policies travel with the sensor; settings left "From parent" (TTL, KeepHistory, SelfDestroy) resolve against the new parent chain; alert templates are re-evaluated — templates that stopped matching are detached, templates that started matching are applied, attached templates that still match are left untouched (no journal churn). The decision is made from the templates actually attached, so a lost re-evaluation is repaired by the next one. Across products the re-evaluation is dispatched to the destination's queue, so it completes shortly after `MoveSensorAsync` returns.
- **A move is serialized with every structural change.** Sensor creation, node/product removal and the move take one tree-structure lock; sensor creation re-checks the ingestion index under it. The lock covers only validation, node creation, the model and index mutation and the entity write; template re-evaluation, the journal record and UI subscribers run after it. The move runs on the queue of the sensor's current root product and publishes the sensor in the destination index only after every model mutation.
- **Follow-up work runs on the queue that owns the sensor.** Template re-evaluation after a cross-product move, a registration through a cross-product alias, alias removals and the alias pass all carry the root they were routed to; a handler that finds the sensor moved meanwhile re-dispatches to the new owner (at most 3 hops) or, for an awaited call, retries. This covers the move's own follow-ups only; other sensor-scoped requests routed before a move are a known limit below. A registration through a cross-product alias is the one of them whose sender waits for the result (#1756): the handler that forwards it returns at once (no handler waits for another queue), and the collector is answered when the registration has ended on the owning queue, with a fault (`500`) if that queue failed or the sensor's row could not be written, as a registration applied directly is (`aicontext/features/api/wire-contract/feature.md` § Statuses of the registration routes).
- **An alias never shadows a real sensor.** The alias index is consulted only when the real index misses, and an alias is dropped (at startup) or displaced (at a move) wherever a real sensor holds the path.
- **The sensor model stays single-writer.** A value arriving through an alias of another product is re-dispatched to the queue of the product that owns the sensor; a sensor that moved again while the value was in flight is followed at most 3 hops, then the value is dropped and counted (`Aliased value dropped`, rate-limited `Warn`).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Move a sensor (`ITreeValuesCache.MoveSensorAsync`) — `POST /api/v1/sensors/move`, MCP `move_sensors` (#1721), the tree's "Move sensor(s)..." (#1722) | operator / AI agent |
| 1a | Predict a move (`ITreeValuesCache.PreviewMove`): outcome, nodes to create, alert templates detached/applied, inherited settings that change — the dry run of both surfaces | operator / AI agent |
| 2 | Value or registration arrives at an old path → lands in the moved sensor | collector |
| 3 | Value of another type, or a registration with an explicit other type, arrives at an old path → displaces the alias and creates a new sensor there | collector |
| 3a | Registration declaring `PreviousPaths` → the old sensor moves (or merges) to the registration path first (`ApplyPreviousPathsAsync`, #1724) | collector |
| 4 | Alias expires (`RunSensorsSelfDestroyAsync` → alias pass) | background worker, hourly |
| 5 | Alias removed by hand (`RemoveSensorAliasAsync`) — `DELETE /api/v1/sensors/{id}/aliases`, MCP `remove_sensor_alias`; aliases are listed in `SensorDto.aliases` | operator / AI agent |

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
  1. `SensorAbsorbRequest` on the occupant's own queue (the destination's): no value of the occupant is being added meanwhile. Store its in-progress bar, copy its rows (`MergeSensorValues`), then — under the tree-structure lock — take it off the index, make its path and aliases aliases of the survivor, journal "Merge sensor" and remove the occupant. A failure before the index flip leaves the occupant where it was (copied rows only fill ticks the survivor did not have, and a re-run is idempotent apart from counting them as collisions). If removing the occupant's entity from the database fails after the flip, the merge still reports `Merged`, with an error saying the occupant comes back at that path after a restart and must then be removed.
  2. The plain move, on the survivor's queue: the destination is now only the survivor's own alias, which the move retires.
  3. `SensorMergeRefreshRequest` on the queue that owns the survivor afterwards: refresh its cache from the merged rows.
  Step 1 adds the occupant's path and adopted aliases to the survivor's alias list from the occupant's queue, under the tree-structure lock — the one place an alias list changes off the owning queue. Each change is atomic under the list's lock, and the step-2 entity write (or the forced persist after a failed step 2) persists them.
  Steps run back to back and are not cancellable once step 1 started. Values queued behind step 1 for the occupant's path resolve through the new alias; a value already forwarded to the occupant by id from another product's alias follows the merge to the survivor (the cache remembers absorbed id → survivor id).
- **Copy failures are not silent.** The copy uses a write that throws (`PutSensorValueOrThrow`); a failed write aborts step 1 before the occupant is touched, so its rows are never deleted uncopied.
- **Races with the caller's look.** An occupant that appears after the caller checked turns a would-be `Conflict` into a second pass that merges it; one that disappears before step 1 turns the merge into a plain move.
- **Adopted aliases are persisted by the survivor's own queue**: by the move's entity write, or — if the move fails after step 1 — by a forced alias-persist request on the survivor's queue. They then expire by the survivor's SelfDestroy interval, measured from their own last hit.
- **Outcome**: `Merged` with `MergedSensorId`, `MergedValues`, `MergeCollisions`. Different types → `Conflict` (checked before step 1 and again inside it). A free destination under `Merge` is a plain `Moved`. If the move fails after step 1, the result is `Failed` with the merge counts set: the occupant is already merged and its path already routes to the survivor; retry the move.
- **Irreversible**: a merge cannot be undone — the absorbed sensor and its settings are gone. Preview it with the dry run.

### Collectors: `PreviousPaths` (#1724)

A collector that changes its layout declares, per sensor, the paths the sensor was registered under before; the server moves the history there on registration (owner decision 7):

- **Collector surface.** Managed (`HSMDataCollector` 3.9.0): `SensorOptions.PreviousPaths` (`List<string>`, also merged from custom options onto default-sensor prototypes). A helper that builds several sensors from one options object refuses options carrying previous paths (`ArgumentException`): the list is one sensor's. Native (0.13.0): `hsm_sensor_set_previous_paths(sensor, paths, count)` (C ABI, a new function — the options structs are unchanged) and `Sensor::SetPreviousPaths(std::vector<std::string>)` in the C++ wrapper; before Start the paths ride the Start registration, while running the sensor is re-registered. Entries are FULL product-relative paths as the tree shows them — no computer/module prefix is added, so a renamed computer or module node can be expressed. Blank entries are dropped.
- **Wire.** `AddOrUpdateSensorRequest.PreviousPaths`, omitted when unused (see `features/api/wire-contract`). It travels on `/api/sensors/commands` and on the collector session alike: both feed `SensorIngestionService.AddOrUpdateSensorAsync`.
- **Server.** Before the registration is applied, `ITreeValuesCache.ApplyPreviousPathsAsync` (outside the product queue) moves each previous path's sensor to the registration path with `SensorMoveConflict.Merge` — the key's own product only. The list is walked from the LAST entry so the FIRST listed path that still has a sensor merges last and survives (its settings and alerts win); a sensor the collector's value already created at the new path is absorbed the same way, so the order of value vs registration does not matter. A previous path that is no longer a sensor (already moved — it is an alias now — or never existed) is skipped, which makes re-registering on every start a no-op; only when something is left to move does the key need the path's write check and `CanAddSensors`, so a key without that flag keeps re-registering its existing sensors. Only sensors of one type move: the registration's explicit type, else the type of the sensor already at the target, else the first listed one's; others are skipped and logged, and so is a **blocked** sensor at an old path (it would win the merge and turn the live sensor at the new path into a blocked one). Only the first 32 non-blank entries are looked at (each costs a path check and a lookup before any permission check); the rest are logged and ignored. Nothing fails the registration — every refusal, and an exception from the move pipeline, is logged and the registration is applied as usual. The other direction is not tied either: the registration is converted before anything moves (a malformed one moves nothing), but a move or merge already made stands if the product queue then refuses the registration's settings. The blocked check on the old sensor is read before the move is queued; an operator blocking it in the moment between is not caught (the window is one queue hop). The server's own collector (`DataCollectorWrapper`) applies `PreviousPaths` the same way, with its self-monitoring key. Moves and merges are journaled with the collector key as initiator.
- **Blank entries** are one rule in both collectors: null, or ASCII whitespace only (space, `\t`, `\n`, `\v`, `\f`, `\r`) — so both send the same bytes. The server further normalizes each entry like an ingestion path (segments trimmed, a leading `/` ignored).
- **The collector's declaration wins over a manual move back.** A collector that keeps listing `A` in `PreviousPaths` moves the sensor from `A` to its new path again on its next registration, also after an operator moved it back to `A` by hand; to keep a sensor at an old path, drop the entry from the collector. A key moves (and can merge) any sensor of its own product it lists, so a typo that names another host's sensor of the same type moves that sensor — the journal records every such move with the key as initiator.
- **Conformance.** `previous_paths_contract.hsmtest` (verb `create_int_sensor_with_previous_paths`, both drivers); wire bytes pinned by `WireFormatGoldenLockTests.Registration_with_previous_paths_*` and the native `native_wire_registration_with_previous_paths_matches_net_byte_layout`.

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
- The tree view moves the same sensor node under its new parent.
- **Web UI (#1722).** The sensor tree's context menu offers "Move sensor..." on one sensor and "Move sensors..." when every selected item is a sensor (nodes are never offered). The modal (`Views/Tree/_MoveSensorsModal.cshtml`, `wwwroot/src/js/sensorMove.js`) takes the destination root product (those the user manages), the new path of one sensor (prefilled with its current path, the last segment renames it) or the destination node of several (prefilled with the first sensor's parent; empty = directly under the product), and a "merge into an existing sensor" checkbox marked irreversible (a confirm dialog guards it again). **Preview** runs the dry run and lists per sensor the outcome, the nodes it creates, alert templates removed/added, inherited settings that change, a sensor it would absorb or whose alias it takes over; **Move** applies it and toasts the per-sensor failures. Server-side, `HomeController.MoveSensors` / `RemoveSensorAlias` (POST, antiforgery-validated) call the same `SensorMoveAdministrationService` as REST/MCP through `CookieSensorMoveAccess`: Manager of the sensor's root product and of the destination's, or admin — the `RemoveNode` rule (#1512) on both ends; out of sight answers not-found, in sight without the role forbidden.
- **The sensor page** lists the sensor's aliases ("Old paths", `Views/Home/Sensor/_SensorAliases.cshtml`): product/path and the last value that arrived through each, with a **Remove** button for a user who manages both the alias's product and the sensor's.

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
