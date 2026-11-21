# Database Architecture

> Owner: shared | Last reviewed: 2026-11-20 | Canonical: yes

## Overview

HSM stores application data in an embedded **LevelDB** database through `HSMDatabase.LevelDB` / `SoftFX.LevelDB.Standard`. No external database service is required.

## Projects

| Project | Purpose |
|---|---|
| `HSMDatabase` | Abstract interfaces (`IDatabaseCore`, `ISnapshotDatabase`) and settings |
| `HSMDatabase.LevelDB` | LevelDB implementation of database interfaces |
| `HSMDatabase.AccessManager` | Data access layer with entity formatting and typed read/write operations |

## Data Stored

- Sensor history values (all types: bool, int, double, string, timespan, version, bar, file)
- Product metadata and access keys
- User accounts and roles
- Sensor configurations and policies (TTL, alerts)
- Journal entries (audit log)
- Tree snapshots

## Key Design Points

- Database is embedded in the server process — no network overhead
- Data path is configured via volume mount (`/app/Databases`)
- Backup via SFTP to `DatabasesBackups` volume: the environment and server layout databases only; the weekly history databases (rows and sealed chunks) are not backed up
- `ConcurrentStorage<T>` pattern: in-memory cache synced with LevelDB on writes
- Serialization uses MemoryPack for high performance
- **A failed write of a sensor or product row is logged, not thrown.** `EnvironmentDatabaseWorker` catches the exception of the LevelDB `Put`, writes an `Error` line and returns, and `IDatabaseCore.AddSensor` / `UpdateSensor` are void: their callers are not told. One caller has to know, the registration of a sensor by a collector (#1756: a registration answered as applied is not sent again). It uses the checked variants, `IDatabaseCore.AddSensorChecked` / `UpdateSensorChecked` (over `IEnvironmentDatabase.WriteSensorIdToList` / `WriteSensor`), which do the same writes and return the failure, or null when the row was written; nothing is thrown there either. In the worker the void methods are the checked ones with the outcome dropped, so the two cannot drift apart; `AddSensorChecked` differs from `AddSensor` only in what it does after a failure (it does not write the row when the id list could not be written, and takes the id off the list again when the row could not). Do not move another caller to the checked variants without deciding what it does with a failure: today a failed write leaves the object in memory and the next save writes it.

## Weekly Shards and Their Compaction

Sensor history and the presentation journal are split into one LevelDB database per week (`SensorValuesV2_<from>_<to>` under the database folder, `JournalValues_<from>_<to>` under `Journals/`; Monday 00:00 to the next Monday). `ValuesDatabaseDictionary<T>` discovers the shard directories at startup and opens each one lazily, on first use; `GetShards()` lists them without opening any.

A closed history week can be **sealed** (ADR-0020): its rows encoded into chunks in a sibling LevelDB database `SensorValuesSealedV1_<from>_<to>`, complete only when it holds the file `hsm-sealed`. When the week is opened, `LevelDBManager` then serves it with `SealedSensorValuesDatabase` instead of `SensorValuesDatabaseWorker`: the row database stays as the overlay, reads merge it with the chunks, writes go to it, range deletes rewrite the chunks they touch. A week whose chunk directory exists without the marker is served from its rows alone. `SensorHistoryDbSize` counts both directories of a week. Layout, merge rule, range-delete crash states and error reporting: `aicontext/features/storage/sealed-history/feature.md`, section "Sealed week store".

Weeks are sealed by the **sealing job** (#1804, `HistorySealingService` → `IDatabaseCore.RunHistorySealingPass`, off by default: `HistorySealing.Enabled`). Every history week sits behind a `SensorValuesWeek` handle in the shard dictionary, which lets the job swap a week from rows to sealed (and back, for an unseal) while the server runs. A pass takes one action on one week, chosen by `HistorySealingPlanner` (pure, unit-tested): finish an interrupted unseal, remove the source rows of a sealed week whose keep period ended, seal a closed week, or fold a sealed week's overlay. A pass enters the same compaction state as the two compaction paths below, so sealing, folding, unsealing, the background compaction and the manual Compact never run at the same time, and shutdown waits for a running pass (which also stops at its next safe point once disposal has begun). Its stages live in marker files next to `hsm-sealed`, in the week's own directories. Design, markers, crash states and configuration: the feature doc, section "Sealing job".

Rewritten and deleted rows stay in a shard's files until a LevelDB compaction merges them, and a week nobody writes to any more is not compacted on its own (a production week measured 65 MB before and 23 MB after `Compact()`). Two paths compact:

- **Manual:** the Compact button (`HomeController.Compact` → `IDatabaseCore.Compact`) compacts the environment database and every shard, the current week included, in one go. It writes the compaction marker into every closed shard it compacted (not into the current week's, whose next write would remove it), so the background step does not compact those shards again.
- **Background (#1801):** `HistoryCompactionService` calls `IDatabaseCore.CompactNextDueHistoryShard` every 10 minutes (first pass 10 minutes after start). Each call compacts **at most one** shard, so the backlog of the first run after an upgrade (every closed week, sensor and journal) drains one shard per pass instead of in one burst. The newest due week goes first; on the same week the sensor shard before the journal shard.

  It is **off by default** and confined to a daily window: section `HistoryCompaction` of `Config/appsettings.json` (`HistoryCompactionConfig`, read once at startup, written back with its defaults, validated with an error that names the key):

  | Key | Default | Meaning |
  |---|---|---|
  | `Enabled` | `false` | Off: a pass does nothing and opens nothing. Off by default so that an upgrade changes nothing on disk until the operator says so: the first run after it finds every closed week due, and on a large deployment (more than a hundred weekly shards of up to several GB) that is about a day of continuous rewriting (`HistoryCompactionConfig.DefaultEnabled`). |
  | `WorkingHoursStartUtc`, `WorkingHoursEndUtc` | `"00:00"`, `"00:00"` | Daily UTC window, `HH:mm`; equal values mean all day, a window may wrap past midnight. Same parsing and semantics as `HistorySealing` (`DailyUtcWindow`). Outside it a pass does nothing. |

  The check lives in the service; `IDatabaseCore.CompactNextDueHistoryShard` itself compacts whenever it is called. A compaction that is running when the window ends is not interrupted (a LevelDB compaction cannot be), so the work may overrun the window by one shard. At construction the service logs `History compaction is off (HistoryCompaction.Enabled)`, and a Warn when `HistorySealing.Enabled` is true at the same time (sealed weeks do not get smaller on disk after their source rows are removed until this step runs, see below); when on, a start line names the window and the first pass (`History compaction is on: one closed history shard per pass[, working HH:mm-HH:mm UTC]; first pass at <UTC>, then every 10 minutes`). The switch does not affect the manual Compact below nor the compaction a seal does of its own chunk database; neither goes through this service.

A shard is due (`HistoryShardCompactionPlanner`, pure and unit-tested) when all of these hold:

- its week began before the current week did: the current week and any later one are never compacted by the service;
- it carries no compaction marker: it was never compacted, or it changed since;
- it has not changed for the quiet period (3 hours, longer than the hourly retention pass): a shard still being trimmed or written to waits until that stops instead of being compacted after every change;
- no failed attempt holds it back: a failed compaction is logged with the shard name and the shard is retried a day later (in memory; a restart retries at once). Other shards go on meanwhile.

**The compaction marker** is a file named `hsm-compacted` inside the shard directory (`CompactedShardMarker`). It is written after a compaction (background or manual) and removed by the shard worker (`IntervalDataseBase.OnBeforeChange`) before any write or delete that changes the shard: a late value, a clear-history or retention delete that finds rows, a sensor-move merge, a journal write. A range delete that finds no row leaves it in place. Because it is removed before the data changes, a crash in between costs at most one extra compaction. A change that lands while the compaction runs keeps the marker off (`TryMarkCompacted` compares the change counter read before the compaction). If the marker cannot be deleted, the shard still counts as changed in memory and the failure is logged once (not on every later write); the stale file on disk can make the shard miss one compaction after a restart, which costs space, not data. If the background step cannot write a marker, the step counts as failed and the shard waits for the failure backoff. The marker lives in the shard rather than in the environment database so that it is read without opening the shard, survives a restart, and travels with the shard when the directory is copied, backed up or restored. LevelDB claims only `CURRENT`, `LOCK`, `LOG*`, `MANIFEST-*` and numbered files of its directory and ignores this one.

Each background compaction logs the shard path and its size before and after. The two paths share `IsCompactRunning`, taken with a compare-and-swap (the sealing job takes it too): the service skips its pass while the manual compaction or a sealing pass runs, and the button answers "already running" while a background step runs. A sealing pass that finds the state taken is skipped and logs what held it (#1805). A seal changes no row, so the week keeps its compaction marker and this step does not visit it; the seal compacts the new chunk database itself, outside the week's gate, before it marks the week sealed (#1805). After the sealing job removes a week's source rows, the deletes have already cleared its compaction marker, so this background step compacts the week (rows and chunks) once it has been quiet for 3 hours, and only when `HistoryCompaction.Enabled` is true and inside its window; the job does not compact it then. A week that the hourly retention pass is trimming is never quiet for 3 hours. On a sealed week retention costs almost nothing since #1805: a delete of a prefix of one sensor's history (retention, clear history, sensor removal) deletes the chunks wholly below its cutoff and records a per-sensor delete watermark in the chunk database instead of re-encoding the chunk the cutoff falls into; other range deletes still rewrite their border chunks (feature doc, "Writes and range deletes"). In the manual path a shard that fails to open or to compact is logged with its name and the next shard goes on.

**Shutdown and compaction exclude each other.** Once `DatabaseCore.Dispose` has begun, no compaction starts (the background step answers `ShuttingDown`, the button is refused), and `Dispose` waits for a running one to finish before it closes any database: a LevelDB compaction cannot be cancelled, and closing a shard under it would free the native database it still uses. A manual run that is going over all databases stops after the shard it is compacting, so the shutdown waits for one shard, not for the rest. The wait is logged with the name of the database being compacted, every 30 s. The server disposes the database after the host has stopped its hosted services, when the DI container is disposed (and once more from `TreeValuesCache.DisposeAsync`; the second call returns at once). The host's shutdown timeout (`HostOptions.ShutdownTimeout`, 30 s by default, not set by HSM) bounds only how long the host waits for the hosted services to stop, so it does not cut this wait short. What can is the process manager: `docker stop` kills the container 10 s after SIGTERM unless `stop_grace_period` says otherwise, and HSM's compose files do not set it. A kill during a compaction is what a crash is to LevelDB: the shard reopens from its last consistent state, keeps no marker, and is compacted again later.

## Native Libraries

LevelDB native binaries are shipped per-platform in `src/lib/runtimes/`:
- `win-x64/`, `win-x86/` — Windows
- `linux-arm/` — Linux ARM
- `osx/` — macOS
