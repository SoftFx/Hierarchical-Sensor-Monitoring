# Database Architecture

> Owner: shared | Last reviewed: 2026-11-19 | Canonical: yes

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
- Backup via SFTP to `DatabasesBackups` volume
- `ConcurrentStorage<T>` pattern: in-memory cache synced with LevelDB on writes
- Serialization uses MemoryPack for high performance
- **A failed write of a sensor or product row is logged, not thrown.** `EnvironmentDatabaseWorker` catches the exception of the LevelDB `Put`, writes an `Error` line and returns, and `IDatabaseCore.AddSensor` / `UpdateSensor` are void: their callers are not told. One caller has to know, the registration of a sensor by a collector (#1756: a registration answered as applied is not sent again). It uses the checked variants, `IDatabaseCore.AddSensorChecked` / `UpdateSensorChecked` (over `IEnvironmentDatabase.WriteSensorIdToList` / `WriteSensor`), which do the same writes and return the failure, or null when the row was written; nothing is thrown there either. In the worker the void methods are the checked ones with the outcome dropped, so the two cannot drift apart; `AddSensorChecked` differs from `AddSensor` only in what it does after a failure (it does not write the row when the id list could not be written, and takes the id off the list again when the row could not). Do not move another caller to the checked variants without deciding what it does with a failure: today a failed write leaves the object in memory and the next save writes it.

## Weekly Shards and Their Compaction

Sensor history and the presentation journal are split into one LevelDB database per week (`SensorValuesV2_<from>_<to>` under the database folder, `JournalValues_<from>_<to>` under `Journals/`; Monday 00:00 to the next Monday). `ValuesDatabaseDictionary<T>` discovers the shard directories at startup and opens each one lazily, on first use; `GetShards()` lists them without opening any.

Rewritten and deleted rows stay in a shard's files until a LevelDB compaction merges them, and a week nobody writes to any more is not compacted on its own (a production week measured 65 MB before and 23 MB after `Compact()`). Two paths compact:

- **Manual:** the Compact button (`HomeController.Compact` → `IDatabaseCore.Compact`) compacts the environment database and every shard, the current week included, in one go.
- **Background (#1801):** `HistoryCompactionService` calls `IDatabaseCore.CompactNextDueHistoryShard` every 10 minutes (first pass 10 minutes after start). Each call compacts **at most one** shard, so the backlog of the first run after an upgrade (every closed week, sensor and journal) drains one shard per pass instead of in one burst. The newest due week goes first; on the same week the sensor shard before the journal shard.

A shard is due (`HistoryShardCompactionPlanner`, pure and unit-tested) when all of these hold:

- its week began before the current week did: the current week and any later one are never compacted by the service;
- it carries no compaction marker: it was never compacted, or it changed since;
- it has not changed for the quiet period (3 hours, longer than the hourly retention pass): a shard still being trimmed or written to waits until that stops instead of being compacted after every change;
- no failed attempt holds it back: a failed compaction is logged with the shard name and the shard is retried a day later (in memory; a restart retries at once). Other shards go on meanwhile.

**The compaction marker** is a file named `hsm-compacted` inside the shard directory (`CompactedShardMarker`). It is written after a compaction and removed by the shard worker (`IntervalDataseBase.OnBeforeChange`) before any write or delete that changes the shard: a late value, a clear-history or retention delete that finds rows, a sensor-move merge, a journal write. A range delete that finds no row leaves it in place. Because it is removed before the data changes, a crash in between costs at most one extra compaction. A change that lands while the compaction runs keeps the marker off (`TryMarkCompacted` compares the change counter read before the compaction). The marker lives in the shard rather than in the environment database so that it is read without opening the shard, survives a restart, and travels with the shard when the directory is copied, backed up or restored. LevelDB claims only `CURRENT`, `LOCK`, `LOG*`, `MANIFEST-*` and numbered files of its directory and ignores this one.

Each background compaction logs the shard path and its size before and after. The two paths share `IsCompactRunning`, taken with a compare-and-swap: the service skips its pass while the manual compaction runs, and the button answers "already running" while a background step runs. The manual compaction does not write markers.

## Native Libraries

LevelDB native binaries are shipped per-platform in `src/lib/runtimes/`:
- `win-x64/`, `win-x86/` — Windows
- `linux-arm/` — Linux ARM
- `osx/` — macOS
