# Database Architecture

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

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

## Native Libraries

LevelDB native binaries are shipped per-platform in `src/lib/runtimes/`:
- `win-x64/`, `win-x86/` — Windows
- `linux-arm/` — Linux ARM
- `osx/` — macOS
