# Feature: Write faults of the LevelDB databases

> Owner: storage | Last reviewed: 2026-11-28 | Canonical: yes
> Scope: what every LevelDB database of the server does after a write fails (a full disk), so that no write is acknowledged that a restart would drop (#1791, ADR-0024).

---

## Overview

LevelDB appends every write to its write-ahead log (`<n>.log`) before the write is applied. A write that fails there,
typically because the volume is full, can leave part of its record in the log, and LevelDB still counts the whole record
as written: its log writer moves its block offset on either way, and the failure does not stop the database (only a
failed `fsync` or a failed background flush does). The next writes are appended after the torn record and **succeed**.
When the database is opened again, the recovery meets the torn record, drops the rest of its 32 KiB block
(`(ignoring error) <n>.log: dropping <bytes> bytes; Corruption: checksum mismatch` in the database's `LOG`), and
with the block offset off it can lose its place in the blocks after it. So writes the server acknowledged after a
disk-full episode were lost on the next restart (#1791: a registration answered `200`, gone after the restart).

Proved record by record on a 2 MB tmpfs (LevelDB 1.23, Linux): five 942-byte records before the volume filled, three
more that still fit into the last page of the log, the fourth written for 656 of its 942 bytes (`No space left on
device`), then ten records after the space came back, each intact on the disk at the offset where the file ended, and
all ten dropped by the reopen: the reader took the torn record's length, read into the first record after it, failed
the checksum and dropped the rest of the block (10,076 bytes, the figure in the issue's `LOG`).

## Invariants

- **Every write of every LevelDB database goes through the guard of its database** (`LevelDbWriteGuard`): the
  environment database, the history weeks (rows and sealed chunks), journals, dashboards, change sets, snapshots, the
  download log, the table block and batch stores, and the backup copy. The wrapper's direct writes (`DB.Put`,
  `DB.Delete`, `DB.Write`) are banned in `HSMDatabase.LevelDB` and `HSMDatabase` (`src/database/BannedSymbols.txt`,
  analyzer error RS0030); the store code writes through `LevelDBDatabaseAdapter` or the `GuardedPut`, `GuardedDelete`,
  `GuardedWrite` extensions. A database is registered with its guard at its open (`LevelDBDatabaseAdapter`,
  `LevelDbOpen.Open`).
- **The writes of one database are serialized** by its guard (one native write at a time). LevelDB queues them anyway;
  what is lost is the merging of writes that wait together into one log record (group commit), synced or not: one log
  append (and, for a synced write, one `fsync`) per write. It shows only with many concurrent writers of the same
  database; the history weeks are written mostly from the update queue.
- **After a failed native write no write of that database is acknowledged until the log the failure may have torn is
  closed.** A write in that state is refused with `LevelDbWritesRefusedException` (a `ServerDatabaseException`), and
  nothing of it reaches the database. The failed write itself throws the native error, as before.
- **The log is closed by a roll**: a manual compaction of a key range that holds no key (`[0x00]`) makes LevelDB start a
  new log and write the memtable into a table; once that table is in the MANIFEST, LevelDB deletes the old log. The guard
  takes the writes again only when **no log numbered at or below the torn one is left** in the database's directory:
  LevelDB reports nothing from a manual compaction, so the directory is the proof.
- **The roll is tried right after the failure, then by a write at most once per second** (`RecoveryInterval`), and
  **only when the volume's free space takes the memtables** (`leveldb.approximate-memory-usage` plus 1 MB). A roll on a
  full volume would fail its memtable flush, and after a failed background flush LevelDB keeps the error and fails every
  later write of that database until it is opened again. With the check, a database whose write failed on a full disk
  takes writes again by itself once the space is back, without a restart, and those writes survive a restart. The check
  is best effort: each database checks alone (two rolling at once on one volume can together need more than is free),
  and a volume whose free space cannot be read is rolled anyway. A roll that fails this way costs a restart, never data.
- **When LevelDB holds a background error** (a flush or compaction failed before or despite the check, such as the
  history database of #1791 whose flush hit the full disk on its own), the database fails every write until it is
  opened again: **only a restart of the server recovers it.** The guard keeps refusing and reporting it meanwhile.
- **A restart drops only the torn record**: the recovery reads the torn log up to the torn record (everything before it
  was acknowledged and is kept) and drops what follows it, which is only the write that failed, never an acknowledged one.
- A write on a closed database (`ObjectDisposedException`, a shutdown) is not a write failure and does not put the
  database into the refusing state.
- The guard knows nothing about reads. A close of a database under a running read or write is #1961 (the history weeks
  have a gate and drain since #1958; the other databases do not).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | A write fails (full disk): the native error goes back to the caller; the database refuses writes; a roll is tried at once if the volume has room | any writer (registration, value queue, sealing job, table stores) |
| 2 | A write arrives while the database refuses writes: at most once a second a roll is tried (if the volume has room); refused unless it closed the torn log | any writer |
| 3 | The space is back: the next write's roll closes the torn log; that write and the later ones are taken and durable | any writer |
| 4 | LevelDB holds a background error: every write fails until the server restarts | operator restarts the server |

What a caller does with the failure is its own contract and unchanged: a registration answers `500` (#1756), so the
collector sends the package again; a value write is logged by the update queue; a table block put is not acknowledged
and its collector keeps it (`Database/Tables/Write failures`). The large file store keeps its files outside LevelDB
and is not covered here (ADR-0022).

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `LevelDbWriteGuard` (`Register`, `Of`, `Put`, `Delete`, `Write`, `IsRefusingWrites`) | `src/database/HSMDatabase.LevelDB/LevelDbWriteGuard.cs` | Internal test seams: `BeforeWrite`, `BeforeRecovery`, `RecoveryInterval` |
| `LevelDbWrites.GuardedPut/GuardedDelete/GuardedWrite` | same file | The only way the store code writes to a raw `DB` |
| `LevelDbWritesRefusedException : ServerDatabaseException` | same file | Thrown for a refused write; nothing of it was written |
| `LevelDbWriteFaults` (`Shared`, `Failures`, `RefusedWrites`, `RefusingDatabases`) | same file | Process-wide counters for the self-monitoring |

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.LevelDB/LevelDbWriteGuard.cs` | The guard, the write extensions, the refused-write exception and the fault counters |
| `src/database/HSMDatabase.LevelDB/Database.cs` | `LevelDBDatabaseAdapter`: registers its database and writes through the guard |
| `src/database/HSMDatabase.LevelDB/LevelDbOpen.cs` | Opens the table stores' databases and registers them |
| `src/database/BannedSymbols.txt` | The banned direct writes |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/Database/StoreWriteFaultSensors.cs` | The self-monitoring sensors |

## Storage / Persistence

None of its own: no key, file or format changes. The guard reads the names of the `*.log` files of the database's
directory and LevelDB's `leveldb.approximate-memory-usage` property.

## UI / Operator Visibility

- **Log**: an `Error` when a database starts refusing writes (`LevelDB database <path>: a write failed (...)`), one
  `Warn` when the first roll cannot close the log (with the reason: too little free space, or LevelDB did not close
  it), an `Info` when the writes are taken again.
- **Self-monitoring** (`Database/`, posted with the 5-minute sample):
  - `Write failures`: native writes that failed since the start; Error in the sample in which it grew.
  - `Refused writes`: writes refused after a failure since the start; Error in the sample in which it grew.
  - `Databases refusing writes`: how many databases refuse writes now, their paths in the comment; Error while above 0.
    Stays above 0 until the space is back and a write came (the roll is tried by writes), or until a restart when
    LevelDB holds a background error.
  - These values are stored in the history database: while that one refuses writes they are only in memory (the UI
    shows them; they are not in the history).
- A refused registration is a `500` with the text of `LevelDbWritesRefusedException`.

## Dependencies

- Depends on: the LevelDB wrapper `SoftFX.LevelDB.Standard` (LevelDB 1.23 on Linux): the log writer's behaviour on a
  failed append, `CompactRange` flushing the memtable and switching the log, obsolete logs deleted after the flush.
- Used by: every store of `HSMDatabase.LevelDB` and `HSMDatabase`.

## Tests

- `src/tests/HSMDatabase.LevelDB.Tests/StorageTests/LevelDbDiskFullTests.cs`: a real full volume. Needs
  `HSM_TEST_SMALL_VOLUME` (a directory on a small file system the test may fill); skipped without it. CI: the
  `disk-full` job of `server-tests.yml` mounts a 4 MB tmpfs on the Linux runner. Locally (any OS with Docker), build the
  test project and run its output in a Linux container:

  ```sh
  dotnet build src/tests/HSMDatabase.LevelDB.Tests
  docker run --rm --tmpfs /vol:rw,size=4m -e HSM_TEST_SMALL_VOLUME=/vol \
    -v <repo>/src/tests/HSMDatabase.LevelDB.Tests/bin/Debug/net8.0:/tests:ro mcr.microsoft.com/dotnet/sdk:8.0 \
    sh -c 'cp -r /tests /work && dotnet test /work/HSMDatabase.LevelDB.Tests.dll --filter FullyQualifiedName~LevelDbDiskFullTests'
  ```

  (the copy: the wrapper unpacks its native library next to the assembly). Before the fix every acknowledged write made
  after the space came back was lost by the reopen, synced or not.
- `src/tests/HSMDatabase.LevelDB.Tests/StorageTests/LevelDbWriteGuardTests.cs`: every platform. The failed write is
  stood for by the `BeforeWrite` seam; the roll is the real one, made to fail by a directory in the place of the next log
  file. Refusal, the roll after the blocker goes, the once-a-second retry, every write of the adapter, a closed database,
  the guard of a database opened by `LevelDbOpen`.
- `src/tests/HSMServer.Core.Tests/BackgroundServices/StoreWriteFaultSensorsTests.cs`: the self-monitoring sensors.

## Not covered

- Reopening a database that holds a LevelDB background error without a restart: it needs a gate and drain of the reads
  of every database (#1961).
- A node of the tree whose row was not written stays in memory (its writes swallow the store's failure): #1986.
