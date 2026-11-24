# Feature: Table block store

> Owner: storage | Last reviewed: 2026-11-24 | Canonical: yes
> Scope: where and how the server stores the blocks of table sensors as they arrive, finds them for a read, detects a block sent twice, and recovers from a crash (#1844, part 2b of epic #1842; ADR-0021 point 8). The block format: `aicontext/features/api/table-block/feature.md`. The table sensor kind: `aicontext/features/server/table-sensors/feature.md`. The transport that sends blocks is #1845; the hourly merge and retention are #1850.

---

## Overview

A collector sends the rows of all its table sensors as one **block** per tick. The server checks the block, decides per table whether it takes the rows, and stores the block **unchanged**, with a small envelope in front, in a LevelDB database of its own under a key that only grows. Rows are readable as soon as their block is stored. Nothing of this is opened or created on a server that never sees a table sensor.

Two parts:

- `TableBlockDatabase` (`src/database/HSMDatabase.LevelDB/TableBlocks/`, behind `ITableBlockStore` in `HSMDatabase.AccessManager/TableBlocks/`): the databases, the order of appends, the in-memory index, duplicates, recovery. `IDatabaseCore.TableBlocks` holds the one instance; `DatabaseCore.Dispose` closes it.
- `TableBlockService` (`src/server/HSMServer.Core/TableSensors/`): the put (checks a block against the sensors of a product) and the read (the rows of one sensor in a time range).

## On disk

| Path | Holds |
|---|---|
| `<DatabaseFolder>/Tables/Blocks` | LevelDB. Key `arrival hour ticks (i64, big-endian) | sequence (i64, big-endian)`, 16 bytes. Value: the envelope, then the block bytes exactly as received. No LevelDB compression (blocks are zstd already). Every put is synced. |
| `<DatabaseFolder>/Tables/BlockIndex` | LevelDB. The same key; value: 80 bytes describing the block (below). Derived from `Blocks`; written after the block, not synced. |
| `<DatabaseFolder>/Tables/hsm-rebuild-index` | Present only after an index write failed: the next open rebuilds the index. |

The folder is created by the first use that needs it: the registration of a table sensor (which asks for the next sequence) or a put. A read on a server without the folder opens nothing.

### Key

`arrival hour` is the UTC hour (ticks at the start of the hour) in which the server stored the block; `sequence` is a number that only grows, restored at open as the last key's sequence plus one. The hour of a key is never below the hour of the key before it: if the clock steps back, the hour stays where it was (the true arrival time is in the envelope). So both parts only grow and every write lands after every key already written: LevelDB never rewrites the data during ingest (measured below, and pinned by a test that reads LevelDB's `LOG`).

### Envelope (version 1, little-endian)

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | version, `1` |
| 1 | 1 | flags, `0` |
| 2 | 2 | envelope length: the offset of the block bytes |
| 4 | 8 | arrival time, UTC ticks (the true clock) |
| 12 | 16 | product id: the product the block's tables were resolved in |
| 28 | 16 | producer id: who sent the block (the collector's stream id, #1845) |
| 44 | 8 | producer sequence: the producer's number of the block |
| 52 | 2 | refused table count n |
| 54 | 5·n | per refused table: directory index (i32), reason (u8, `TableRefusalReason`) |

The block follows at the envelope length. A refused table's rows are never served.

### Index value (version 1, 80 bytes, little-endian)

`u8 version (1), u8 0, u16 refused count, i64 first time, i64 last time, i32 rows, i32 tables, i32 stored length, 16 product id, 16 producer id, i64 producer sequence, i64 arrival ticks`. First and last time are the block header's (the minimum and maximum time of all its rows).

## The order of appends

Several threads put blocks at once. A put reserves a sequence number (`Reserve`) **before** it matches the block's tables against the sensors, and appends (or skips) it afterwards; the store writes tickets strictly in the order they were reserved: an append waits until every smaller ticket is done (appended, skipped, or failed). The wait releases the lock; decompressing and matching a block never happen under it. A ticket must always be finished: the service skips it in a `finally`.

## Duplicates

A block whose `(producer id, producer sequence)` is already stored is not written again; the put answers what the first one did (its stored sequence and refusals). The in-memory map of stored pairs is rebuilt from the index at open, so this holds across restarts, for as long as the block is stored. Once #1850 deletes merged blocks, it needs a per-producer watermark (see "What later steps must do").

## Crash and failure

| When | After a restart |
|---|---|
| Before the block's put | Nothing stored, nothing acknowledged; the collector sends the block again; stored once. |
| After the block's put, before its index entry | The open indexes every block after the index's last key again ("tail"), so the block is known; the resend is a duplicate. |
| After both, before the acknowledgement | The same: the resend is a duplicate. |
| An index write failed (the block is stored) | The block is served from memory; `hsm-rebuild-index` makes the next open rebuild the index. |
| The index is missing, damaged, or holds a key beyond the last block | The open rebuilds it from the blocks (`Index rebuilds` counter, a Warn line). |

A failed block write (disk, LevelDB) fails the put with `TableBlockStoreException`, a fault: the put is not acknowledged and the collector keeps the block. The store then refuses puts for a minute (LevelDB keeps a background error until it is reopened) and reopens on the first put after that. Puts are refused the same way while the disk has less free space than `TableSensors.MinFreeDiskMb`. A stored block that cannot be read back is counted (`Read failures`) and skipped by reads.

## The put (`ITableBlockService.Put`)

`TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block)`, in this order:

1. **Verify** the block with `TableBlockReader.ReadDirectory` under the configured limits (header, checksum, frame, directory, every time value; no column is copied). A block that fails is answered `Invalid` with the format's error code; nothing is reserved or stored (`Invalid blocks` counter). This decompression happens before anything is locked.
2. **Reserve** a sequence (`Reserve`). This is the first use that opens the store.
3. **Match** every directory entry against the sensors, outside any lock. The identifier is the sensor path in UTF-8, looked up in the root product `productId` (the product of the key that sent the block). A table is refused with the first reason that applies:

   | Reason | When |
   |---|---|
   | `InvalidIdentifier` | the identifier is not valid UTF-8 |
   | `UnknownSensor` | no sensor has the path in the product |
   | `NotATableSensor` | the sensor there is of another kind |
   | `SensorBlocked` | the table sensor is blocked (a muted one takes rows) |
   | `SensorCreatedAfterBlock` | the table sensor's `FirstSequence` is above the block's sequence (below) |
   | `SchemaIdMismatch` | the entry's schema id is not the sensor's |
   | `ColumnTypesMismatch` | the entry's column types are not the sensor's (the id alone is not trusted) |

4. **Append** in sequence order: a duplicate is answered `Duplicate` with the first put's sequence and refusals (nothing written); a block whose tables were all refused is answered `AllRefused` and **not stored**; otherwise the block is stored with its refusals in the envelope and answered `Stored`.

Refusals are results. A fault of the store (a write that failed, too little free disk, a store that is reopening) throws `TableBlockStoreException`: the block is not stored, and the caller must not acknowledge it, so the collector sends it again. Every refused table is counted (`Tables refused`), logged at Debug, and summarised in a Warn line at most once a minute.

**Why a block with every table refused is not stored.** Its bytes would never be read and would cost disk and, later, merge work; the caller learns every refusal from the result. Not storing it also means that the same block sent again after its sensors were registered is taken, not answered as a duplicate. A block with at least one accepted table is stored with its refusals, and a resend of it is a duplicate: a refused table stays refused, as ADR-0021 point 8 says.

## A sensor created during a put

A put reserves its sequence before it matches its tables; a new table sensor records, before it can be seen, the sequence the next reservation will get as its `FirstSequence` (`TreeValuesCache.TableBlockSequence`, which reads `ITableBlockStore.NextSequence`). Two rules follow:

- a read of a sensor takes only blocks with a sequence at or above its `FirstSequence`;
- a put refuses a table whose sensor's `FirstSequence` is above the block's sequence (`SensorCreatedAfterBlock`).

So a block numbered before a sensor existed is never read for it: its tables were matched against an earlier sensor of the same path, or against none. And every table a put accepts for a sensor is in a block that sensor reads. A sensor is removed before a new one is created on its path (the product's queue), and a removed sensor is never read. Pinned by tests that remove and create the sensor between the reservation and the match.

## The read (`ITableBlockService.Read`)

`TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null)`: the rows of a table sensor with a time in `[from, to)`.

- **Finding blocks.** From the in-memory index of the sensor's root product: blocks with a sequence at or above the sensor's `FirstSequence` whose header range `[first time, last time]` meets `[from, to)`. A late block with old times is found by its header, wherever it is in the sequence. Nothing else is opened.
- **Rows.** Each candidate is read (one LevelDB get), its table read out by identifier (`TableBlockReader.ReadTable`, which decompresses the block), skipped when the envelope marks it refused, and its rows filtered to the range.
- **Order.** By time, then by block sequence (arrival), then by the row's position in its block: a stable sort of the rows in the order they arrived.
- **Limit.** At most `maxRows` rows (capped by `TableSensors.MaxReadRows`, default 1,000,000), the earliest ones, `Truncated` when more matched. Memory stays within about twice the limit: past it, the rows collected so far are sorted and the latest dropped.
- **Damage.** A block that is missing or cannot be read is counted (`Read failures`, `BlocksFailed` in the result) and skipped.

## Configuration, self-monitoring, sizes

See "Operations" below (#1844, part 2b, commit 6).

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.AccessManager/TableBlocks/ITableBlockStore.cs` | The store's interface, index entry, ticket, statistics |
| `src/database/HSMDatabase.AccessManager/TableBlocks/TableBlockEnvelope.cs` | The envelope and the refusal reasons |
| `src/database/HSMDatabase.LevelDB/TableBlocks/TableBlockDatabase.cs` | The store |
| `src/server/HSMServer.Core/TableSensors/TableBlockService.cs` | The put and the read; the rule for a sensor created during a put |
| `src/server/HSMServer.Core/TableSensors/TableRowCollector.cs` | Collects, filters and sorts the rows of one table |
| `src/server/HSMServer.Core/TableSensors/TableBlockResults.cs` | Options, results, rows |

## Tests

`src/tests/HSMDatabase.LevelDB.Tests/TableBlockTests/TableBlockDatabaseTests.cs`: nothing created before first use; key order and a restart; a block read back unchanged with its envelope; duplicates (also after a restart, and per producer); a block not to be stored; a crash between the block and its index entry, then reopen and resend; a deleted and a damaged index rebuilt; a clock stepping back; tickets waiting for earlier ones and a skipped ticket; eight threads putting while a reader reads; the disk guard; the `LOG` of both databases after 5,000 puts through 16 KB memtables shows flushes and no real compaction, and the same detector sees real compactions when keys come in random order.

`src/tests/HSMServer.Core.Tests/TableSensors/TableBlockServiceTests.cs`: put and read across hour boundaries with a late block and overlapping blocks, equal times in arrival order, `[from, to)` edges; the row limit; a refused table inside an accepted block for every reason and a sensor unblocked afterwards; the schema id and an identifier that is not UTF-8; a block with every table refused, then its resend after the registration; a duplicate; an invalid block and one beyond the limits as results (the store is not even opened); the disk guard as a fault; a removed sensor and a new one on its path; a sensor created between the reservation and the match (both directions); six producers putting while a reader reads; a server without table sensors creates no `Tables` folder.
