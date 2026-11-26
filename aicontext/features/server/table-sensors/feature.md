# Feature: Table sensors (sensor kind and schema)

> Owner: server | Last reviewed: 2026-11-24 | Canonical: yes
> Scope: the table sensor kind in the server's sensor tree: its schema, its registration, what it refuses, and how a server that does not know it treats its entity (#1844, part 2a of epic #1842, ADR-0021 points 1 and 2). The block store and the read of rows are part 2b; the wire (registration and blocks over the session) is #1845.

---

## Overview

A **table sensor** (glossary) is a sensor whose data is rows, not values: a time and a fixed set of typed columns, sent by a collector in blocks (`aicontext/features/api/table-block/feature.md`). In the tree it is an ordinary sensor node of type `SensorType.Table` (server enum, value 11), with a **table schema** fixed when it is registered. It has no values, no statuses, no alerts and no comments, and it never moves.

The model is `TableSensorModel`, which derives from the non-generic `BaseSensorModel` directly: a table sensor has no value type and no typed policy, so it does not take a generic base with a value type that is never stored. Its value storage is `NoValuesStorage` (holds nothing), its policy collection is `TableSensorPolicyCollection` (holds no policy, refuses every policy update).

The table identifier inside a block is the sensor's path as the collector registers it, in UTF-8, resolved in the product of the access key that sent the block (decided for #1844). That is why a table sensor never moves: a stored block cannot be re-keyed.

## Invariants

- The schema is fixed at registration and never changes: registering the same path again needs the same schema (names, ordinal, and types, in order); any other is refused. A different shape is a different sensor.
- The schema id stored with the sensor is the CRC-32 of its canonical bytes (`TableSchema.Id`, the format doc's section "Table schema"). A loaded entity whose stored id is not the id of its columns is not built (see "Loading").
- `FirstSequence` is taken when the sensor is created, before the sensor can be seen: blocks stored before it belong to an earlier sensor of the same path and are never read as its rows. The source is `TreeValuesCache.TableBlockSequence`, which reads the block store's next sequence (opening the store: the registration of the first table sensor is a first use). Why this is enough when a put is in flight: `aicontext/features/storage/table-blocks/feature.md`, "A sensor created during a put".
- A table sensor never holds a value: every value path refuses (`TryAddValue`, `TryUpdateLastValue` answer false; a value addressed to it is counted in `RejectedValues` like any refused value). The members that would turn stored value bytes into a value (`Convert`, `ConvertFromJson`, `GetEmptyValue`) throw `NotSupportedException`: no value of a table sensor is ever stored, and their callers are refused first (the operator's value edit, the history clear, the sensor-move merge) or never reach them (the history reads find no rows).
- A table sensor has no policies of any kind (data or TTL), never times out (`CheckTimeout` is false), and is never self-destroyed (`ShouldDestroy` is false: its activity is its blocks, which that check cannot see).
- A table sensor's path never changes (the list below).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Register a table sensor with a schema: over the wire, `SensorType` 11 with `TableColumns` on `commands` / `addOrUpdate` / the session's `commands` op (#1845), which `SensorIngestionService` turns into `ITreeValuesCache.AddOrUpdateSensorAsync(SensorAddOrUpdateRequest { Type = SensorType.Table, TypeIsExplicit = true, Schema = ... })` | collector |
| 2 | Show a table sensor's schema on its page | UI |
| 3 | Refuse what a table sensor does not do (below) | every surface |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `SensorType.Table = 11` | `src/server/HSMCommon/SensorValues/BaseValue.cs` | The server enum. The DTO `HSMSensorDataObjects.SensorType.TableSensor = 11` maps to it (`ApiConverters.Convert`, #1845). |
| `AddOrUpdateSensorRequest.TableColumns` (`TableColumnRequest { Name, Type }`, DTO `TableColumnType` 1 time, 2 int64, 3 double) | `src/api/HSMSensorDataObjects/SensorRequests/AddOrUpdateSensor/` | The schema on the wire (#1845); `SensorIngestionService` builds the `TableSchema` from it with the configured `TableSensors` limits. The wire and its refusals: `aicontext/features/api/wire-contract/feature.md`, "Table sensor registration". |
| `SensorAddOrUpdateRequest.Schema` (`HSMTableBlocks.TableSchema`), `.Refusal` | `src/server/HSMServer.Core/Model/Requests/SensorAddOrUpdateRequest.cs` | A refused table registration is answered by `AddOrUpdateSensorAsync` as an error (`TaskResult.FromError`, `IsFault` false) with the reason in `Refusal`, unlike every other failed registration, which is answered Ok. It is never a fault: sending it again changes nothing. A store failure while the sensor is written stays a fault (#1756). |
| `TableSensorModel.Schema`, `.FirstSequence` | `src/server/HSMServer.Core/Model/Sensors/SensorModels/TableSensorModel.cs` | |
| `SensorEntity.Table` (`TableSensorEntity`: `SchemaId`, `Columns` of `{Name, Type}`, `FirstSequence`) | `src/database/HSMDatabase.AccessManager/DatabaseEntities/TreeEntity/` | Appended, nullable; JSON like the rest of the entity. Column types are the block format's numbers (1 time, 2 int64, 3 double). |
| `SensorMoveOutcome.NotSupported` ("notSupported" in the management API) | `src/server/HSMServer.Core/Model/Requests/SensorMoveRequest.cs` | The answer to any move of a table sensor. |
| `ITreeValuesCache.ClearSensorHistoryAsync` returns `TaskResult` | `src/server/HSMServer.Core/Cache/ITreeValuesCache.cs` | An error for a table sensor (and for an unknown id); was `Task`. |

### Registration rules (`TreeValuesCache.TryCheckTableRegistration`)

| Request | Path holds | Answer |
|---|---|---|
| Table with a schema | nothing (or an alias of another sensor, which a table registration displaces like any explicit type) | created with the schema; inherits no TTL policy from its parent |
| Table with a schema | a table sensor with the same schema | the registration's other fields are applied (description, ...) |
| Table with a schema | a table sensor with another schema | refused: the error names both schemas |
| Table | a sensor of another kind | refused: "cannot become a table sensor" |
| Table without a schema | anything | refused |
| Another explicit type | a table sensor | refused: "is a table sensor" |
| No explicit type | a table sensor | applied as an update; refused if it carries alerts, TTL policies or aggregation |
| Table, carrying alerts, TTL policies or aggregation | anything | refused before anything is created: "is a table sensor" |

**What the caller receives.** A refusal is a non-fault error all the way out. `SensorIngestionService.AddOrUpdateSensorAsync` turns only `IsFault` into `RegistrationFaultException` (answered 500, repeated by the collector every cycle) and returns any other error as it is. So a refused registration is answered `406` with the reason as the body on `POST /api/sensors/addOrUpdate`, an entry `{path: reason}` in a `200` on `POST /api/sensors/commands`, and an entry in the errors map of a `200` acknowledgement on the session: a per-command refusal the collector does not send again. Pinned by `RegistrationStatusTests.Registration_RefusedByATableSensor_IsARefusal_NotAFault` and `TableRegistrationWireTests`.

**What the ingestion refuses before the cache (#1845)**, with the same answers and the text `The table sensor registration is refused: …`: `TableColumns` on a registration that is not of type 11; a schema that breaks the rules of `TableSchema` or the configured column limit (the reason is the rule); a column type other than 1, 2, 3 (mapped one by one, so 257 is not read as 1); and **a path that is not in its normalized form**: a block names its table by the registered path byte for byte, so the server takes a table registration only under the form it stores (`a/b`, not `/a/b`, `a//b`, `a/ b`), and the refusal names that form; and **a path longer in UTF-8 than the configured `TableSensors.MaxTableIdLength`** (1,024 bytes by default), which no block could name. These checks live in one place (`SensorIngestionService.FindTableRefusal`), which the move pre-pass of a batch calls too, so a refused table registration moves no sensor. The limits are the configured `TableSensors` section, a required parameter of the service. A table registration may carry `KeepHistory`: it is stored as the sensor's setting (owner decision of 2026-11-24) and applied by the retention of batches in whole weeks (#1850 part 4a); the per-sensor history cleanup skips table sensors. The sensor page says so under "Keep sensor history" (`_CleanupSettings.cshtml`, for a sensor of type Table): the period is rounded up to whole weeks (10 days are kept as 2 weeks) and rows may stay up to a week longer.

### What a table sensor refuses

| Operation | Where it is refused | Answer |
|---|---|---|
| A value (collector, API, alias) | `TableSensorModel.TryAddValue` | dropped and counted in `RejectedValues`, as any refused value |
| The operator's value or status edit | `TreeValuesCache.UpdateSensorValueAsync` (and its queue handler); `HomeController.UpdateSensorStatus` | error / 400; the edit modal hides its value block |
| A data or TTL policy, aggregation of values | `TreeValuesCache.UpdateSensorAsync` and `TryUpdateSensor` (`RefuseForTableSensor`), `TableSensorPolicyCollection.TryUpdate` | error naming the sensor |
| A policy through the management API or MCP | `PolicyAdministrationService.ResolveWritableSensor` | 422 `Invalid`, key `sensor` |
| An alert template | `AlertTemplateModel.IsMatch` (no template, any-type included, matches a table sensor); the template apply's sensor scan; `AlertTemplateAdministrationService` (sensor type 11 refused); `AlertTemplatesController.AlertTemplate`; the template form's type list | no match / 400 / model error |
| The alert editor | `HomeController.AddDataPolicy`, `AddAlertCondition`, `GetOperation`; `_MetaInfo.cshtml` hides the alerts block | 400 |
| A history clear of the sensor | `TreeValuesCache.ClearSensorHistoryAsync` | error "clearing its history is not supported" |
| A history clear of a node | `ClearNodeHistoryAsync` | the table sensor is skipped |
| Per-sensor retention (KeepHistory) | `CheckSensorHistory` | skipped here: the setting is stored (a registration may carry it, #1845) and is applied by the retention of batches in whole weeks: a week's rows go with their week database once the sensor's period, rounded up to whole weeks, has passed after the week's end (#1850 part 4a; `aicontext/features/storage/table-batches/feature.md`, "Retention"; ADR-0021 point 12) |
| Self-destroy | `BaseSensorModel.ShouldDestroy` | never |

### Operations that change a table sensor's path

Every one is refused. The refusals of the sensor's own operations name the table sensor; the rename of a node above it is refused by the general rule that no node below a root is renamed (#1881), which names the node.

| Operation | Where | Answer |
|---|---|---|
| Move or rename of the sensor (same node, other node, other product) | `TreeValuesCache.MoveSensorAsync` | `NotSupported`, the error names the sensor's full path and id |
| The same as a dry run | `TreeValuesCache.PreviewMove` | `NotSupported` |
| A move onto an occupied path with merge, the table sensor moving | `MoveSensorAsync` | `NotSupported` |
| A move of another sensor onto the table sensor's path with merge | `MoveSensorAsync` (types differ) | `Conflict`, the error names the table sensor and its type |
| A collector's previous paths that name the table sensor's path (#1724) | `ApplyPreviousPathsCoreAsync` | skipped with a Warn naming the sensor (that surface never fails the registration) |
| Management API and MCP sensor moves | `SensorMoveAdministrationService` over `MoveSensorAsync` | entry outcome `notSupported` |
| Renaming a node (not a root product) above a table sensor | `TreeValuesCache.UpdateProductAsync` and the queue handler `UpdateProduct` (`RenameRefusal`) | refused like the rename of any node below a root (#1881): "Cannot rename node '<path>' to '<name>': only a root product can be renamed, …" |

Renaming a root product does not change a sensor's path (paths are relative to the root product) and is allowed. Moving a product between folders does not change paths. No operation re-parents a node.

### Removing a table sensor or its product (#1850 part 4d)

Removing a table sensor removes its entity as for any sensor (`TreeValuesCache.RemoveSensor`), and its stored rows go too, at once, as an ordinary sensor's values do (ADR-0021 point 12): no grace period, no undo. The tree records the removal in the batch store **before** the sensor leaves the tree (`ITableBatchStore.MarkRemoved`, one durable write); the merge job deletes its batches, index records and placement in the next passes, within its budget, once the sensor is gone from the tree (`aicontext/features/storage/table-batches/feature.md`, "The removal").

| Removal | What the tree records | What the merge job deletes |
|---|---|---|
| A table sensor (the tree, the API, a self-destroy sweep) | the sensor, with its root product | its keys in every week database of every class, its placement |
| A node (not a root product) | every table sensor of its subtree, in one write | as above, per sensor |
| A root product | the product and its table sensors, in one write | its blocks (never merged, or within their delay; the other products' blocks of the same hours stay), then its whole folder of batches |
| A sensor whose parent is missing at start | the sensor, its product not known | as for a sensor, in the product that holds its placement |

- A record that cannot be written is logged as an Error and the removal goes on: the sensor does not come back, and its rows stay as an orphan placement, counted (`Orphan table placements`) and named in a daily Warn, never deleted by themselves.
- The job acts on a record of this run only once the tree has confirmed that the entity is gone from the environment database (read back as absent). A crash between the record and the removal of the entity leaves a record of a sensor still in the tree after the restart: the job clears it with a Warn and deletes nothing.
- A new sensor registered on the same path has another id and never sees the old rows; its own rows and placement are not touched by the old sensor's removal, also when they were merged before it.
- A collector still sending the removed sensor's table gets it refused (`UnknownSensor`); the feed sensors of the removed sensor stay (`aicontext/features/server/table-feed-sensors/feature.md`).
- Ordinary sensors are not touched by any of this.

## Loading, and a server that does not know the type (rollback)

`SensorModelFactory` builds a `TableSensorModel` from an entity of type 11. The constructor throws `InvalidDataException` when the entity has no `Table`, its schema breaks the rules (read back with the format's maximum column count, so a sensor registered under a looser limit still loads), or its stored schema id is not the id of its columns.

The tree load (`TreeValuesCache.ApplySensors`) builds every sensor in its own try/catch: an entity the factory cannot build is logged (`Applying sensor {id} error`) and skipped, and the rest of the tree loads. Nothing writes that entity back: it is not in memory, and nothing at start deletes or rewrites an entity that did not load. An **older server** that does not know type 11 takes exactly this path (its factory throws for an unknown type), and System.Text.Json with default options (`EnvironmentDatabaseWorker`) skips the `Table` member it does not know. So a rollback neither crashes nor loses the table sensor's entity; the table sensor is simply absent until the newer server runs again.

Tested: an entity of a type this build does not know (12) with a `Table` member, a type-11 entity without a schema and one with a wrong schema id are skipped while the other sensors load, and the stored entities are byte-for-byte the same JSON afterwards; an entity JSON with a member this build does not know deserializes. Argued, not tested: that an older build's factory throws for 11 (it throws for any value outside its switch, as this build's does for 12).

What a rollback cannot protect: while the older server runs, a value sent to the table sensor's path creates an ordinary sensor there (the table sensor is not in its tree). After rolling forward both entities exist; the first one loaded keeps the path in the index (`AddSensorToCache` uses `TryAdd`). The operator removes the stray sensor.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/Model/Sensors/SensorModels/TableSensorModel.cs` | The model |
| `src/server/HSMServer.Core/Model/Storages/ValueStorages/NoValuesStorage.cs` | Storage that holds nothing |
| `src/server/HSMServer.Core/Model/Policies/PolicyCollection/TableSensorPolicyCollection.cs` | Policy collection that holds none |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.cs` | Registration rules, update/clear/retention/edit refusals, node rename guard, `TableBlockSequence` |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.SensorMove.cs` | Move, preview and previous-path refusals |
| `src/database/HSMDatabase.AccessManager/DatabaseEntities/TreeEntity/TableSensorEntity.cs` | The persisted schema |
| `src/server/HSMServer/Views/Home/Sensor/History/_SensorTableSchema.cshtml` | The sensor page body: the schema |

## Data Flow

Registration request (Type `Table`, schema) → product queue → `TryCheckTableRegistration` → `TryAddSensor` (entity with `Table`, `FirstSequence` from `TableBlockSequence`) → environment database → tree. Rows: part 2b.

## Storage / Persistence

The schema is part of the sensor's row in the environment database (`SensorEntity.Table`), so it is backed up with it. The rows themselves live in the block store (part 2b) and the batch store (#1850), which are not backed up, like sensor history; they are deleted with the sensor or its product (above). A table sensor entity that does not load is left out of the tree and not removed: its stored rows stay, reported as an orphan placement (unless an earlier run recorded its removal and stopped before its entity left the database: the user's removal is then carried out).

## UI / Operator Visibility

- The sensor page of a table sensor shows its schema (columns, types, schema id) instead of the history, graph and table tabs (`_Sensor.cshtml`, `_SensorTableSchema.cshtml`); `SensorNodeViewModel.IsChartSupported` and `IsTableFormatSupported` are false, `IsTableSensor` true; `history.js` treats type 11 like File (no history request). Under the schema the page shows the rows: chart, notes and download (`_SensorTableChart.cshtml`, #1847; `aicontext/features/server/table-sensors-ui/feature.md`, "The page").
- Hidden for a table sensor: the alerts block, the "Aggregate values" switch, the value block of the status edit.
- The alert template form does not offer the type.

## Dependencies

- `HSMServer.Core` references `HSMTableBlocks` (`TableSchema`).

## Tests

- `HSMServer.Core.Tests/TableSensors/TableMergeJobTests.Removal.cs` (#1850 part 4d): a removed sensor's keys in every class and another sensor's keys byte for byte, a crash after every write of a sensor's and of a product's removal, a record of something still in the tree, a removal during a move, a product's blocks and folder, a new sensor on the path, an orphan placement, a record that does not read, a sensor removed at start without its parent.
- `HSMServer.Core.Tests/TreeValuesCacheTests/TableSensorTests.cs`: registration (create, persist, restart; same schema; three kinds of other schema; another kind's path; no schema; another explicit type on a table path), a value addressed to a table sensor (rejected, nothing stored, value edit refused), no TTL inheritance and refused policy/TTL/aggregation updates, no template match, every move form (move, rename, other node, merge both ways, previous paths, dry run), node rename refused, history clear and retention, self-destroy, the rollback behaviour above. Every refusal is asserted to be a non-fault error; a table registration carrying alerts is refused before anything is created; a store failure during a table registration stays a fault (`A_store_fault_during_a_table_registration_is_still_a_fault`).
- `SensorApi/RegistrationStatusTests.Registration_RefusedByATableSensor_IsARefusal_NotAFault`: what the HTTP routes answer (200 with a per-path entry, 406).
- `SensorApi/TableRegistrationWireTests` (#1845): a table registration over `commands`, `addOrUpdate` and the session creates the sensor with its schema and is accepted again; every refusal of the ingestion (the rules of the schema, an unknown or out-of-byte-range type, a null column, columns on another type or without a type, the column limit, four spellings of a path not in normal form) is a per-path error in a 200 with the registration after it applied; the cache's refusals (another schema, no columns) reach the wire as refusals; `KeepHistory` is stored.
- `Model/ManagementApi/PolicyAdministrationServiceTests.SensorPolicies_OnATableSensor_AreRefused`, `Controllers/AlertTemplatesApiControllerTests.Create_TableSensorType_Is400_TableSensorsHaveNoAlerts`, `Controllers/AlertTemplatesControllerTests.TableSensorType_IsRefused_TableSensorsHaveNoAlerts`; `Mcp/AlertTemplateEnumTableDocsTests` pins that the template DTO documents type 11.
- `HSMTableBlocks.Tests/SchemaAndDirectoryTests.cs`: the schema rules and the pinned id.

## Known Issues / Limitations

- Alerts on a table feed are set on its feed sensors, ordinary sensors under `.tables` of the product: `aicontext/features/server/table-feed-sensors/feature.md` (#1847).
- Moving a table sensor would need the stored blocks to know the sensor by more than one path; if moves are wanted later, the entity can carry a history of `(path, from sequence)` and no block has to change.
- A template entity of type 11 written into the database by hand would fail to build (`Policy.BuildPolicy` has no table policy, so `AlertTemplateModel`'s constructor meets a null for each of its policies); every write path refuses the type.
