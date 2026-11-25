# Feature: Table block store

> Owner: storage | Last reviewed: 2026-11-25 | Canonical: yes
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
| `<DatabaseFolder>/Tables/BlockIndex` | LevelDB. The same key; value: 84 bytes describing the block (below). Derived from `Blocks`; written after the block, not synced. |
| `<DatabaseFolder>/Tables/hsm-sequence-mark` | The sequence high-water mark: a decimal number above every sequence ever handed out (below). Written durably. |
| `<DatabaseFolder>/Tables/hsm-rebuild-index` | Present only after an index write failed: the next open rebuilds the index. |

The folder's name is `IDatabaseSettings.TableBlocksFolderName` (`Tables` in `DatabaseSettings`), and its path `IDatabaseSettings.PathToTableBlocks`, defined once in the interface like the other database paths. The folder is created by the first use that needs it: the registration of a table sensor (which asks for the next sequence) or a put. A read on a server without the folder opens nothing.

**The first open is not under the tree lock.** The first table registration of a process opens the store: both databases, the whole index into memory, and a rebuild from every block if the index is missing or damaged. `TreeValuesCache.TryAddSensor` does that (`ITableBlockStore.EnsureOpened`, which also writes the sequence mark ahead) for a table registration only, before it takes `_treeStructureLock`; under the lock the sensor's first sequence is read with `PeekNextSequence`, which never opens and costs an in-memory read (plus, at most once per 1,024 sequences reserved in between, the durable write of the mark). When the mark on disk already covers the value, the peek does not take the store's lock either: an append holds that lock across its synced put, and a registration (with every structural change of the tree behind it) must not wait for the disk. It reads the next sequence and the persisted mark with `Volatile` (both only grow and are written under the store's lock; the persisted mark is set only once the mark file holding it is durable) and answers when the first is below the second; otherwise, and for a store not open, failed or disposed, it takes the lock as before. So the mark on disk is above every sequence it answers, and a block reserved after the sensor became visible gets a sequence at or above the sensor's first sequence. `TryAddSensor` is the only path to `TryAddSensorUnderLock`, the only reader of the sequence. If the store failed between the two calls, `PeekNextSequence` refuses instead of reopening under the lock, and the registration is a fault the collector repeats; the same when `EnsureOpened` fails. A registration of any other kind touches nothing of the store.

**A reopen swaps the in-memory index in one assignment.** An open builds the per-product index and the duplicate map aside and replaces both with one reference assignment at its end, so a reader that does not take the store's lock (`GetBlocks`) sees the old index in full or the new one in full, never a cleared or half-filled one. Its reads of block values wait for the reopen (`_dbLock`) and then read the reopened database.

### Key

`arrival hour` is the UTC hour (ticks at the start of the hour) in which the server stored the block; `sequence` is a number that only grows, restored at open as the larger of the sequence mark and the last key's sequence plus one (section "Sequence mark"). The hour of a key is never below the hour of the key before it: if the clock steps back, the hour stays where it was (the true arrival time is in the envelope). So both parts only grow and every write lands after every key already written: LevelDB never rewrites the data during ingest (measured below, and pinned by a test that reads LevelDB's `LOG`).

### Envelope (version 1, little-endian)

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | version, `1` |
| 1 | 1 | flags, `0` |
| 2 | 2 | reserved, `0` |
| 4 | 4 | envelope length (i32): the offset of the block bytes |
| 8 | 8 | arrival time, UTC ticks (the true clock) |
| 16 | 16 | product id: the product the block's tables were resolved in |
| 32 | 16 | producer id: who sent the block (the collector's stream id, #1845) |
| 48 | 8 | producer sequence: the producer's number of the block |
| 56 | 4 | refused table count n (i32) |
| 60 | 5·n | per refused table: directory index (i32), reason (u8, `TableRefusalReason`) |

The block follows at the envelope length. A refused table's rows are never served. The counts are 32-bit, so every table of any block the format allows can be recorded as refused, whatever `TableSensors.MaxTables` is configured to (the first layout had 16-bit counts, too small for the default of 65,536 tables; changed in review, before anything was stored).

### Index value (version 1, 84 bytes, little-endian)

`u8 version (1), u8 0, u16 0, i32 refused count, i64 first time, i64 last time, i32 rows, i32 tables, i32 stored length, 16 product id, 16 producer id, i64 producer sequence, i64 arrival ticks`. First and last time are the block header's (the minimum and maximum time of all its rows).

### Sequence mark

Sequences are also used up without a stored block: a ticket skipped by a put that failed, a block whose tables were all refused, the sequence a new table sensor records as its `FirstSequence`. Restored from the last stored key alone, the next sequence would go back after a restart, and a table sensor created meanwhile would have every table refused (`SensorCreatedAfterBlock`) until the sequence passed its `FirstSequence` again; the collector takes a refusal as final, so those rows would be lost.

So before the store hands out a sequence `s` (a `Reserve`, or `NextSequence` read for a new sensor), the file `hsm-sequence-mark` holds a number above `s`. The store writes it ahead in steps of 1,024 (`DurableFile.WriteAtomic`: a temporary file renamed with write-through on Windows, a directory sync elsewhere), so it costs one durable write per 1,024 sequences, about every three hours per collector. The open restores `max(mark, last key + 1)`: a restart skips at most 1,024 sequences and never hands one out twice. The mark lives outside both databases, so an index rebuild keeps it, and a `Blocks` database emptied by the merge (#1850) keeps it. A mark that cannot be written fails the reservation (a fault: nothing is handed out); a mark that cannot be read fails the open with an error naming the file, because a guess could hand out sequences again.

### Sequence floor from the sensors

The mark covers a restart, not the loss of the folder: an operator who deletes `Tables/` (or restores it from an older copy) takes the mark with it, and the sequence would start below the `FirstSequence` of every registered table sensor, whose tables would then all be refused. So the first put through the block service (`TableBlockService.EnsureSequenceFloor`, before its reservation) raises the store's next sequence to the largest `FirstSequence` of the table sensors in the tree (`ITableBlockStore.RaiseNextSequence`: through the mark, so durably, and in order with tickets in flight, which are written first). Once per service; if the raise fails (the store cannot open), that put fails as a fault and the next one tries again. The tree is loaded by then: `TreeValuesCache` loads it in its constructor, and the service is built from the cache; every put goes through the service, so no block is numbered before the floor is in place. A sensor registered before the first put reads the next sequence from the store directly and may get a smaller first sequence, which is harmless: the floor only has to be at or above every first sequence. The service's constructor opens nothing, so a store that cannot open costs table puts only, never the construction of the service or of what it is injected into. Without table sensors nothing is raised.

The service's floor covers a restart. Within one process the store keeps it itself: **an open never takes the next sequence back.** Every open (the first, a reopen after a failure) sets the next sequence to the larger of what the store on disk restores and what it was in memory before the open. In memory it is at or above every sequence handed out in this process and every first sequence of the table sensors (raised to the largest by the service's first put, and read from the store by every sensor created since). So a folder removed or replaced while the server runs (it can only happen while the store is failed and closed; a reopen then creates a fresh store without a mark) does not restart the sequence at 0 under the registered sensors, whose tables would otherwise all be refused (`SensorCreatedAfterBlock`) until the counter caught up. A Warn line names both numbers. This is applied inside the open, under the store's lock, and the open is what the first reservation after it runs, so no reservation comes between the open and the floor; the mark of the new folder is written ahead before the next sequence is handed out, as always. Pinned by `TableBlockServiceTests.A_store_whose_folder_is_removed_while_the_server_runs_keeps_taking_the_sensors_tables` and `TableBlockDatabaseTests.A_reopen_onto_a_removed_folder_never_takes_the_next_sequence_back`.

What a deleted folder costs: the rows of the deleted blocks are gone; the sensors take rows again from the next block, with a Warn line naming the old and the new sequence.

## The order of appends

Several threads put blocks at once. A put reserves a sequence number (`Reserve`) **before** it matches the block's tables against the sensors, and appends (or skips) it afterwards; the store writes tickets strictly in the order they were reserved: an append waits until every smaller ticket is done (appended, skipped, or failed). The wait releases the lock; decompressing and matching a block never happen under it. A ticket must always be finished: the service skips it in a `finally`.

## Duplicates

A block whose `(product id, producer id, producer sequence)` is already stored is not written again; the put answers what the first one did (its stored sequence and refusals). The product is part of the key because one store holds every product's blocks and the producer id is chosen by the client: the same pair under two products is two blocks, and one product can never shadow another's. The in-memory map is rebuilt from the index at open (the envelope and the index entry carry the product; nothing on disk changed), so this holds across restarts, for as long as the block is stored. Once #1850 deletes merged blocks, it needs a per-producer watermark, per product as well (see "What later steps must do").

## Crash and failure

| When | After a restart |
|---|---|
| Before the block's put | Nothing stored, nothing acknowledged; the collector sends the block again; stored once. |
| After the block's put, before its index entry | The open indexes every block after the index's last key again ("tail"), so the block is known; the resend is a duplicate. |
| After both, before the acknowledgement | The same: the resend is a duplicate. |
| An index write failed (the block is stored) | The block is served from memory; `hsm-rebuild-index` makes the next open rebuild the index. |
| The index is missing, damaged, or holds a key beyond the last block | The open rebuilds it from the blocks (`Index rebuilds` counter, a Warn line). |

While the server runs:

| State | Put | Read |
|---|---|---|
| A write failed; the databases are still open | Fault for a minute (the backoff), then the first put reopens | Served as before, from the open databases (a read does not reopen them) |
| A reopen failed; the databases are closed | Fault; inside the backoff with the backoff's message, past it the next put tries to reopen | Fault (`TableBlockStoreException`); past the backoff the read itself tries to reopen, which on a failed store creates nothing. Never an empty result, and no read failure counted per block. A failed open counts in `Write failures` and logs "could not be opened; puts and reads are refused until it opens", whether a put or a read started it |
| A reopen failed, then the folder is missing or cannot be reached (removed, replaced by a file, its volume or parent gone) | Past the backoff: opens; if the folder is really gone it creates a fresh store, whose index and duplicate map are what is on disk, and the next sequence does not go back | The fault, inside the backoff and past it: the reopen a read tries on a failed store creates nothing, so the attempt fails ("missing or cannot be reached"), the backoff starts again, and nothing in memory is dropped. A store cannot tell a removed folder from an unreachable one, so a read never answers "no blocks" for a store that has failed |
| The databases closed during a read (a reopen failed on another thread between the listing and the block reads) | | The read ends as the fault (`ReadBlock` throws), not as rows missing |
| Disposed (shutdown) | Fault | Fault |
| Never opened, no folder | Opens and creates the store | No rows; nothing created |
| Never opened, the folder exists | Opens | Opens (and may create the databases in the folder, as a put would) |

**A store that has failed stays failed until an open succeeds**, whoever tries it. A read creates nothing in two cases: on a store that has failed (its reopen never creates the folder or the `Blocks` database) and on a store without a folder that never opened, which answers "no blocks". A read on a store that never opened and whose folder exists opens it like a put. Only a put or a registration creates a store that is not there.

**A transient file error at an open is retried.** Every open of a LevelDB database writes a new MANIFEST and points `CURRENT` at it through a temporary `<n>.dbtmp` file; on Windows LevelDB moves it over `CURRENT` (`MoveFileA`, then `ReplaceFileA`), which fails while another process holds `CURRENT` open without delete sharing, as a virus scanner or the search indexer does for a moment after a file is written ("IO error: …/000002.dbtmp: Unable to remove the file to be replaced.", or "…being used by another process."). Seen in a full run of the server tests; in production it would cost a minute of faults. The store retries the open of a database when it fails with an IO error that names the temporary `.dbtmp` file (in practice the replace of `CURRENT`), recognised by the wrapper's message whatever the (localized) Windows text, four times with pauses of 10, 30, 60 and 120 ms (220 ms at most per database; one open of the store may open three databases, `Blocks`, the index and the index rebuilt, so about 660 ms at worst, under the store's lock), and logs one Warn line with the count and the error. The retry is `LevelDbOpen` (`src/database/HSMDatabase.LevelDB/LevelDbOpen.cs`), shared with the batch store (#1850). Any other error (corruption, a missing database, another IO error) fails at once; when the retries run out the open fails as the fault it was. The other databases of HSM open through `LevelDBDatabaseAdapter`, which retries any open error up to ten times without a pause.

A failed block write (disk, LevelDB) fails the put with `TableBlockStoreException`, a fault: the put is not acknowledged and the collector keeps the block. The store then refuses puts for a minute (LevelDB keeps a background error until it is reopened) and reopens on the first put after that. Puts are refused the same way while the disk has less free space than `TableSensors.MinFreeDiskMb`. A stored block that cannot be read back (missing from `Blocks`, a damaged envelope, a LevelDB read error) is counted (`Read failures`, every one) and skipped by reads; its Error line is written at most once a minute, with the count of the ones not logged since. That count appears only with the next failure after the minute: a burst followed by nothing leaves one line, and the rest of the burst is in the counter only.

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

**Why a block with every table refused is not stored.** Its bytes would never be read and would cost disk and, later, merge work; the caller learns every refusal from the result. `AllRefused` is a final answer (the wire contract): the collector drops the block, its rows are lost and counted, and its producer sequence is never sent again. What comes back after the sensors are registered are the rows of later blocks. A resend of a refused block is not something to rely on: once a later block of the producer is merged, the producer's watermark answers every sequence at or below it as a duplicate (#1850), so such a resend would be acknowledged without being stored. A block with at least one accepted table is stored with its refusals, and a resend of it is a duplicate: a refused table stays refused, as ADR-0021 point 8 says.

## A sensor created during a put

A put reserves its sequence before it matches its tables; a new table sensor records, before it can be seen, the sequence the next reservation will get as its `FirstSequence` (`TreeValuesCache.TableBlockSequence`, which reads `ITableBlockStore.NextSequence`). Two rules follow:

- a read of a sensor takes only blocks with a sequence at or above its `FirstSequence`;
- a put refuses a table whose sensor's `FirstSequence` is above the block's sequence (`SensorCreatedAfterBlock`).

So a block numbered before a sensor existed is never read for it: its tables were matched against an earlier sensor of the same path, or against none. And every table a put accepts for a sensor is in a block that sensor reads. A sensor is removed before a new one is created on its path (the product's queue), and a removed sensor is never read. Pinned by tests that remove and create the sensor between the reservation and the match.

## The read (`ITableBlockService.Read`)

`TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null)`: the rows of a table sensor with a time in `[from, to)`. Times are UTC: a time of kind `Unspecified` is taken as UTC (as row times are), a `Local` one is converted.

**Since the merge (#1850 part 3) the read joins batches and blocks**: an arrival hour committed for the sensor's product is read from its batches only, any other from its blocks only, never both and never neither; the order and the early stop of the row limit are in `aicontext/features/storage/table-batches/feature.md`, section "The read". What follows is the part of the read that takes blocks.

- **Finding blocks.** From the in-memory index of the sensor's root product: blocks with a sequence at or above the sensor's `FirstSequence` whose header range `[first time, last time]` meets `[from, to)`, in an arrival hour not committed by the merge. A late block with old times is found by its header, wherever it is in the sequence.
- **Rows.** Each candidate is read (one LevelDB get), its table read out by identifier (`TableBlockReader.ReadTable`, which decompresses the block), skipped when the envelope marks it refused, and its rows filtered to the range.
- **Order.** By time, then by arrival hour, then by block sequence, then by the row's position in its block: a stable sort of the rows in the order they arrived (an hour from batches keeps the same order).
- **Limit.** At most `maxRows` rows (capped by `TableSensors.MaxReadRows`, default 1,000,000), the earliest ones, `Truncated` when more matched. Memory stays within about twice the limit: past it, the rows collected so far are sorted and the latest dropped.
- **Where the next page starts (#1845).** The collector keeps one row more than the limit (blocks and batches alike; the early stop of the joined read counts that row too). When more rows matched, the result is cut at a time boundary: the rows of the time of the first row left out are left out too, and `NextFromTicks` is that time, so a read from it returns every row exactly once. When all the kept rows share that one time (a single time with more rows than the limit), the rows are cut at the limit, `RowsAtTimeOmitted` is set and `NextFromTicks` is one tick later.
- **One walker, two readers (#1847).** The read is a plan (the sources and their order, above) and the visit of each source, which hands the rows to a sink: `TableRowCollector` for `Read`, `TableChartReducer` for the chart (`ITableChartReader.Reduce`, `aicontext/features/server/table-sensors-ui/feature.md`). Taking one table out of a block, with the failure counting and logging below, is `TableRowCollector.ReadBlockTable`, shared by both. The plan reads the batch store's generation of the product (`ITableBatchStore.GetGeneration`), lists the merged hours, then the blocks, asks whether each hour of a candidate block is committed, and reads the generation again; it is stale, and taken again, when the two readings differ (an hour committed, also one without any batch class, or a week dropped during the plan). `Read` never reads a stale plan: after three it throws `TableReadChangedException` (the read route answers 503 with `Retry-After: 1`). `Read` holds the hour gate for its whole range; the chart holds it per source and plans again when a source of blocks it planned has been committed since (the same feature doc, "The hour gate").
- **Cancellation (#1845).** `Read` takes a `CancellationToken`, checked before every block and every merged hour read from its batches: a read whose caller went away stops decompressing and throws `OperationCanceledException`. `TableBlockService.BeforeBlockRead` is the test seam inside that loop.
- **Null cells (#1859).** Every column of `TableRows` but the time may have rows without a value: `TableRowsColumn.NullMap` (the bit order of `TableNullMap`, `ceil(RowCount / 8)` bytes), `HasNulls`, `IsNull(row)`; the value array holds 0 at a null row. The collector keeps, per value column, one flag per collected row, created at the first null row of that column (nothing for a column without one), and moves it with its row through the range filter, the stable sort, the trims and the limit, so a column with a map in one block and none in another is right. `NullMap` is null when no returned row of the column is null, also when a block had a map whose null rows fell outside `[from, to)` or past the limit. A row whose every value is null is returned like any other. The put needs nothing new: `ReadDirectory` gives the plain column types, which pass the schema check, and it refuses a malformed map (`Invalid`, `CorruptNullMap`).
- **Damage.** A block that is missing or cannot be read is counted (`Read failures`, `BlocksFailed` in the result) and skipped. A batch of a merged hour that cannot be decoded, or whose columns are not the sensor's, is counted the same way on the batch side (`Batch read failures`, `BatchesFailed` in the result) and logged as an Error; the read route passes both figures on (`X-Hsm-Blocks-Failed`, `X-Hsm-Batches-Failed`).
- **A store that cannot be used.** `GetBlocks` is lock-free while the databases are open, also after a failed write, once the first open of the process has finished: during that open it waits for it on the store's lock (the index in memory is still the empty initial one), never answering no blocks. When they are closed (a reopen that failed, disposed) it goes through the same open as a put: inside the backoff it throws the fault, past it the read reopens the store (so a server that only reads recovers too), disposed it throws. It never answers the in-memory index of closed databases, whose blocks would all read as missing. `ReadBlock` tells a closed store (it throws `TableBlockStoreException`) from a missing block (damage, counted). `TableRowCollector` and `Read` do not catch the exception, so a read whose store closed under it ends as the fault, and the caller answers 5xx, not 200 with no rows.

## The identifier is the registered path, byte for byte

Registration normalizes a sensor path (`BaseUpdateRequest`: a leading separator, empty segments and the spaces around segments are dropped), and the sensor's `Path` is that normalized form; the tree's path index is keyed by it (`AddSensorToCache`), ordinally. A put looks a block's identifier up in that index exactly, without normalizing it, and a read looks for the sensor's `Path`. So a table is only ever accepted under the exact bytes a read looks for: nothing is stored under an identifier no read would find. A collector that sends the raw form it registered (`/a/ b`) gets `UnknownSensor` for it, a refusal it is told about; step 3 must send the normalized path (`a/b`). Pinned by `A_block_is_taken_only_under_the_exact_path_the_registration_stored`.

## Configuration

Section `TableSensors` of `Config/appsettings.json` (`TableSensorsConfig`), read once at startup, written back with its defaults, validated with an error that names the key (`TableSensors.MaxReadRows = 0 is invalid: expected 1..2147483646.`):

| Key | Default | Meaning |
|---|---|---|
| `MaxDecompressedBytes` | 67,108,864 (64 MiB) | A block's limits on a put (`TableBlockLimits`); the format's maxima bound them. A stored block is read back with the format's maxima, so lowering a limit later does not hide it. |
| `MaxTables` | 65,536 | Any value the format allows: the envelope's refusal count is 32-bit. |
| `MaxRows` | 8,388,608 | |
| `MaxColumnsPerTable` | 64 | |
| `MaxTableIdLength` | 1,024 | |
| `MaxReadRows` | 1,000,000 | The most rows one read returns (ten million rows of five columns would be 400 MB in one request); at most 2,147,483,646, since a read collects one row past its limit to know where the next page starts. |
| `MinFreeDiskMb` | 1,024 | Puts are refused (a fault) while the store's disk has less free; 0 turns the check off. The merge does not write batches below it either (#1850). |
| `MaxOpenBatchDatabases`, `DeleteMergedBlocksAfterHours`, `MergeMemoryBudgetMb` | 32, 24, 256 | The batch store and the merge (#1850): `aicontext/features/storage/table-batches/feature.md`. |
| `MaxChartScanRows` | 10,000,000 | The rows of its range a chart reads before it stops (#1847): `aicontext/features/server/table-sensors-ui/feature.md`. |

There is no switch: the feature does nothing until a table sensor exists.

## Self-monitoring

Under `Database/Tables` (`TableBlockSensors`), posted from the 5-minute self-monitoring sample, **only once the store has been opened in this process**: a server without table sensors creates none of these sensors. Counters since the process started, posted when they change: `Blocks stored`, `Rows stored`, `Stored` (MB), `Blocks in store`, `Duplicate blocks`, `Duplicates by watermark` (of them, those recognised by a producer's watermark after their hour was merged, #1850), `Tables refused`, `Blocks not stored` (every table refused), `Invalid blocks`, `Write failures`, `Read failures`, `Index rebuilds`. `Write failures` also counts a failed open of the store, whether a put or a read started it (the store cannot take puts until it opens), so on a server where only reads arrive it can grow about once a minute with no put attempted. `Invalid blocks`, `Write failures` and `Read failures` are Error in the sample in which they grew, and Ok again at the next sample without a new failure (posted once more with the same value), so one transient failure does not stay Error until a restart.

The daily database sizes get `Database/Tables data size` once the folder exists. `IDatabaseCore.TableBlocksDbSize` is the folder's size (0 without the folder; nothing is opened); `TotalDbSize` includes it. The Configuration page shows it read-only as "Table blocks (MB)". The weekly export list, the Unseal button and the Compact button do not concern it.

## Log lines

- Open: `Table block store <folder> opened in <ms> ms: <n> blocks, next sequence <s>[, index rebuilt from <n> blocks | , <n> blocks indexed again after the last index entry]` (Info); a missing or damaged index, a reopen after a failure (Warn).
- A refused table: Debug per table; a Warn summary of the reasons at most once a minute.
- An open of a database that needed retries of a transient file error: Warn, `<name> opened after <n> retries of a transient file error: <error>`, or `could not be opened after <n> retries …; last error: …` before the open fails.
- A failed write or too little disk: Error, at most once a minute. A failed open (started by a put or a read) says that puts and reads are refused until the store opens.
- A stored block that cannot be read: Error, at most once a minute, with the count of the ones not logged since; that count is written only with the next failure after the minute, so a burst followed by nothing leaves one line (every failure is counted in `Read failures`).

## Memory

The in-memory index holds one entry per stored block (sequence, hour, first and last time, rows, tables, stored length, refused count, product, producer, producer sequence, arrival: 96 bytes) in a per-product array, and the duplicate map one entry per block (the producer pair and the entry). Measured: **450 bytes of managed memory per stored block**, so **3.8 MB for one collector-day (8,640 blocks)**. Until the merge (#1850) deletes merged hours, this grows with every block stored: about 27 MB per collector-week. After #1850 it holds only the hours not merged yet.

## Measured (2026-11-24, `TableBlockStoreBenchmark`, Release, Windows, one NVMe machine)

Synthetic feed shaped like the measured real hour: one collector, a block every 10 seconds, 1,000 tables, about 6,700 rows per block with a skewed share per table, columns time, bid, ask from a random walk, generated with the block writer (no real data). Its noise compresses worse than real quotes (13.5 block bytes per row against 3.8 on the real hour), so the blocks are larger (88.5 KB) and every byte figure is pessimistic; the ratios are what matter. Seven day-equivalents (60,480 blocks, 5.2 GB stored). After every hour the hour before the previous one is deleted as one range and that range compacted, which is what #1850 will do; so about two hours stay live.

| | One database | One database per day-equivalent (7) |
|---|---|---|
| Bytes the process wrote / bytes stored, deletes included | 2.23 | 2.22 |
| Real compactions in `LOG` during ingest | 0 | 0 |
| Real compactions during the 166 range deletes (their total time) | 497 (11.8 s) | 491 (13.8 s) |
| Synced put latency, median / p99 / max | 2.3 / 5.4 / 183 ms | 2.9 / 7.6 / 121 ms |
| Open of the last database (index load) | 35 ms (720 blocks) | 29 ms (360 blocks) |
| One table for one hour (360 blocks decompressed, 6,837 rows) | 163 ms | 77 ms |

- Ingest alone, one collector-day without deletes: **1.99×** the stored bytes written, 0 real compactions; opening it (index of 8,640 blocks): 61 ms.
- The read of one table for one hour varies between runs on the same blocks (77–163 ms here, 109–115 ms in a one-day run): it is the decompression of 360 blocks of 88.5 KB. The target is 300 ms.
- The real compactions all come from the hourly range deletes, which LevelDB has to merge with the files below them; about 70 ms per hour of 360 blocks.

**Decision: one database.** The split changes nothing that matters: the same bytes written, no compaction during ingest in either, the same work for the hourly deletes; it only adds databases, open handles and a rule for which one a block goes to. With the merge deleting each hour after it is packed, the one database holds about two hours of blocks, and nothing in it ever needs to be dropped as a whole.

## What later steps must do

**Step 3 (#1845), the transport** (done: `aicontext/features/api/wire-contract/feature.md`, "Table connection"; `TableBlockPutResult.RefusalsKnown` tells a duplicate whose refusals cannot be told, set today when the stored envelope cannot be read, and by #1850 for a block merged and deleted):
- Send a **producer id** that a collector keeps across restarts (with its spool of undelivered blocks) and its own **producer sequence** per block; pass `productId` as the root product of the key that sent the block. Deliver in order per producer: a block is sent after the previous one was acknowledged, or the resends after a restart keep the order (needed for the watermark below).
- Answer a `TableBlockPutResult` per block: `Stored` and `Duplicate` are acknowledgements (with the refused tables), `AllRefused` and `Invalid` are final refusals (4xx-like: the collector drops the block and never sends its producer sequence again; rows of later blocks are taken once the sensors are registered), and `TableBlockStoreException` is a fault (5xx-like: the collector keeps the block and sends it again). `Read` throws the same exception when the store cannot be used (cannot open, failed and in its backoff): a fault, answered 5xx, not an error result, which would say the request is wrong.
- Check the key's permission to send data before calling `Put`; the block service does not see keys.

**The merge step (#1850):** built: the block store's part (`CloseHour`, `DeleteMergedHour`, watermarks in memory, `GetProducts`) and the merge job with the joined read (part 3); `aicontext/features/storage/table-batches/feature.md`, sections "The block store's part" and "The merge". The list below is what it had to do, kept for the record.
- After an hour is merged, delete it from both databases as one range and compact that range (`TableBlockDatabase.DeleteArrivalHour` does this today, for the benchmark), with the blocks and index entries leaving the in-memory index and the duplicate map.
- Keep **duplicate detection** working after the blocks are gone: a per-producer watermark per product (the highest producer sequence merged for a product and producer, the same key as the duplicate map), persisted with the batches, so that a resend of a merged block is answered as a duplicate. This relies on in-order delivery per producer (step 3).
- The sequence is kept by the sequence mark (above): deleting every block does not take the next sequence back.
- Serve reads from batches and blocks joined by time, with the same order rules (time, then arrival, then row).

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.AccessManager/TableBlocks/ITableBlockStore.cs` | The store's interface, index entry, ticket, statistics |
| `src/database/HSMDatabase.AccessManager/TableBlocks/TableBlockEnvelope.cs` | The envelope and the refusal reasons |
| `src/database/HSMDatabase.LevelDB/TableBlocks/TableBlockDatabase.cs` | The store |
| `src/server/HSMServer.Core/TableSensors/TableBlockService.cs` | The put and the read; the rule for a sensor created during a put |
| `src/server/HSMServer.Core/TableSensors/TableRowCollector.cs` | Collects, filters and sorts the rows of one table; reads one table out of a stored block for every reader |
| `src/server/HSMServer.Core/TableSensors/ITableRowSink.cs` | What the read hands rows to: the collector or the chart's reducer (#1847) |
| `src/server/HSMServer.Core/TableSensors/TableBlockResults.cs` | Options, results, rows |
| `src/server/HSMServer/ServerConfiguration/Sections/TableSensorsConfig.cs` | The configuration section |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/Database/TableBlockSensors.cs` | Self-monitoring |
| `src/tests/HSMServer.Core.Tests/TableSensors/TableBlockStoreBenchmark.cs` | The measurement (`HSM_TABLE_STORE_BENCH=1`) |

## Tests

`src/tests/HSMDatabase.LevelDB.Tests/TableBlockTests/TableBlockDatabaseTests.cs`: nothing created before first use; key order and a restart; a block read back unchanged with its envelope; duplicates (also after a restart, and per producer); a block not to be stored; a crash between the block and its index entry, then reopen and resend; a deleted and a damaged index rebuilt; a clock stepping back; tickets waiting for earlier ones and a skipped ticket; eight threads putting while a reader reads; the disk guard; the sequence mark (sequences used without a stored block are not handed out again after a restart, a first sequence read before any block, the mark through an index rebuild and an emptied database, writes in steps, a damaged mark); an envelope with 70,000 refused tables; raising the next sequence durably with a ticket in flight; the `LOG` of both databases after 5,000 puts through 16 KB memtables shows flushes and no real compaction, and the same detector sees real compactions when keys come in random order; the open retries of a transient failure to replace `CURRENT` (`An_open_...`: a seam that fails twice and then opens, one that fails past the retries and leaves the store failed, another error not retried, and the real error, made by holding `CURRENT` open without delete sharing, cured by the retry; Windows only, since on Linux `rename(2)` replaces an open file and the error cannot be made).

`src/tests/HSMServer.Core.Tests/TableSensors/TableBlockServiceTests.cs`: put and read across hour boundaries with a late block and overlapping blocks, equal times in arrival order, `[from, to)` edges; the row limit; null cells across blocks with and without a map, a range that leaves every null out (no map), equal times of two blocks with a null in the earlier one only, the row limit cutting exactly at a null row (kept last, and dropped first) through the trims while collecting, a block with nulls passing the put's type check and one with a malformed map refused there; a refused table inside an accepted block for every reason and a sensor unblocked afterwards; the schema id and an identifier that is not UTF-8; a block with every table refused, then its resend after the registration; a duplicate; an invalid block and one beyond the limits as results (the store is not even opened); the disk guard as a fault; a removed sensor and a new one on its path; a sensor created between the reservation and the match (both directions); six producers putting while a reader reads; a server without table sensors creates no `Tables` folder; unspecified and local times of a read; a service without a store refused at construction; the `Tables` folder deleted under a registered sensor, then a restart: its first block is stored and read; a store that cannot open (a damaged mark) under a registered sensor: the service is built, a put is a fault, and the same service stores the block once the mark is repaired; building the service with table sensors creates nothing on disk; the identifier must be the registered path byte for byte.

`src/tests/HSMServer.Core.Tests/BackgroundServices/TableBlockOperationsTests.cs`: the configuration's defaults and an error naming the key for each value out of range; no self-monitoring sensor and no value before the store is opened, counters posted when they change, failures as errors; the size sensor appears once the store exists. `TableBlockDatabaseTests.Deleting_an_arrival_hour_...`: the hour delete of #1850.

After the review of PR #1857: duplicates per product (`TableBlockDatabaseTests.Duplicates_are_per_product_so_one_product_cannot_shadow_another`), a reader during a reopen sees the whole old index (`A_reader_during_a_reopen_sees_the_whole_old_index_never_a_cleared_one`), the store opened outside the tree lock (`TableBlockServiceTests.The_store_is_opened_before_the_tree_lock_when_a_table_sensor_is_registered`), a read of a store that cannot open throws the store fault, and failure counters back to Ok after a sample without growth.

After the second review of PR #1857: a read after a reopen that failed is the fault, without a read failure per block, and the same store (`TableBlockDatabaseTests.A_read_after_a_failed_reopen_is_the_store_fault_and_the_same_store_reads_once_it_reopens`) and the same service (`TableBlockServiceTests.A_read_after_a_failed_reopen_throws_the_store_fault_and_the_same_service_reads_once_the_store_reopens`) read the rows once the mark is repaired and the backoff passed; a block read after the store closed since its listing is the fault (`A_block_read_after_the_store_closed_since_its_index_was_taken_is_the_store_fault`); a read from a disposed store is the fault (`A_read_from_a_disposed_store_is_the_store_fault`, `TableBlockServiceTests.A_read_from_a_disposed_store_throws_the_store_fault`); a store whose write failed keeps serving reads while its databases are open; every missing block is counted; `PeekNextSequence` does not wait for an append holding the store's lock when the mark covers the value, still refuses a failed or disposed store, and writes the mark ahead when it must (`Peeking_the_next_sequence_...`); a read during the first open waits for the index (`A_read_during_the_first_open_...`); a failed store whose folder is removed answers the fault to a read inside its backoff and past it, keeps its index and creates nothing, and only a put creates a fresh store, whose block is not a duplicate of a removed one and whose sequence does not go back (`A_failed_store_whose_folder_is_removed_...`, `Past_the_backoff_a_failed_store_whose_folder_is_removed_...`); a folder replaced by a file and a missing parent folder are the fault to a read past the backoff, every time (`Past_the_backoff_a_failed_store_whose_folder_is_a_file_...`, `..._whose_parent_folder_is_missing_...`).
