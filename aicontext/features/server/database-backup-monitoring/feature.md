# Feature: Database backup monitoring (self-monitoring)

> Owner: server | Last reviewed: 2026-10-07 | Canonical: yes
> Scope: the self-monitoring sensors fed by the server's own database backup (#1525) — per-run result, backup file sizes and duration under `Backup/`, plus the per-database on-disk sizes under `Database/` — so an operator sees whether backups happen without reading logs.

---

## Overview

`BackupDatabaseService` (hosted service, `src/server/HSMServer/BackgroundServices/DatabaseServices/`) backs up two LevelDB databases on a fixed schedule: **EnvironmentData** (folders, products, sensors, users, access keys) and **ServerLayout** (dashboards). Each run writes one zip per database into `DatabasesBackups/` (`EnvironmentData_<time>.zip`, `ServerLayout_<time>.zip`), prunes old files (older than `StoragePeriodDays`, and earlier same-day files), then optionally mirrors the folder to SFTP. The **sensor values (History), Journals and Snapshots databases are not backed up** — every backup sensor's description says so.

The run reports itself through the server's embedded self-monitoring collector (`DataCollectorWrapper`, product `HSM Server Monitoring`, sensors at product root — the same `Backup/` and `Database/` nodes that already carried the backup-folder and database sizes).

Schedule: `BackupDatabase.PeriodHours` (default 1 h), ticks aligned to multiples of the period (`BaseDelayedBackgroundService` → `PeriodicTask`), read **once at start** — a period change in the Configuration page takes effect on the next server start, for the timer and for the result sensor's TTL alike. `IsEnabled = false` makes every tick a no-op (nothing is posted). The Configuration page's manual "create backup" runs the same `ServiceActionAsync` and posts the same values.

## Invariants

- **Status carries the health**: `Backup/Backup result` is posted once per run — `true`/Ok when both databases were written, `false`/Error when either failed. The Error comment is the first line of each failure (`<type>: <message>`, no stack trace), joined with `; `; the Ok comment lists the written file names.
- **A missing run shows**: the result sensor's TTL is `period + period / 2` (period clamped to ≥ 1 h): 1 h → 1.5 h, 24 h → 36 h. One skipped tick (service stuck, backups disabled, server down across the tick) turns it to Timeout; the half period absorbs the run's own duration. The other backup sensors keep `TTL = MaxValue` like every other self-monitoring sensor — liveness is the result sensor's job.
- **No default status alert**: like the rest of the self-monitoring sensors, `Alerts = []`; the server's TTL policy handles Timeout; owners add status alerts in the UI.
- **Per-file sizes**: one size sensor per backed-up database, value = the zip just written, posted only when that database's backup succeeded (a failure is reported by the result sensor, not by a 0 size).
- **Isolation (rule #6)**: every post goes through `BackupSensors.Post`, which catches and logs (`NLog`, logger `BackupSensors`) — a broken collector never breaks, delays or fails the backup; one failing post does not skip the next. A throwing backup action is turned into an Error result (it used to return `null` and kill the run with a `NullReferenceException`). File-size reads are guarded the same way.
- **Cost**: four values per run (result, duration, two sizes) plus the pre-existing `Local backup size`; no bars. At the default 1 h period that is ~120 records/day; at 24 h, five.
- **SFTP is separate**: the upload outcome stays on `Backup/Remote backup size` (status Error on upload failure); `Backup result` covers the local backup only.

## API / Public Contracts

Paths are relative to the `HSM Server Monitoring` product.

| Sensor | Type / unit | TTL | When | Description (summary) |
|---|---|---|---|---|
| `Backup/Backup result` | Bool | period × 1.5 | once per run | Outcome of the last local backup; Error + error comment on failure; Timeout when a run is missing; not-backed-up note |
| `Backup/Backup duration` | Double, Seconds | never | once per run | Wall time of writing and zipping both backups, pruning included, SFTP excluded |
| `Backup/Environment backup size` | Double, MB | never | once per run (on success) | Size of `EnvironmentData_<time>.zip`; comment = file name |
| `Backup/Dashboards backup size` | Double, MB | never | once per run (on success) | Size of `ServerLayout_<time>.zip`; comment = file name |
| `Backup/Local backup size` | Double, MB | never | once per run | Pre-existing: whole `DatabasesBackups/` folder (all retained backups); Error status when any database failed |
| `Backup/Remote backup size` | Double, MB | never | once per run with SFTP on | Pre-existing: total size on the SFTP target; Error on upload failure |
| `Database/Environment data size` | Double, MB | never | daily | On-disk size of EnvironmentData |
| `Database/Dashboards data size` | Double, MB | never | daily | On-disk size of ServerLayout |
| `Database/History data size` | Double, MB | never | daily | Pre-existing: the sensor-values database; description now states it is not backed up |

`Database/*` sizes are posted by `DataCollectorWrapper.UpdateStatictics` at most once per `DbSizeUpdateInterval` (1 day, first post at start) — the backup period is configurable down to 1 h, so the database-size cadence stays on the node's existing daily schedule. `Config data size` (= Environment + ServerLayout + Snapshots), `Journals data size` and `Total data size` are unchanged.

`IDatabaseCore` gained `EnvironmentDbSize` and `ServerLayoutDbSize` (`ConfigDbSize` is now their sum plus `Snapshots.Size`).

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/BackgroundServices/DatabaseServices/BackupDatabaseService.cs` | The run: backup both databases, prune, post the per-run values, SFTP sync |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/BackupNode.cs` | `BackupSensors`: registration (names, units, TTL formula, descriptions) and the isolating `Post` |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/Database/DatabaseSensorsSize.cs` | Daily per-database size sensors |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/DataCollectorWrapper.cs` | Creates `BackupSensors` with the configured period |
| `src/tests/HSMServer.Core.Tests/BackgroundServices/BackupDatabaseServiceTests.cs` | Success / failure / throwing / isolation / disabled paths with a fake collector; registration shape and TTL pinned |

## UI / Operator Visibility

Product `HSM Server Monitoring` → `Backup` and `Database` nodes. Wiki: `wiki-git/HSM-Server-Monitoring.md`.

## Dependencies

- Depends on: self-monitoring collector (`DataCollectorWrapper`), `IDatabaseCore` backup and size APIs, `BackupDatabaseConfig`.
- Used by: operators (alerts on `Backup result` status/Timeout).

## Tests

`BackupDatabaseServiceTests` (xUnit, `HSMServer.Core.Tests`):

- happy path: Ok result listing both files, per-file sizes in MB, duration posted, local-size comment lists both files (regression: the dashboards branch used to append the environment path);
- failure path: a database backup returning an error → Error result with the first error line, that size skipped, the other posted;
- throwing backup action → Error result with the exception message;
- isolation: every sensor post throws → backup still writes both files, every post was attempted, no exception escapes;
- disabled backups → nothing posted;
- registration: result TTL = 1.5 × period (clamped), units, `Alerts = []`, descriptions (unit, cadence, Timeout, not-backed-up note); database-size sensors register and post MB.
