# Feature: Periods inherited from a folder

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: how a root product takes "Keep sensor history", "Remove sensor after inactivity" and the expected update interval (TTL) from its folder, what is stored for it, and what the hourly cleanup does with it.

---

## Overview

A node has three period settings: `KeepHistory` ("Keep sensor history"), `SelfDestroy` ("Remove sensor after inactivity") and `TTL`. A sensor or a node left on "From parent" resolves the setting through its parents up to the root product (`TimeIntervalSettingProperty.Value`). A root product in a folder can itself be on "From parent": it then takes the folder's period.

Folders are not part of the core tree. They live in the web layer (`FolderManager`, `FolderModel`) and the core (`TreeValuesCache`) never asks them anything. So a product that inherits from its folder holds a **copy** of the folder's period: `TimeIntervalModel` with `Interval = FromFolder` and the period in `Ticks`. The core judges that copy by its ticks alone (`UseTicks`).

A period is one of:

| Kind | Core model | Judged by |
|---|---|---|
| A fixed length: one minute to a week, or a custom span | `Interval = Ticks`, `Ticks` = the length | ticks |
| A calendar preset: a month, three months, six months, a year | `Interval = Month \| ThreeMonths \| SixMonths \| Year`, `Ticks = 0` | the calendar (`AddMonths`, `AddYears`) |
| Never / forever | `Interval = None`, `Ticks = long.MaxValue` | never up |
| The folder's period, copied | `Interval = FromFolder`, `Ticks` = the length | ticks |

## Invariants

- **A copy carries the folder's period as ticks, whatever the folder's choice is.** A calendar preset has no ticks of its own, so it is copied at its fixed length, the value of the enum member: 31 days a month, 93 days three months, 186 days six months, 365 days a year (`TimeIntervalModel.ToFromFolderModel`). An inherited month is therefore 31 days, while a month set on the product itself is a calendar month. Until #1864 the copy took the preset's zero ticks and read as "never": history was kept for ever and idle sensors were never removed.
- **Both writers of the copy store the same thing.** A save of the folder's settings (`FolderManager.TryUpdate` -> `GetCoreUpdate` -> `ToFromFolderModel`) and a save of the product page with "From parent" (`TimeIntervalViewModel.ToModel`, which takes the folder control's `CustomSpan`) both produce `FromFolder` + the preset's length. The folder form posts all three periods on every save, so every save of a folder rewrites the copies of all its products.
- **"Is the time up" is one question.** The history pass, the sensor sweep, the empty-node sweep and the TTL check all ask `TimeIntervalModel.TimeIsUp`. For a copy it is `Ticks > 0 && now - time > Ticks`, so a copy with 0 ticks and a copy with `long.MaxValue` ticks are both never up. Nothing shifts a date by a copy's ticks without that guard: `long.MaxValue` overflows `DateTime` and 0 lands on the date itself.
- **A folder's "Never" has two stored shapes**, both never up: `FromFolder` + `long.MaxValue` when the folder was saved last, `FromFolder` + 0 when the product page was.
- **No new stored shape.** `FromFolder` + ticks is what a week or a custom period always inherited as. The entity (`TimeIntervalEntity`: `Interval`, `Ticks`) is unchanged, and a server from before #1864 reads a re-derived copy as that many ticks.
- **The copy is the product's, not a live link.** A product moved out of a folder takes the folder's period as its own (`ActionType.Delete` in `GetCoreUpdate`); a product moved into a folder keeps its own periods and inherits only what it is later set to inherit.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Save a folder's settings: every product of the folder that inherits a period gets a fresh copy | operator |
| 2 | Save a product with "From parent" under a folder: the product gets a copy of the folder's period | operator |
| 3 | Start: copies of a calendar preset that do not carry its length are re-derived | server |
| 4 | Hourly cleanup: clear history older than the period, remove sensors idle for longer, remove empty nodes older than it | background worker |

### 3. Re-deriving at start

Products that inherited a calendar preset on a server from before #1864 hold `FromFolder` with 0 ticks in their stored entities, and only another save of the folder would correct them. `FolderManager.Initialize` ends with `ReapplyFolderCalendarPresets`: for every root product in a folder, for each of the three settings, if the product's own value is `FromFolder`, the folder's is a calendar preset and the copy's ticks are not the preset's length, the product is updated with the copy a save of the folder would give it.

- It is the same update as workflow 1, so the same journal record is written per setting ("Settings update", old and new value, initiator `ServerMigration`) and the same entity shape is stored.
- It is forced past the change table: the setting was last changed by a user, and the user's choice ("from folder") is what the update keeps.
- It touches nothing else: a product's own period, a copy of a folder period that is not a calendar preset (a folder's "Never" with 0 ticks included), a copy that is already right. A second start finds nothing to do.
- Each re-derived setting is logged at `Warn` ("Folder period re-derived (#1864): product ..., folder ..., '<setting>': FromFolder(00:00:00) -> FromFolder(31.00:00:00)"), followed by one `Warn` with the totals and what the next cleanup run will do.

The alternative, resolving the folder's present period whenever a policy is evaluated, was not taken: the core would have to call the web layer from the value hot path (the retention floor of out-of-order values reads `KeepHistory` for every late value), and the copy would stay wrong for any reader of the entity.

### 4. Hourly cleanup

`ClearDatabaseService` runs on the hour grid (first run at the next full hour after the start), in this order:

1. `CheckSensorsHistoryAsync`: for every sensor whose oldest stored value is older than its `KeepHistory`, clears history up to `now - period`. Logged per sensor: `Clearing history [id][path] <period> : from <oldest> up to <cutoff>`.
2. `RunSensorsSelfDestroyAsync`: removes a sensor whose last activity is older than its `SelfDestroy`. Logged per sensor: `Sensor removed: Id = ..., Path = ..., LastUpdate = ..., Settings = <period>, Initiator = ...` (the initiator is the system, "Clean up").
3. `RunProductsSelfDestroyAsync`: removes a non-root node that is empty and was created longer ago than its `SelfDestroy`.

A period that starts to apply (a re-derived copy, or a folder saved after the upgrade) is applied by the next run in full: everything older than the period goes in one pass. The `Warn` lines of the start come before that run.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `TimeIntervalEntity` (`Interval`, `Ticks`) | `src/database/HSMDatabase.AccessManager/DatabaseEntities/TimeIntervalEntity.cs` | Stored in product, sensor and folder entities. Unchanged by #1864. |
| `TimeInterval` (core enum, sparse `long`) | `src/server/HSMServer.Core/Model/TimeIntervalModel.cs` | `FromFolder = -100`; the calendar presets are their fixed length in ticks. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/Model/TimeIntervalModel.cs` | The period model: `TimeIsUp`, `GetShiftedTime`, `ToFromFolderModel`, `IsCalendarPreset` |
| `src/server/HSMServer.Core/Model/NodeSettings/CustomSettingsProperty.cs` | `TimeIntervalSettingProperty`: "From parent" resolution through the tree |
| `src/server/HSMServer/Folders/FolderManager.cs` | `GetCoreUpdate` (the copy on a folder save), `ReapplyFolderCalendarPresets` (the pass at start) |
| `src/server/HSMServer/Model/Controls/TimeIntervalViewModel.cs` | `ToModel` (the copy on a product save), `FromModel`, `DisplayValue` |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.cs` | `CheckSensorHistory`, `RunSensorsSelfDestroyAsync`, `ClearEmptyNodesAsync`, the retention floor of late values |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/ClearDatabaseService.cs` | The hourly cleanup |

## Storage / Persistence

The copy is in the product entity's `Settings` dictionary (`KeepHistory`, `SelfDestroy`, `TTL`), as `TimeIntervalEntity(-100, ticks)`. The folder's own period is in the folder entity's `Settings`, as the folder chose it (a calendar preset is `(preset, 0)`).

Rollback to a server from before #1864: nothing has to be undone and nothing is unreadable. A re-derived copy keeps being applied by the old server, as 31 days a month, because the old server also judges a copy by its ticks; it goes back to 0 ticks ("never") only when a folder is saved on the old server. History the cleanup has cleared and sensors it has removed do not come back.

## UI / Operator Visibility

- The product page and the sensor page show an inherited period as `From parent (<the folder's choice>)`, for example `From parent (1 month)`. The label is resolved through the view models' parents (`TimeIntervalViewModel.DisplayValue`), not through the stored copy, so it named the folder's preset before #1864 as well, while the copy read "never".
- The journal of the product records each change of the copy: `FromFolder(00:00:00)` -> `FromFolder(31.00:00:00)`.
- The `hsm-server` log: the `Warn` lines of the pass at start, and the per-sensor lines of the cleanup.

## Dependencies

- Depends on: `TreeValuesCache` product updates, `FolderManager`, the journal.
- Used by: the hourly cleanup, the retention floor of out-of-order values ([wire contract](../../api/wire-contract/feature.md)), TTL policies, sensor aliases (they expire by `SelfDestroy`, [sensor move](../sensor-move/feature.md)).

## Tests

`src/tests/HSMServer.Core.Tests/Folders/FolderPresetRetentionTests.cs`, on a real `TreeValuesCache` over LevelDB with the real `FolderManager` and `TreeViewModel`, driven the way the two controllers drive them:

- each of the four presets, for `KeepHistory` and for `SelfDestroy`, on both paths (the folder saved last, the product saved last), and a folder saved again unchanged: the cleanup clears what is older than the period, keeps what is inside it, removes the sensor idle for longer and keeps the others;
- the stored entity is `FromFolder` + the preset's length on both paths, for the three settings;
- the pass at start: the lost copies are re-derived in the model and in the database, a right copy, a product's own period and a copy of a "Never" folder are left as they are, a second pass finds nothing, each re-derived setting is reported;
- a late value older than an inherited month is refused by the retention floor;
- an empty node under an inherited preset or an inherited "Never" is kept by the sweep, which does not throw;
- what the pages show.

`TimeIntervalModelTests` pins `ToFromFolderModel` and `TimeIsUp` for the copies.

## Known Issues / Limitations

- An inherited calendar preset is its fixed length (31 days a month), not a calendar month. The two differ by at most three days a month.
- The "5 years" choice of "Keep sensor history" is not a fixed length the controls know (`TimeIntervalExtensions.IsStatic` ends below a month) and not a calendar preset: `TimeIntervalViewModel.ToModel` falls through to its "From parent" branch for it. Not covered by #1864.
- `FolderManager.ResetServerPolicyForFolderProducts`, the older pass at start, tests `Value.IsFromParent`, which a resolved value never is; it does nothing.
