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
- Backup via SFTP to `DatabasesBackups` volume
- `ConcurrentStorage<T>` pattern: in-memory cache synced with LevelDB on writes
- Serialization uses MemoryPack for high performance
- **A failed write of a sensor or product row is logged, not thrown.** `EnvironmentDatabaseWorker` catches the exception of the LevelDB `Put`, writes an `Error` line and returns, and `IDatabaseCore.AddSensor` / `UpdateSensor` are void: their callers are not told. One caller has to know, the registration of a sensor by a collector (#1756: a registration answered as applied is not sent again). It uses the checked variants, `IDatabaseCore.AddSensorChecked` / `UpdateSensorChecked` (over `IEnvironmentDatabase.WriteSensorIdToList` / `WriteSensor`), which do the same writes and return the failure, or null when the row was written; nothing is thrown there either. In the worker the void methods are the checked ones with the outcome dropped, so the two cannot drift apart; `AddSensorChecked` differs from `AddSensor` only in what it does after a failure (it does not write the row when the id list could not be written, and takes the id off the list again when the row could not). Do not move another caller to the checked variants without deciding what it does with a failure: today a failed write leaves the object in memory and the next save writes it.

## Native Libraries

The LevelDB native libraries come inside the NuGet package `SoftFX.LevelDB.Standard` (repository `SoftFx/leveldb.net`), embedded in its managed DLL and extracted at run time: Windows x64, Windows x86 and Linux x64. There is no Linux ARM or macOS library. (`src/lib/runtimes/` holds LMDB binaries, not LevelDB, and nothing in the build uses them.)

## Block Compression (#1815)

Every database is opened through `LevelDBDatabaseAdapter`, which sets the compression of the blocks it writes in one place, `LevelDBDatabaseAdapter.WriteCompression`. LevelDB records the compression of each block, so reading never depends on that setting, only on whether the native library knows the codec.

- **Up to package 1.2.1** the Linux library had no Snappy. Every database a Linux server wrote is uncompressed (sensor rows about 137 B/row against 54 B/row on Windows), and a database written on Windows could not be opened on Linux (`Corruption: corrupted compressed block contents`).
- **Package 1.2.2** has Snappy on Linux, so the server reads Snappy blocks on every platform.
- **Writing, step 1 (the first release on 1.2.2):** Windows writes Snappy, as it always did. Everything else writes uncompressed, so a rollback to a release on 1.2.1 can still read all the data this release wrote. `LevelDBCompressionTests` pins both halves.
- **Writing, step 2 (the next release):** Linux writes Snappy. After it, a rollback past the step-1 release cannot read the new blocks. Old uncompressed blocks shrink only when a compaction rewrites them; `Compact()` does not rewrite an already compacted database.
