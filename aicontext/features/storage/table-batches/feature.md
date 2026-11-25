# Feature: Table batches (format and store)

> Owner: storage | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: the byte format of a stored batch of a table sensor (#1850, part 1; ADR-0021 points 9 and 11): the header, the payload, the column streams, the rule that chooses a stream, the exception list, nulls, versioning, and the codec `TableBatchCodec` that writes and reads it. And the batch store (#1850, part 2; ADR-0021 points 10 and 12): where batches live, how a merged hour is committed, the week databases and their bound, and what the block store gives the merge. And the merge job with the read that joins batches with blocks (#1850, part 3). Retention and the move between period classes are part 4. The block a collector sends: `aicontext/features/api/table-block/feature.md`. The block store: `aicontext/features/storage/table-blocks/feature.md`.

---

## Overview

A **batch** (glossary) holds 1 to 4,096 rows of one table sensor after the hourly merge: its columns, coded with the integer and double coders of sealed history (ADR-0020) and compressed with zstd 15. It is internal to the server: nothing outside it reads or writes a batch, so its format can change with a server release under the versioning rules below.

`TableBatchCodec` (`src/database/HSMDatabase.SealedHistory/TableBatchCodec.cs`) is a pure codec next to `ChunkCodec`: no storage, no shared state, safe from any thread. It takes a `TableBatch` (a schema id and columns, the first the time) and returns bytes; it decodes bytes back into a `TableBatch` bit for bit, or throws `TableBatchFormatException` with a `TableBatchFormatError`.

## Invariants

- **Fidelity.** Every value reads back bit for bit: time ticks and 64-bit integers, doubles by their IEEE 754 bits (NaN payloads, ±0.0, ±Infinity). Every null reads back as null; a null cell decodes as 0.
- **Order as given.** Rows decode in the order they were encoded; the codec neither sorts nor requires time order (the merge sorts, stably, before it encodes). The header holds the minimum and the maximum time.
- **Nulls cost the coders nothing.** A null cell is left out of its column's stream (the stream holds the present values only); which rows are null is the column's presence map.
- **An outlier does not cost a double column its decimal mode.** Up to `max(1, n / 256)` values of the column's n present values that are not decimals of the column's exponent are set aside as exceptions.
- **Never larger than raw by much.** Each column is written as the rule's coded stream or as raw 8-byte values, whichever is smaller after zstd at level 1; the whole payload is one zstd 15 frame, or stored as it is when that is not smaller.
- **Fail loudly, never partially.** A damaged batch is refused with a named error; the reader never returns an empty or partial batch. Every size the reader allocates is bounded by the header's row and column counts (at most 4,096 × 255), and a header that declares more payload than those counts allow is refused before anything is allocated.
- **The sealed coders' output for sealed chunks does not change.** The batch codec uses two additive helpers (`IntColumn.WriteVarint`, `DoubleColumn.WriteDecimal`) and an overload of `DoubleColumn.TryFindDecimalExponent` with exceptions; `IntColumn.Write`, `DoubleColumn.Write` and every sealed golden chunk are unchanged.

## API

| Contract | Notes |
|---|---|
| `TableBatchCodec.Encode(TableBatch, TableBatchEncodeStats = null) -> byte[]` | `ArgumentException` for input outside the rules: 1..4,096 rows, 1..255 columns, the first of type time with no null row, every column as long as the first with the values array of its type (`Int64Values` for time and int64, `DoubleValues` for double), every null map `TableNullMap.ByteLength(rows)` bytes with zero padding. The value under a null is ignored. |
| `TableBatchCodec.Decode(ReadOnlySpan<byte>) -> TableBatch` | Columns of the encoded types; `NullMap` null for a column without a null row. |
| `TableBatchCodec.ReadHeader(ReadOnlySpan<byte>) -> TableBatchHeader` | Verifies magic, version, lengths, checksum, header fields and the zstd frame's shape; does not decompress. |
| `TableBatchColumn`, `TableBatch`, `TableBatchHeader`, `TableBatchEncodeStats`, `TableBatchFormatException` / `TableBatchFormatError` | `src/database/HSMDatabase.SealedHistory/TableBatch.cs`. The column types and the null map bit order are those of the block format (`HSMTableBlocks.TableColumnType`, `TableNullMap`). |
| `TableBatchEncodeStats` | What the encoder chose, summed over the batches given: streams written coded or raw per kind of column (time, int64, double), exception cells, double columns that kept the decimal mode thanks to exceptions or have none, null cells, batches, rows, bytes. For the merge's self-monitoring and for measurements. Not thread-safe. The decimal counters describe what was stored: a column stored raw counts as raw only; `DecimalKeptByExceptions` and `ExceptionCells` count a column written by the decimal writer after its exceptions (not one whose values left are constant); `DecimalGivenUp` counts a column stored without the decimal mode because the exceptions did not fit, coded by the double coder or raw. |

## Byte layout (format version 1)

All integers are little-endian.

### Header (48 bytes)

| Offset | Size | Field | Rule |
|---|---|---|---|
| 0 | 2 | magic | ASCII `HT` |
| 2 | 1 | format version | `1`; the same position in every version |
| 3 | 1 | encoder revision | any value; readers ignore it |
| 4 | 1 | flags | bit 0: the payload is one zstd frame; the other bits zero |
| 5 | 3 | reserved | zero |
| 8 | 4 | schema id (u32) | the table sensor's schema id (`TableSchema.Id`); opaque to the codec |
| 12 | 4 | row count (i32) | 1..4,096 |
| 16 | 1 | column count | 1..255 |
| 17 | 3 | reserved | zero |
| 20 | 4 | payload length (i32) | the decompressed payload; `columns ≤ length ≤ columns × (rows × 16 + 1,024)`; equal to the stored length when not compressed |
| 24 | 4 | stored length (i32) | ≥ 1; the batch is exactly `48 + stored length` bytes |
| 28 | 8 | first time | the minimum of the time column; equal to the last time for one row |
| 36 | 8 | last time | the maximum of the time column |
| 44 | 4 | checksum | CRC-32C (Castagnoli), as in sealed chunks: over bytes 0..43, then the stored bytes |

CRC-32C and not the CRC-32 of blocks: a batch never leaves the server, so the constraint that chose CRC-32 for blocks (the collector's target frameworks) does not apply, and the hardware CRC-32C of `ChunkCodec` is reused.

When flag bit 0 is set, the stored bytes are exactly one zstd frame (not skippable) that declares a content size equal to the payload length; it is checked before decompression. The encoder compresses at level 15 and stores the payload as it is when the frame is not smaller.

### Payload

```
types        column count bytes: 1 time, 2 int64, 3 double; the first is 1
column 0     an integer stream of row count values (the time; never null)
column c ≥ 1:
  presence   the sealed Presence: 0 every row present, 1 no row present,
             2 then ceil(rows / 8) bytes, bit i (byte i / 8, bit i % 8) set = row i present; padding bits zero
  if at least one row is present (n present values):
    time or int64 column: an integer stream of n values
    double column:
      exceptions  varint count k, 0 ≤ k ≤ max(1, n / 256), k < n;
                  then k times: varint delta of the value's index among the n present values
                  (the first absolute, then strictly ascending: every later delta ≥ 1), 8 bytes of the value's bits
      a double stream of the n − k values that are not exceptions, in order
```

The payload ends right after the last column. Presence is the inverse of a null map: a bit set means a value.

### Streams

Every stream starts with an encoding byte:

| Byte | Encoding | Allowed for | Body |
|---|---|---|---|
| 0 | raw | every column | the values, 8 bytes each (a double by its bits); a double column's exception count must be 0 |
| 1 | integer coder | time and int64 columns | an `IntColumn` stream (sealed format: mode 0 constant, 1 varints of residuals of order 0..2, 2 bit buckets) |
| 2 | double coder | double columns | a `DoubleColumn` stream (sealed format: mode 0 constant, 1 XOR bytes, 2 decimal with an `IntColumn` stream of mantissas, 3 Gorilla) |

The integer and double streams are byte for byte the layouts of `aicontext/features/storage/sealed-history/feature.md`, read by the same `IntColumn.Read` and `DoubleColumn.Read`.

## The rule (encoder revision 1)

How the encoder chooses, per column. A reader does not depend on any of it: any stream the table above allows decodes.

- **Time and int64:** the coded stream is mode 0 (one constant) when every value is equal, otherwise mode 1 with order 1 (varints of the first difference).
- **Double:**
  1. Every value equal: the double coder's own choice (mode 0, one constant).
  2. Every value a decimal of 0..15 digits (`DoubleColumn.TryFindDecimalExponent`): mode 2 with the smallest such exponent, the mantissas as one constant or varints of their first difference. No exception.
  3. Otherwise, the smallest exponent for which at most `max(1, n / 256)` values do not fit, and fewer than n: those values are exceptions, and the rest are coded as in 2 with the smallest exponent they all fit.
  4. Otherwise (the decimal mode is given up): no exception, the double coder's own choice among XOR bytes and Gorilla.
- **Raw or coded:** the column's coded form (the exception list and the coded stream) and its raw form (exception count 0 and the raw stream) are each compressed alone with zstd level 1; raw is written only when it is strictly smaller.

Why a fixed rule and not a choice among every candidate after zstd (measured, #1850 phase A, synthetic quotes hour): the coders as they are choose their mode by size before zstd and give 2.75 bytes per row; compressing every candidate at zstd 15 and keeping the smallest gives 2.195, but costs 4.5 µs per row; making the same choice at zstd 1 or 3 costs 0.8–0.9 µs per row and gives only 2.35, because at those levels the XOR and Gorilla streams look smaller than decimal mantissas as varints while at level 15 the varints win. The fixed rule gives 2.193, as good as the exhaustive choice, and with the comparison against raw it costs 0.45–0.58 µs per row (`TableBatchBenchmark`). The time column goes from 2.18 to 1.78 bytes per row (varints of differences instead of bit buckets), as on the real hour (17–32%). One zstd frame for the whole payload is smaller than one frame per column (2.193 against 2.303). A once-a-day exhaustive check per sensor was considered and not built; it would be a new encoder revision.

Measured with outliers (0.05% of ask values outside any decimal): the coders as they are 3.25 bytes per row, the rule with exceptions 2.21 (the clean hour: 2.20). Nulls: 0.05% of cells +0.4%, 5% of cells +13%, mostly the presence maps themselves.

## Errors

| `TableBatchFormatError` | When |
|---|---|
| `NotABatch` | the first two bytes are not `HT` |
| `UnknownVersion` | the format version is not 1 |
| `Truncated` | shorter than the magic, the version, the header, or the declared stored length |
| `ChecksumMismatch` | the CRC-32C does not match |
| `LengthMismatch` | a stored length below 1; bytes after the stored payload; a payload length outside its bound or, uncompressed, not the stored length; a decompressed payload of another length; bytes after the last column |
| `CorruptHeader` | unknown flags, reserved bytes not zero, a row count outside 1..4,096, no column, first time after last time (or not equal for one row) |
| `CorruptFrame` | the stored payload is not exactly one zstd frame of the declared content size, or it does not decompress |
| `CorruptStream` | an unknown column type, a first column that is not the time, an unknown encoding or one not allowed for the column, a stream cut short, a bad mode, varint or presence map (any refusal of the sealed coders) |
| `CountMismatch` | an exception list longer than allowed or than the column, out of order or beyond the column, or beside a raw stream |
| `HeaderMismatch` | the decoded time column does not have the header's first and last time |

## Versioning

As ADR-0020 point 12 for sealed chunks:

- **A new format version** for any change a reader of the current version would decode differently or refuse: a new encoding or column type, a header of another layout, another meaning of a field, another checksum or compressor, a change of a limit (4,096 rows, 255 columns, the exception bound, the payload bound). A reader refuses an unknown version and decodes every older one forever.
- **A new encoder revision** for different bytes of the same rows that a current reader decodes: another rule, other zstd levels (15 for the frame and 1 for the choice are pinned by a test), a zstd library whose output changes. The revision records which encoder wrote a batch; readers ignore it.
- **Golden batches** (`src/tests/HSMDatabase.SealedHistory.Tests/GoldenTableBatches/format{F}-encoder{R}/*.htbatch`) stay forever as decode tests; the set of the current revision also pins the bytes the encoder writes. `HSM_WRITE_GOLDEN_TABLE_BATCHES=1` writes a missing set and never overwrites one.

## The batch store (#1850, part 2)

`TableBatchDatabase` (`src/database/HSMDatabase.LevelDB/TableBatches/`, behind `ITableBatchStore` in `HSMDatabase.AccessManager/TableBatches/`) keeps the batches of merged hours. `IDatabaseCore.TableBatches` holds the one instance; `DatabaseCore.Dispose` closes it. Nothing is opened or created before the first call that needs a product.

### On disk

| Path | Holds |
|---|---|
| `<Tables>/BatchesV1/` | The store. The version is in the name: an incompatible layout is a new folder, and a server that does not know it ignores it. |
| `<Tables>/BatchesV1/<productId:N>/Hours` | LevelDB, the product's merged hours. Key: the merge hour, ticks, big-endian (8 bytes); value: the hour's record (below). A merged hour is committed by this record. |
| `<Tables>/BatchesV1/<productId:N>/Dropped` | A text file, the product's record of dropped week databases: one name `<yyyyMMdd>_P<weeks>` per line. Written atomically and durably (`DurableFile.WriteAtomic`, strict: a temporary file flushed, renamed with write-through on Windows, the folder synced on Linux) before a dropped database's folder is deleted. Outside `Hours`, so it survives the loss of `Hours`. Absent until the first drop. |
| `<Tables>/BatchesV1/<productId:N>/<yyyyMMdd>_P<weeks>` | LevelDB, one per Monday-aligned week (`StorageWeek`) of the merge hour and per retention period class: `weeks` whole weeks, `P0` kept forever. No LevelDB compression (batches are zstd already), every write synced. |

The database of a batch is (product, week of its merge hour, the sensor's period class at the merge). Outside a move between classes (part 4), the rows of a sensor for a week are in one database, the one of its current class, so a read looks in one database per week.

### Keys of a week database

| Region | Key | Value |
|---|---|---|
| Index | `hour (i64 BE) \| 0x00 \| sensor id (16, big-endian Guid bytes) \| first time (i64 BE) \| ordinal (i32 BE)` | u8 version (1), i64 last time, i32 rows, i32 stored length (17 bytes, little-endian) |
| Batches | `hour \| 0x01 \| ordinal` | the batch (the format above) |
| Mark | `hour \| 0x02` | the hour's record, the same bytes as in `Hours` |
| Moved batches, index | `0xFF \| sensor \| hour \| 0x00 \| first time \| ordinal` | as the index |
| Moved batches | `0xFF \| sensor \| hour \| 0x01 \| ordinal` | the batch |

The ordinal numbers the batches of one hour in one database in the order the merge gives them; a sensor's batches of an hour are in their row order. Every hour key is below `0x2C…` (the ticks of `DateTime.MaxValue` start with `0x2B`), so the region of moved batches, written by a move to another period class (part 4), lies after every hour key: ingest only appends at the end of the hour region and a move only writes into its own region, and neither makes LevelDB rewrite the other. The merge writes hours in order, so the keys of a database only grow.

### The record of a merged hour

Little-endian: u8 version (1), i64 hour, i64 first block sequence, i64 last block sequence, i32 blocks merged, u16 class count, per class (u16 period weeks, i64 minimum row time, i64 maximum row time, i32 batches, i64 rows), i32 count and the i64 sequences of the blocks the merge could not read, i32 count and per producer (16 bytes producer id, i64 sequence) the product's watermarks after the hour (cumulative), i64 the commit time (UTC ticks, the store's clock when `WriteHour` wrote the hour; last in the record). A record that does not decode is a `FormatException` at the open. The commit time is what the deletion delay of the hour's blocks runs from, also across a restart (`TableBatchHour.CommitTicks`). The field was added before the store was released, without a version step. A mark holds the time its part was written; an `Hours` database rebuilt from the marks takes the time of the rebuild as the commit time of every hour it rebuilds (the safe side: their blocks live a little longer).

### Writing an hour

**An hour of a product is merged atomically by its commit.** Its batches may span several databases (one per period class of its sensors), so there is no single write for all of them: each class's part is written with its mark in one synced write of its own, and the hour's record in the product's `Hours` database, written last, is the commit point. Before the record the hour is not merged, whatever parts are on disk; after it the hour is merged. `Hours` is rebuilt from the marks when it is missing or damaged. This also lets a product's merged hours be known at its first use without opening a single week database.

`WriteHour(product, commit, parts)`, one part per period class:

1. If the hour is committed, nothing (a repeat changes nothing). `WriteHour` checks this under the product's lock against the hours in memory, loaded from `Hours` at the open, so a committed hour is never written again.
2. The record is encoded once: every mark this run writes is the same bytes, the commit time included (two runs differ at least in it).
3. Per part, in class order, in the week database of the class: one synced write of the index records, the batches and the mark. When the database already holds a mark of the hour (a run that did not reach the commit), every key of the hour there is deleted in the same write first: the part is **always** written again. Its metadata cannot tell which sensors' rows it holds: two sensors that swapped classes between the runs, one row each at the same time, give parts with the same counts and ranges.
4. Then every week database of the product's week whose class the record does not name and that holds a mark of the hour (a part of a run that did not commit, before a sensor's period changed): the hour's keys (index records, batches, mark) deleted, one synced write each. After the parts, not before: see the table.
5. The record into `Hours`, synced: the hour is merged from here on.

Each part written again (3) or deleted (4) is counted in `PartsRewritten` once its write succeeded (not posted to self-monitoring, like `HoursRebuilt`) and logged as a warning. The region of moved batches is not touched. A database a part is deleted from is pinned as a written one is, so the newest week's databases are all flushed when the next week starts.

So at the commit every mark of the hour is its record, byte for byte, and `Rebuild` takes an hour only when its marks are the same bytes. A crash of a repeat (record R2) over the parts of a run that did not commit (R1):

| Crash | On disk | `Hours` lost: the rebuild | and a class database dropped first |
|---|---|---|---|
| before or during the first part written | R1 parts as they were | as for the crash of R1 | as for the crash of R1 |
| after some parts of R2 | R2 and R1 marks, or R2 marks with a part of R2 missing | not committed (marks differ, or a part missing) | a dropped class counts as found: R2 marks only and all others present: committed with R2's complete parts; an R1 mark left: not committed |
| after every part of R2, during the removals | R2 marks, R1 marks in classes R2 does not name | not committed | dropping every remaining R1 class leaves R2 complete: committed |
| after the removals, before `Hours` | R2 marks only, complete | committed with R2 | committed with R2's classes left |
| after `Hours` | committed | | |

Without the loss of `Hours` the hour is simply not merged until the merge repeats it, and the repeat writes every part again. The old order (removals first) lost rows: R1 in P0 and P1, R2 names P0 only (B moved to P0); the repeat removed P1's part and crashed before P0; P0 kept R1's mark, which names P1; with P1 dropped and `Hours` lost, the rebuild committed R1 with P0's rows of A only, and the deletion of the hour took B's blocks. Residual: two runs whose clocks gave the same tick have equal marks if their metadata is equal too (not seen with the system clock between a crash and a repeat).

Pinned by `TableBatchDatabaseTests`: `Two_sensors_that_swapped_classes_between_the_runs_...`, `A_repeat_with_another_batch_split_...`, `..._another_list_of_unreadable_blocks_...`, `A_sensor_moved_to_another_class_between_the_runs_...`, `A_repeat_equal_to_the_crashed_run_writes_its_parts_again_...`, `A_repeat_that_moved_a_sensor_and_crashed_leaves_no_rebuilt_hour_without_its_rows` (crash after the part, after the removal; the old class dropped, `Hours` lost), `A_repeat_that_crashed_before_removing_the_old_class_part_is_not_rebuilt_...`, `The_database_a_repeat_removed_a_part_from_is_flushed_...`, and for the rebuild `A_rebuild_does_not_commit_an_hour_whose_marks_disagree` and `A_rebuild_does_not_commit_parts_of_two_runs_with_the_same_metadata_and_another_commit_time`. A database closed without a flush replays its log at the next open: `A_crash_at_any_point_of_the_hour_write_...` (after the first of two parts, before the commit, after it) and `A_store_that_stopped_without_closing_its_databases_...`.

### What is in memory

Per product opened: the merged hours with, per class, the minimum and maximum row time (a read selects hours by them: a late block makes an hour reach back), the watermarks (per producer the highest over every commit of the product, whatever order the hours were committed in: an hour merged after a later one may name a producer the later one does not), the week databases on disk. Loaded from `Hours` at the product's first use, without opening a week database. An hour's class whose week database was dropped is left out. If `Hours` is missing, it is rebuilt from the marks of the week databases, visited by week start then class (an hour whose marks are the same bytes, commit time included, and whose mark is in every database its record names; marks that differ are parts of two runs, an hour not committed, left on its blocks; a class the record of dropped databases names for the hour's week counting as found: the hour is restored with the classes left); if its open fails with LevelDB's corruption or a record does not decode, it is moved aside to `Hours.damaged-<ticks>` and rebuilt the same way. An empty `Hours` that exists is the truth: a first hour whose commit was not written is not merged.

### Week databases: the bound, the flush

- The databases of a product's newest week written are pinned. When a later week is written, the databases of the earlier ones are flushed (a `CompactRange` over an empty key range writes the memtable out and merges no file) and unpinned: a closed week has no log to replay when it is opened again.
- Every other database is opened on demand (a read, retention, a rebuild) through one cache for the whole store, at most `MaxOpenDatabases` open (`TableSensors.MaxOpenBatchDatabases`, 32 by default), least recently used closed first. A database is held only while a read or a write uses it; a read of many weeks holds one week at a time. Pinned and held databases are never closed, so the count can be above the bound while they are more than it.
- A database is opened with `MaxOpenFiles` 100.
- Opens go through `LevelDbOpen.Open` (`src/database/HSMDatabase.LevelDB/LevelDbOpen.cs`), the block store's retry made shared: an IO error that names the temporary `.dbtmp` file (in practice the replace of `CURRENT`) is retried four times with pauses of 10, 30, 60 and 120 ms, any other error fails at once; a Warn line per retry.

### Reading

`ReadSensor(product, sensor, period class, hours)`: per week of the hours, the database of that week and class (none: nothing, nothing created); per hour one seek to `hour | 0x00 | sensor`, then one seek into the region of moved batches of the sensor; each index record with its batch. In hour order, then the hour region before the moved region, then by ordinal. An index record that does not decode or whose batch is missing is counted (`ReadFailures`) and skipped.

### Dropping a week

`DropWeek(product, week)`, under the product's lock:

1. Marks the database as being dropped in the cache: from then on a read finds nothing there and does not open it again, and a write into it is a fault. Waits for the reads that hold it (looking the handle up again after every wait: a read's release may have closed it over the bound) and closes it.
2. Adds the database to the record of dropped databases (`Dropped`), durably, before anything is deleted. Then deletes its folder.
3. The hours of that week lose the class; an hour left with none is deleted from `Hours`. The mark of step 1 is cleared.

Why the record: the mark of an hour in the databases of its other classes still names the dropped class. Without the record a rebuild of `Hours` could not tell a class retention dropped after the commit from a part a crash left unwritten, and an hour of a forever class would be lost once a shorter class of its week was dropped. With it, a rebuild counts a recorded class as found; a part never written (no record) still keeps its hour out.

- **A write into a dropped database is refused** (`TableBatchStoreException`, counted in `Batch write failures`), by `WriteHour` before anything is written and by `WriteMoved`: its period has passed, and a database created again would make the record wrong. The merge must leave out the part of a class whose period has passed for the hour's week (the rule retention drops by).
- **A delete that fails** (a file held open, a permission) throws `TableBatchStoreException` with the week dropped all the same: gone from `GetWeeks` and from the hours' classes, absent to reads, the folder left on disk. The next open of the product deletes a folder the record names (also one a crash left between the record and the delete); if that fails too, the folder stays absent until the open after. A record that cannot be written fails the drop before anything is deleted: the week stays listed and readable.
- **A removal of hours that fails** (the write of step 3 into `Hours`) throws `TableBatchStoreException`, counted in `Batch write failures`, with the week dropped all the same: the hours left with no class stay in `Hours` and in memory, hidden from `GetHours` and the reads as any hour whose classes were all dropped, and the next open loads them the same way. Pinned by `A_drop_whose_removal_of_hours_fails_...`.
- **Bounded:** an entry stays while a database of its week is left (a mark there may name it) or its own folder is still on disk; the others go when the record is next written. It holds at most the dropped classes of the weeks still on disk: a few lines a week for a product with a forever class, nothing once a week's last database is dropped and the record written again.

### Closing the store

`Dispose` refuses every operation that starts after it (`TableBatchStoreException`, "closed"), then closes each week database once the reads and writes that hold it let it go, and each product's `Hours` under the product's lock, so after a write in flight. Closing a LevelDB database under a live iterator or a running write would free it under native code. No timer bounds the wait: an operation in flight ends. A write in flight that needs another week database after the close began fails with the "closed" fault and leaves its hour not committed.

### The block store's part

- `CloseHour(hour)`: no block is appended to that hour or an earlier one afterwards, also with the clock behind (held in memory: the merge closes its newest hour again after a restart).
- `DeleteMergedHour(hour, keep, watermarks)`: under the append lock, raises the watermarks, deletes the hour's blocks except `keep` (blocks the merge could not read) from `Blocks` (synced) and then `BlockIndex` (a repeat removes index entries left by a crash between the two), and drops them from the in-memory index and the duplicate map; then compacts the range outside the append lock.
- Watermarks per product and producer, in memory (`RaiseWatermarks`, `GetWatermark`): an append at or below its producer's watermark is a duplicate with `Merged`, which the block service answers as `Duplicate` with `RefusalsKnown` false, no sequence and no refusals (`RefusalsKnown` is true by default and false also for a duplicate whose stored envelope cannot be read; the same shape as the wire step's). The durable watermarks are in the records of the merged hours (`GetWatermarks`); the merge raises them before the first put after a start (part 3).
- `TableSensors.DeleteMergedBlocksAfterHours` (24 by default, 0..8,760): how long the blocks of a merged hour stay after its commit; reads of a merged hour take the batches and ignore them (part 3).

### Self-monitoring

Under `Database/Tables`, once a product of the batch store was opened in this process: `Batch databases open`, `Batch week databases`, `Merged hours` (product hours committed), `Batch write failures` and `Batch read failures` (Error in the sample in which they grew), and the merge's sensors (section "Self-monitoring of the merge").

### Measured (2026-11-25, `TableBatchStoreBenchmark`)

Windows, .NET 8.0.28, Release, one NVMe machine shared with other work (timings approximate). The feed is the synthetic quotes hour of `TableBatchBenchmark`, encoded with the codec, cut to every fourth table (`HSM_TABLE_BATCH_STORE_BENCH_SHARE`, 4 by default) so that a year fits on the disk: 359 batches, 607,379 rows, 1.25 MB an hour; the busiest table (19,523 rows an hour) and the median one (101 rows an hour) are kept at their full rate. Every hour of every week is written through `WriteHour`. Process figures are deltas of the test process around the reads, after a full GC.

| What | Result |
|---|---|
| Open of a closed week (215 MB in 29 files) by a read, flushed at its close (log 0 KB) | 14 ms |
| The same week closed without the flush (log 9.1 MB to replay) | 46 ms |
| A year: 53 week databases of one product, 11.1 GB | written in 87 s (one hour every 10 ms) |
| Merged hours of the year loaded (8,904 records) | 20–70 ms |
| The median table for a year (8,904 batches, 899,304 rows, decoded), bound 8 / 32 / all 53 open | 3.3 s / 1.1 s / 1.0 s |
| The busiest table for a year (44,520 batches, 173.8 M rows, read, not decoded), bound 8 / 32 / all open | 3.1 s / 1.3 s / 0.38 s |
| Handles of the process, bound 8 / 32 / all open | +36 / +132 / +489 |
| Private memory after the busy read, bound 8 / 32 / all open | +8 MB / +19 MB / +137 MB |
| Working set after the busy read, bound 8 / 32 / all open | +82 MB / +300 MB / +601 MB (mostly file pages LevelDB read) |
| Ten products, five week databases each (a period of four weeks and the current week), 10.5 GB | written in 132 s |
| The median table of each of the ten products for five weeks, bound 32 / all 50 open | 2.95 s / 1.27 s; handles +168 / +450; private memory +23 / +47 MB |

What it says:

- A closed week opens in about 14 ms once flushed; without the flush the log replay triples it.
- An open week database costs the process about 9 handles and, once read through, a few MB of private memory; 53 open cost 489 handles and 137 MB. With the bound the cost stays flat whatever the number of weeks: 36 handles and under 10 MB at 8.
- A read across many weeks costs the opens it has to repeat (about 14 ms each: 106 opens for two reads of a year at bounds 8 and 32) and, more, one or two seeks per hour: the median table's year is 8,904 hours, 17,808 seeks, and takes about 1 s even with every database open.
- **The week stays the unit.** A month would cut a year's 53 opens to 13 (about 0.5 s less for a read of a year at a bound of 8) and nothing else: the seeks per hour, the bytes read and the memory per open database are the same. It would cost a coarser retention (rows kept up to a month past their period). Not built. A read of a long range is bound by its seeks per hour; a summary per sensor and week (which hours hold it) would remove most of them, if long reads of quiet tables matter (not built, not needed by any decision yet).
- Not measured: Linux; a cold disk (every figure is warm or nearly); the whole feed (four times the bytes; the counts of databases, opens and seeks do not change with it); the read with the row limit of a real read (1 M rows would stop the busiest table's year after about two days).

## The merge (#1850, part 3)

`TableMergeJob` (`src/server/HSMServer.Core/TableSensors/TableMergeJob.cs`), owned by `TableBlockService` (`Merge`) and run by `TableMergeService` (`src/server/HSMServer/BackgroundServices/DatabaseServices/`): one pass a minute after a start delay of a minute, one pass at a time, at below normal priority. A pass does nothing and opens nothing on a server without a `Tables` folder. The pass takes its clock as a parameter (`RunPass(utcNow, token)`); it never throws: a fault is logged, counted and reported in its `TableMergePassReport`.

### A pass

1. **The state after a start, per product** (`TableBlockService.EnsureMergeState(product)`, once per product, before its first put and before the merge of its first hour): the product's watermarks are raised in the block store (`GetWatermarks` → `RaiseWatermarks`), and its newest committed hour (`GetNewestHour`) is closed (`CloseHour`, so no block of the product lands in an hour committed for it). A product whose batches cannot be opened (a held `LOCK`, permissions) faults its own puts and merges only (`TableBlockStoreException`) and is tried again on its next use; the other products go on.
2. **Due hours**: the arrival hours with blocks of a product not committed for them, whose end plus `MergeGrace` (2 minutes) has passed, oldest first, every one of them (a pass catches up after a downtime).
3. **One hour H**: `CloseHour(H)`; the blocks of H are listed after it (an append in flight is published under the store's lock, so the listing is the whole hour). Per product with blocks in H that is not committed (in the order of the products' ids):
   - **The memory budget** (`TableSensors.MergeMemoryBudgetMb`, 256 MB by default) bounds the rows held at once, each weighed by its table's columns: `TableMergeJob.RowBytes(columns)` = 8 bytes a cell (the block reader gives every column, the time column included, its own `long[]` or `double[]`) + 1 bit a cell for null maps, rounded up to a byte per 8 columns + 20 bytes a row (the arrays the cut of a sensor builds over its rows: table, row, time, order). So 45 bytes a row for 3 columns, 61 for 5, 280 for 32, 540 for 64. To the rows each table of a block adds what the reader allocates for it whatever its rows, `TableMergeJob.TableBytes` (64-bit runtime: object header 16, array header 24, reference 8): 104 + the id's length per table (the `BlockTable`, its id array, its column array, its slot in the sensor's list), 80 per column (the value array's header, the `BlockColumn`, its slot) and 32 per column with a null map (the map's header and padding). Blocks hold about six rows of a table, so for a wide schema this is more than the rows: a one-row table of 64 columns weighs about 5.8 KB, not 540 bytes. With the default budget about 6 M rows of 3 columns in one pass in tables of thousands of rows, 0.96 M of 32. Not weighed: the copy of the one batch being encoded (at most 4,096 rows), the tables of the block being read that are not kept, and the encoded batches, held until the hour is written (5.4 bytes a row in the benchmark's hour).
   - The index gives an hour's rows (the sum of `RowCount`) but not their width. When the rows fit the budget at the narrowest weight (`RowBytes(1)`, 29 bytes), one pass reads every block and adds up the weight of each table it keeps; once that passes the budget it gives up, before anything is encoded, and the hour is split as below (the blocks it found unreadable stay noted, the rows it counted as dropped or expired are counted again by the shares). Otherwise, or after such a try, a pass reads only the directories, weighs each table (by path, `TableBytes` of its directory entry: rows, columns, null maps, id; tables dropped later weigh too) and gives the tables to shares that fit the budget, heaviest first (a table heavier than the budget gets a share of its own); then one pass per share reads the blocks again and keeps the share's tables. The try and the split weigh by the same `TableBytes`, so a wide schema, or many short tables, is split where a narrow one with as many rows in few tables is merged in one pass. A table's rows are never split across shares, and the batches written are byte for byte those of one pass.
   - A block that is missing from `Blocks`, whose envelope is damaged, or whose bytes do not read (`TableBlockFormatException`) is unreadable: kept in the block store, listed in the hour's record, counted. An error of LevelDB's `Get` is told by the status its message starts with (`TableBlockDatabase.IsTransientReadError`, next to `ReadBlock`): an `IO error` is transient, a fault, not damage: the merge reads with `ReadBlock(entry, readErrorIsFault: true)`, which throws `TableBlockStoreException`, and the product's hour is merged again by a later pass. Any other status (`Corruption`, `Not found`, `Not implemented`, `Invalid argument`) or an error without one is permanent: damage, so the block is listed as unreadable, counted and reported with an Error, and the hour is committed without it; retrying it would hold its hour and every later hour of its product back for ever. The reads of users count either as a failed block. A block read in the first pass and not in a later one is a fault.
   - Refused tables (the envelope) are skipped. A table is resolved to the table sensor on its path in the product (strict UTF-8, `TryGetSensorByPath`); rows without one, of a block below the sensor's `FirstSequence`, or whose column types are not the sensor's are dropped and counted. Rows of a sensor whose period class has passed for the week of the hour (`TableBatchRetention.IsExpired(week start, class, now)`: the week's end plus the class's whole weeks; the same boundary retention uses, in `HSMDatabase.AccessManager/TableBatches/TableBatchRetention.cs` for part 4) are left out and counted as expired: a catch-up merge after a long downtime must not write into a week database retention drops or has dropped (`WriteHour` refuses a dropped week), and the hour commits with the other sensors' rows, without a fault.
   - Per sensor: its rows (the block tables kept as they are, not copied) are ordered stably by (time, block sequence, row) — no sort when they are in order already — and cut evenly into batches of at most 4,096 rows. Each batch is encoded, decoded and compared value by value (doubles by their bits) and null by null with its rows. A mismatch, a decode failure or an encode refusal stops the product: nothing is written for it, `Verification mismatches` grows (Error), its blocks stay, and the product hour is not tried again in this process (a codec defect is deterministic; it is tried after the next start).
   - The parts: one per period class, the sensors by id, each sensor's batches in row order. The commit: first and last block sequence, the blocks merged, the unreadable ones, and the watermarks (the product's watermarks from `GetWatermarks`, per producer the highest over every committed hour, raised by every block of the hour, unreadable ones included).
   - The free disk of the `Tables` folder is checked against `TableSensors.MinFreeDiskMb` (a fault below it), the stop token is checked, and `WriteHour` writes and commits the product's hour.
   Once every product with blocks in H is committed, the block store's watermarks are raised for those products.
4. **Deletions** run first in every pass, before any merge, each hour in its own guard: a fault of the merge (too little disk above all) never holds off the deletion that gives the disk back, and an hour that cannot be deleted now does not hold off the others. Every arrival hour whose every product with blocks in it is committed, and that holds a block not listed as unreadable, is deleted once `DeleteMergedBlocksAfterHours` have passed since the latest commit time of its products (the record's commit time, so the delay holds across a restart), with `DeleteMergedHour(H, keep: the unreadable blocks, watermarks: the products' newest)`, under the write side of the read gate (`TableHourGate`).
5. **Faults** are per product: any exception while a product's hour is merged (a store, the free disk, a block read error, its batches that cannot be opened) leaves that product's hour untouched or committed, is logged and counted (`Faults`), and the product's later hours wait for the next pass, so a product's hours are committed in order. The other products of the hour and of later hours go on, and the next pass tries the faulted product again from its oldest unmerged hour. What this keeps: an hour's blocks are deleted only once every product with blocks in it is committed, so an hour left on its blocks keeps them whatever later hours do; the read decides hour by hour and assumes no order; the watermark is the highest producer sequence merged for a product, and while a product's earlier hour is not committed its blocks are still in the block store (no later hour of that product is committed meanwhile, except after a verification mismatch, whose hour's blocks are never deleted) and its resends are answered by the duplicate map first. A fault outside any product (the block store cannot list its blocks) ends the merge part of the pass; the deletions have run by then.
6. **Shutdown**: the token is checked between hours, products and shares and before a product's `WriteHour`; a product's hour is committed or untouched. A call that started after the stores were disposed ends with their "closed" fault; with the token cancelled the job takes it as the stop, not as a fault.

### Crash points

Each pinned by `TableMergeJobTests.A_crash_at_any_point_of_the_merge_...` (a theory over the points): the hook throws, the batch store stops without flushing its databases, the server restarts over the same folders, and then a read returns the same rows in the same order as before the merge, the hour is committed exactly when the crash came after its commit, a rerun completes the hour (or its deletion), and a second rerun merges, writes and deletes nothing.

| Point | Hook | Left on disk | After the restart |
|---|---|---|---|
| After encode | `TableMergeJob.Checkpoint("encoded")` | nothing of the hour | served from blocks; the rerun merges |
| After one class's part | `TableBatchDatabase.Checkpoint("part-written")` | one part with its mark | served from blocks; the rerun skips the part and commits |
| Before the `Hours` commit | `"before-commit"` | every part | the same |
| After the commit | `"committed"` | the commit | served from batches; the rerun raises the watermarks only |
| After the watermarks | `TableMergeJob.Checkpoint("watermarks-raised")` | the commit (watermarks are in memory) | the start state raises them again |
| Between the two deletes | `TableBlockDatabase.FaultHook("after-hour-blocks-deleted")` | blocks gone, index entries left | the hour is committed, its listed blocks are ignored; the rerun removes the entries |
| Before the compaction | `"before-hour-compaction"` | the deletes | the rerun deletes nothing more and compacts |

### The read (`TableBlockService.Read`)

Under the read side of `TableHourGate` (shared; a deletion takes it alone, so no hour's blocks are deleted while a read uses them, also with a delay of 0):

1. The sensor's product's blocks are listed (`GetBlocks`); the candidates are those at or above the sensor's `FirstSequence` whose header range meets `[from, to)`.
2. For each arrival hour of a candidate, `IsHourMerged(product, hour)` is asked once: a committed hour's blocks are ignored (kept ones and those within the delay included); the others are read from blocks.
3. The committed hours are listed afterwards (`GetHours`, with their classes' row ranges). An hour is read from its batches when it has the sensor's current period class with a range that meets `[from, to)`, its last block sequence is at or above the sensor's first sequence, and it was not found uncommitted in step 2 (a commit between the two steps: its blocks are still there, the gate holds their deletion off). So an hour is read from batches or from blocks, never both and never neither, at any moment of a merge and at any crash point. Late blocks are found by the row-time ranges of the hours that merged them.
4. **Sources** — the blocks of one arrival hour, or one merged hour — are visited in the order of their earliest row in the range (then by hour). A merged hour's batches are read with one `ReadSensor` per hour (bounded memory for long reads), skipped by their index range, decoded and added; a batch that does not decode, or whose columns are not the sensor's, is counted (`BatchesFailed`, the store's `Batch read failures`) and skipped.
5. **Order**: time, then arrival hour, then rank within the hour — `(block sequence << 24) | row` for a block, `(moved << 62) | (ordinal << 12) | row` for a batch. Within a merged hour the ordinals follow the merge's (time, block sequence, row) order, so the order is the same before, during and after a merge.
6. **The limit stops early**: past the limit the collected rows are sorted and the latest dropped (as before); before each source, once rows have been dropped past the limit, the read stops when the source's earliest row is after the latest row kept. `Truncated` stays exact (a row was dropped); the result equals the unlimited result cut at the limit.
7. A fault of the batch store is thrown as a `TableBlockStoreException`: one fault type for the caller (5xx).

`TableRows` gains `BatchesRead` and `BatchesFailed`.

### The period class (`TablePeriodClass`)

`Weeks(policy, utcNow)`: the presets by their nominal length (`TimeInterval.Month` 31 days → 5, three months → 14, six months → 27, a year → 53), custom ticks rounded up to weeks (at least 1), 0 for kept for ever. It applies the rule of the ordinary history cleanup: what never expires there is 0 here — `None`, `FromFolder` with 0 or fewer ticks, ticks at or past `utcNow.Ticks`, and a class above 65,535 weeks. **Finding (an open question for the owner, not worked around):** a dynamic preset (a month, a year) inherited from a folder reaches the sensor as `FromFolder` with 0 ticks (`TimeIntervalModel.ToFromFolderModel` of a preset), which says nothing about its length; such a table sensor is kept for ever, exactly as an ordinary sensor's history is kept for ever today. Pinned by `TableMergeJobTests.The_period_class_is_...`.

The read uses the class the sensor has at the read. Until the move between classes (part 4) exists, a sensor whose period was changed does not see its batches of hours merged under the old class.

### A block with every table refused

Such a block is not stored and answered `AllRefused`, a final answer: the collector drops it, its rows are lost and counted, and its producer sequence is never sent again (the wire contract). The reason is the watermark: it answers every sequence of the producer at or below the highest merged one as a duplicate, so a refused block sent again after a later block of its producer was merged would be acknowledged and not stored. After its sensors are registered, the rows of later blocks are taken.

### Configuration

`TableSensors.MergeMemoryBudgetMb` (256 by default, 1..1,048,576). `DeleteMergedBlocksAfterHours` is now used. The grace after the end of an hour (2 minutes) is `TableBlockOptions.MergeGrace`, not configurable.

### Self-monitoring of the merge

Under `Database/Tables`, created with the batch store's sensors (once a product of the batch store was opened in this process), from `ITableBatchStore.MergeStatistics`: `Arrival hours merged`, `Merge lag` (closed hours due and not merged at the end of the last pass, hours that did not verify left out), `Last merge duration` (ms), `Rows merged`, `Merged blocks size` and `Merged batches size` (MB: bytes before and after), `Verification mismatches` (Error whenever above 0, it must stay 0), `Unreadable blocks kept` (Error in the sample in which it grew), `Rows of removed sensors dropped`, `Rows expired before the merge`, `Merged hours holding blocks` (hours committed for every product whose blocks wait for the delay). With the block store's sensors: `Duplicates by watermark`. `Batch read failures` also counts batches the read could not decode.

### Measured (2026-11-25, `TableMergeBenchmark`, `HSM_TABLE_STORE_BENCH=1`)

Windows, .NET 8.0.28, Release, 24 logical CPUs, one NVMe machine shared with other work (timings approximate). One synthetic busy hour: 1,000 table sensors (time, bid, ask) registered in a real tree, 360 blocks of the feed of `TableBlockStoreBenchmark` (random walk, no real data; its noise compresses far worse than real quotes, so every byte figure is pessimistic), 2,415,571 rows, put through the block service, then one merge pass with the stores. Memory: the process's private memory and the managed heap sampled every 20 ms during the pass, against their values after a full GC before it; the managed heap figure counts garbage not yet collected.

| What | Result |
|---|---|
| Put of the hour (360 synced puts) | 2.1 s; 31.1 MB of blocks, 13.5 B/row |
| The merge, one pass (budget 256 MB) | 2.2 s, 0.93 µs/row; allocated 902 MB, managed heap peak +235 MB, private memory peak +173 MB |
| The same hour, budget 32 MB | 3 passes over the blocks (at the former flat 40 bytes a row; 45 now for 3 columns); 2.5 s, 1.03 µs/row; allocated 1,518 MB, managed heap peak +206 MB, private memory peak +49 MB |
| Bytes before / after | 31.1 MB of blocks → 12.5 MB of batches (5.41 B/row on this noise), 1,313 batches |
| Read of the busiest table (229,551 rows) for the hour | 153 ms from blocks, 22 ms from batches |

What it says: the merge takes about a microsecond per row on one core, as phase A measured in memory (1.7 s for 2.4 M rows), so a busy hour costs about two seconds a hour. Every block read materializes all its tables (`TableBlockReader.Read`), so the bytes allocated are several times the hour's rows; the private memory, not the allocations, is what the budget bounds: 173 MB at one pass, 49 MB at three passes for 0.25 s more. The managed heap peak barely moves with the budget because it counts the garbage of the block reads before a collection. Not measured: Linux, a cold disk, real data, several products in one hour, the read across many merged hours with the early stop.

## What the merge (part 3) and retention (part 4) must do

Built in parts 1 and 2 and meant to be called as described here; part 3 (above) calls it so. The decisions behind it are in issue #1850 (the owner's decisions of 2026-11-25 and the architect's answers to the phase A report) and ADR-0021 points 9 to 12.

### The API to call

| Call | Where | What for |
|---|---|---|
| `TableBatchCodec.Encode(TableBatch, TableBatchEncodeStats)` / `Decode` | `src/database/HSMDatabase.SealedHistory/TableBatchCodec.cs` | Encode up to 4,096 rows of one sensor; decode, also to verify bit for bit before anything is deleted (`TableBatchColumn` carries the values and the null map in the bit order of `TableNullMap`). |
| `ITableBlockStore.CloseHour(hour)` | `src/database/HSMDatabase.AccessManager/TableBlocks/ITableBlockStore.cs` | Before merging an hour: no block is appended to it afterwards. In memory only: call it for the newest merged hour after every start, before the first put. |
| `ITableBlockStore.GetBlocks(product)`, `ReadBlock(entry)` | the same | The blocks of the hour (`ArrivalHourTicks`), their envelopes (refused tables) and bytes. `ReadBlock` null or a `TableBlockFormatException` from `TableBlockReader`: an unreadable block, kept and listed. `TableBlockStoreException`: a fault, stop the hour and retry later. |
| `ITableBatchStore.WriteHour(product, commit, parts)` | `src/database/HSMDatabase.AccessManager/TableBatches/ITableBatchStore.cs` | Write and commit one merged hour of a product: one `TableBatchHourPart` per period class, the batches of each sensor in their row order; `TableBatchHourCommit` with the first and last block sequence of the hour, the blocks merged, the unreadable ones (`UnreadableBlocks`) and the product's cumulative watermarks (producer id to the highest producer sequence merged: the previous hour's map from `GetWatermarks`, raised by this hour's blocks). Idempotent: an hour committed is not written again, a part with its mark is skipped. |
| `ITableBatchStore.GetHours(product)`, `IsHourMerged` | the same | The merged hours in memory: per hour its period classes with their minimum and maximum row time (select the hours of a read by them: a late block's rows sit in a later merge hour), the first block sequence (for ordering, below) and the unreadable blocks. |
| `ITableBatchStore.GetWatermarks(product)`, `GetProducts()` | the same | At start, for every product with a folder: raise the block store's watermarks with `ITableBlockStore.RaiseWatermarks` before the first put. |
| `ITableBatchStore.ReadSensor(product, sensor, periodWeeks, hours)` | the same | A sensor's batches for the selected merge hours, from the database of each hour's week and of the class given (the sensor's current class; outside a move a sensor's rows of a week are in one database), the hour region then the moved region, in hour order then ordinal. |
| `ITableBlockStore.DeleteMergedHour(hour, keep, watermarks)` | `ITableBlockStore.cs` | After the commit and after `TableSensors.DeleteMergedBlocksAfterHours` (24 h by default): delete the hour's blocks except the unreadable ones, raising the watermarks in the same lock. Idempotent; a repeat also removes index entries left by a crash between its two writes. |
| `ITableBatchStore.DropWeek(product, week)`, `GetWeeks(product)` | `ITableBatchStore.cs` | Retention: drop a week database as a folder when its period class has passed for it (rows may stay up to a week longer). |
| `TableBatchDatabase.WriteMoved(product, week, hour, batches)` (internal) | `src/database/HSMDatabase.LevelDB/TableBatches/TableBatchDatabase.cs` | The move of a sensor to another period class: writes into the region of moved batches of the target week (after every hour key, so it never overlaps ingest). Nothing more of the move is built. |
| `TableSensors.MaxOpenBatchDatabases`, `DeleteMergedBlocksAfterHours` | `src/server/HSMServer/ServerConfiguration/Sections/TableSensorsConfig.cs`, `TableBlockOptions` | The bound on open week databases (set on the store by `TableBlockService`'s constructor) and the delay before the blocks of a merged hour are deleted (from the commit time, part 3). |

### Blocks or batches, never both, never neither

The order of writes for one arrival hour H:

1. `CloseHour(H)`.
2. Per product with blocks in H: read the blocks, build the batches, encode, decode and compare every cell and every null with the source rows. A mismatch: nothing written for that product, the failure counted as an Error, the blocks stay.
3. `WriteHour` for the product (parts, then the commit in `Hours`).
4. Only once every product with blocks in H is committed, and after the delay: `DeleteMergedHour(H, keep: the unreadable blocks, watermarks: every product's watermarks of H)`.

**The read rule that makes it hold:** for product P and hour H, when `IsHourMerged(P, H)` (the commit exists) the hour is read from batches only and its blocks are ignored, kept ones and those still within the delay included; otherwise from blocks only. A crash at any point leaves an hour either committed (batches) or not (blocks); the blocks are deleted only after the commit. A repeat of the merge skips committed hours and parts already written.

### What goes into the batches

- **The sensor of a table:** by its path in the product (`ITreeValuesCache.TryGetSensorByPath`), as the put does. Rows of a table refused in the envelope are skipped; rows of a table with no table sensor on its path, or of a block whose sequence is below the sensor's `FirstSequence` (a removed sensor's rows), are dropped and counted. Batches are keyed by sensor id, so a sensor created on a removed sensor's path never sees the old rows.
- **The class of an hour's batches:** the period class the sensor has at the merge (its history setting in whole weeks, rounded up: Week 1, Month 5, 3 months 14, 6 months 27, Year 53, a custom period rounded up, at least 1; Forever = `P0`). A change of the period afterwards is a move (part 4), never a second class for the same week.
- **Order:** the rows of a sensor in the hour sorted stably by time, then block sequence, then row in the block (the order `TableRowCollector` uses for blocks), cut evenly into batches of at most 4,096 rows (`ChunkCodec` cuts the same way) and written in that order: the ordinal of a batch within its hour and database is its rank.
- **Equal times across blocks and batches:** a read orders by time, then by arrival hour, then by rank within the hour. For an unmerged hour the rank is (block sequence << 24 | row); for a merged one it is the batch ordinal then the row in the batch. Since every block of an earlier arrival hour has a smaller sequence than every block of a later one, the order is the same before, during and after the merge of an hour.

### Watermarks and duplicates

- The watermark of a product and producer is the highest producer sequence merged. It is durable in the records of the merged hours (the highest per producer over all of them); in the block store it is in memory only (`RaiseWatermarks`, raised again by `DeleteMergedHour`). After a start the merge must raise it from `GetWatermarks` for every product before the first put, or a resend of a merged block would be stored again.
- A put at or below the watermark is a duplicate with `RefusalsKnown` false. `AcceptedTables` is null then (and for a duplicate whose stored envelope cannot be read): which tables were taken is not known (#1850, after the review of #1867).
- It relies on a producer delivering its blocks in order and never taking its sequence back (#1845: a collector that cannot read its producer state takes a new producer id).

### Memory and time of the merge

Measured in phase A (in memory, one busy hour of 2.4 M rows): about 1.7 s on one core (0.2–0.35 s reading and splitting the 360 blocks, 1.2–1.3 s encoding, under 0.1 s verifying) and about 200 MB of managed heap with list-based accumulators; with arrays about 40 bytes per row (3 columns). The budget idea (since weighed by the columns, see "A pass"): when an hour's rows (the sum of `TableBlockIndexEntry.RowCount` of its blocks) times 40 bytes exceed a configured budget (256 MB suggested), merge the hour's tables in several passes, each pass reading the blocks again (0.2–0.35 s a pass) and keeping only its share of the tables. One merge at a time, below normal priority, never holding the block store's append lock while it reads or encodes.

### Retention and removal (part 4)

- Retention drops a week database when its class's period has passed for it; every sensor in a class database has that period (the move keeps it so). Blocks never merged leave when their arrival hour is older than the longest period of the product's table sensors.
- A period change moves the sensor's rows into the databases of the new class (copy into the moved region of the same week, delete from the old database, one compaction of the old database at the end), with a durable record of the move in progress that the read consults; a second change during a move is queued or redirects it; a removed sensor cancels its move; a week whose new period has passed is deleted instead of copied. When every sensor with rows in a week's database moves to the same new class and that class has no database for the week yet, the move is a rename of the folder.
- Removing a sensor deletes its index records and batches; under Forever a week is compacted once the removed share passes 10%.
- The `CheckSensorHistory` skip of table sensors (`src/server/HSMServer.Core/Cache/TreeValuesCache.cs`) is where the history setting starts to apply to them; the page of a table sensor says that rows are kept by whole weeks and may stay up to a week longer.

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.SealedHistory/TableBatchCodec.cs` | The codec |
| `src/database/HSMDatabase.SealedHistory/TableBatch.cs` | Columns, batch, header, statistics, errors |
| `src/database/HSMDatabase.SealedHistory/Format/IntColumn.cs`, `DoubleColumn.cs` | The shared coders and the additive helpers |
| `src/tests/HSMDatabase.SealedHistory.Tests/TableBatch*.cs` | Round trips, corruption, golden batches, benchmark |
| `src/database/HSMDatabase.AccessManager/TableBatches/ITableBatchStore.cs` | The store's interface, records, statistics |
| `src/database/HSMDatabase.LevelDB/TableBatches/TableBatchDatabase.cs` | The batch store |
| `src/database/HSMDatabase.LevelDB/LevelDbOpen.cs` | The open with its bounded retry on the transient `.dbtmp` failure |
| `src/database/HSMDatabase.LevelDB/TableBlocks/TableBlockDatabase.cs` | `CloseHour`, `DeleteMergedHour`, watermarks |
| `src/tests/HSMDatabase.LevelDB.Tests/TableBatchTests/` | The store's tests and `TableBatchStoreBenchmark` (`HSM_TABLE_BATCH_STORE_BENCH=1`) |
| `src/tests/HSMDatabase.LevelDB.Tests/TableBlockTests/TableBlockMergeTests.cs` | The block store's part |
| `src/server/HSMServer.Core/TableSensors/TableMergeJob.cs` | The merge job |
| `src/server/HSMServer.Core/TableSensors/TableBlockService.cs`, `TableRowCollector.cs`, `TableHourGate.cs` | The read rule, the joined read, the start state, the gate |
| `src/server/HSMServer.Core/TableSensors/TablePeriodClass.cs` | The period class of a sensor |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/TableMergeService.cs` | The schedule |
| `src/tests/HSMServer.Core.Tests/TableSensors/TableMergeJobTests.cs`, `TableMergeBenchmark.cs` | The merge's tests and measurement |

## Tests

`src/tests/HSMDatabase.SealedHistory.Tests`:

- `TableBatchRoundTripTests`: extreme values of every type (NaN payloads, ±0.0, ±Infinity, `long.MinValue`/`MaxValue`), one row, 4,096 rows (and 4,097 refused), a column of nulls only, columns of one value, a null in every position of columns of 1..17 rows (and all positions but one), the value under a null ignored, equal times keep their order, rows out of time order; exceptions: none, one, the maximum (16 of 4,096; 1 of 100), one more than the maximum gives the decimal mode up, exceptions counted among present values; the rule codes quote columns and takes raw for noise; the additive coder helpers write what `IntColumn.Read` and `DoubleColumn.Read` decode; 400 random batches from a fixed seed.
- `TableBatchCorruptionTests`: every error by name (magic, version, checksum at ten positions, a cut at every length, bytes after, every length that disagrees, header fields, the frame, column and row counts that do not match the payload, a raw and a varint stream cut short, unknown types and encodings, exception lists, presence padding, bytes after the last column, first and last time); a payload length of 2 GB refused with under 64 KB allocated; every single-byte change behind a rewritten checksum is refused by name or decodes whole; the encoder's argument checks.
- `TableBatchGoldenTests`: eight fixed inputs with their own seeds (full quotes, quotes with nulls, one row, extremes, outliers, noise, null and constant columns, equal times); the set holds compressed and uncompressed batches, raw and coded streams of every kind, exceptions and nulls; the zstd levels pinned.
- `TableBatchBenchmark` (`Category=Benchmark`, `HSM_TABLE_BATCH_BENCH=1`): the comparison table above on a synthetic hour.

The store (`src/tests/HSMDatabase.LevelDB.Tests/TableBatchTests/TableBatchDatabaseTests.cs`): nothing created before the first write; an hour read back by sensor in its order with its ranges and watermarks; two period classes in two databases; two weeks in two databases joined in hour order; a second write of a committed hour changes nothing; a crash after the first of two parts, before the commit and after it, then a reopen and a repeat (merged or not, every part once); a stop without a flush replays every committed hour; hours and the watermarks after a restart; a producer named only by an hour committed after a later one keeps its watermark, also after a reopen and a rebuild; `Hours` rebuilt from the marks without the hour whose part is missing; a damaged `Hours` put aside and rebuilt; a week flushed when a later one is written (no log left); a read across more weeks than the bound returns every batch and leaves the bound respected; a dropped week gone from disk, from the hours and from the ranges, also after a reopen; a rebuild of `Hours` after a shorter class was dropped restores the hour with the class left, and still refuses an hour whose part was never written; a drop waiting for a read whose release closed the database over the bound does not close it twice; a read during a drop finds nothing and does not open the week again; a delete that fails is a fault with the week dropped, a write into it refused, the folder deleted at the next open; a dispose waits for a read that holds a database and for a write in flight (which commits), and refuses what starts after it; a period above 65,535 weeks is an argument error, a failed flush of the previous week a counted fault; two products apart; the region of moved batches after every hour key and read with the hour region; merged hours cause no real compaction (LevelDB's `LOG`); the open retried on the `.dbtmp` failure and on no other.

The block store (`TableBlockMergeTests`): a closed hour takes no block, also with the clock behind; a merged hour deleted except the kept blocks, its resends duplicates by the watermark (not another producer's or product's), also after a reopen; watermarks only grow and need no open store; a crash between the deletes of the blocks and of their index entries is completed by a repeat; a delete in a store never opened only raises the watermarks. The service (`TableBlockServiceTests`): a resend after the merge is a duplicate with its refusals unknown; the bound on open databases comes from the options. Configuration and self-monitoring: `TableBlockOperationsTests`. The block store also: a crash before the compaction of a deleted hour, completed by a repeat; the products listed, none without a folder. The store: the commit time survives a restart; a rebuilt `Hours` takes the time of the rebuild.

The merge and the read (`src/tests/HSMServer.Core.Tests/TableSensors/TableMergeJobTests.cs`, over a real `DatabaseCore` and tree, restarted over the same folders): every crash point (table above); a read at every stage (before, one hour merged with its blocks still there, both merged, after each deletion) for a sensor with rows in a merged hour, an unmerged hour and a late block with equal times across the three; one product committed and another not (the read the same, nothing deleted until both are); a damaged block kept, listed, counted and left by the deletion; a verification mismatch injected after the decode (nothing written, counted, blocks stay, not retried in the process, merged after a restart); a store fault before the commit (a fault, retried by the next pass); too little free disk (a fault before any write); a sensor removed and created again on its path between the put and the merge (the new one never sees the old rows; they are dropped and counted); nulls, 5,100 rows of one table in an hour (two batches of 2,550), an hour of one row of nulls only, rows out of order within and across blocks; a resend of a merged and deleted block after a restart is a duplicate with its refusals unknown, per product, and with the clock behind no block lands in the merged hour; the blocks of an hour merged late stay until commit + delay across a restart; the row limit from 1 to 25 over four overlapping hours (two merged) equals the unlimited result cut at the limit, with `Truncated` exact, and stops before the hours after the rows it keeps; a budget forcing four shares writes the same batch bytes as one pass; a wide schema (40 columns) is split by a budget its rows fit at 3 columns, while a narrow table of as many rows is merged in one pass, and its batches are those of one pass (`A_wide_schema_is_split_where_a_narrow_one_with_as_many_rows_fits_one_pass`); 288 one-row tables that fit four times over by their rows are split by what their tables allocate (`Many_one_row_tables_are_weighed_...`); a single pass given up at the budget counts the rows dropped and expired and the unreadable block once, as a pass under a large budget does (`A_single_pass_given_up_at_the_budget_counts_...`); the period class of every preset and the inherited forms; a sensor with its own period merged into its class's database. After the first review: with too little disk a pass still deletes the merged hours past their delay and the next pass merges; a product whose merge keeps faulting does not stop another's hours; an IO error while the merge reads a block is a fault and the next pass merges the hour whole, a corruption error is damage (the block listed as unreadable, the product's next hour merged in the same pass) (the block store: an IO error is a fault for the merge and a counted failure for a reader, a corruption is damage for both); a product whose batches cannot be opened faults its own puts and merges only. After the merge of the batch store's fixes: a catch-up merge of an hour older than one sensor's period and younger than another's commits with the second sensor's rows only, the first's counted as expired, without a fault.

## Known Issues / Limitations

- The choice against raw at zstd 1 was measured on synthetic quotes, where raw wins only for a few tiny batches; a column of unrelated whole numbers (sizes) is where raw should win.
- Not measured on real data: the check of the codec over the two private hours of the epic was skipped (their converted files cannot be read without the old benchmark); it stays open for the rollout step (#1849).
- Retention and the move between period classes are part 4 of #1850. Until the move exists, a table sensor whose period changes does not see the batches of hours merged under its old class.
- A table sensor that inherits a dynamic preset (a month, a year) from a folder is kept for ever (see "The period class"): an open question for the owner.
- A part of an hour moved by a period change between two runs of the merge no longer stays behind: the repeat deletes it after writing its own parts and before the commit (see "Writing an hour"). What remains for part 4: a period change after the commit leaves the sensor's batches in the old class's database, which the read (by the current class) does not look at until the move exists.
- The products lock (review of #1867, for part 4): `Product()` opens a product, `Rebuild` included (every week database of the product, 168 `Get`s in each), under the store-wide `_productsLock`, so every call on every product waits meanwhile, `IsOpened` of the self-monitoring tick too; and `WeekDatabases` takes each product's lock while it holds `_productsLock`, so a product lock held long (a synced `WriteHour`, a `DropWeek` waiting for the reads of its week) stalls the monitoring tick and every `Product()` call. No deadlock (no path takes these locks in the other order), but the stall is unbounded. Suggested: open a product outside `_productsLock` (a per-product `Lazy` or placeholder, or a double-checked insert); have `WeekDatabases` copy `_products.Values` under the lock and visit each product outside it, or keep an `Interlocked` count.
- An hour committed with no batch at all (every table of its blocks refused or dropped) has no mark in any week database: an `Hours` rebuilt from the marks loses it, and with it the watermarks of that hour if it was the newest; the hour is then merged again from its blocks while they are kept, and its producers' resends after the deletion are not recognised until the next merged hour raises them.
- A crash during `Rebuild`'s scan (for part 4): the new `Hours` exists, empty or partly written, and the next open trusts it, so the hours the scan had not written are not merged and are merged again from their blocks while the blocks are kept; once the blocks are deleted their batches are no longer served. Needs a "rebuilt" marker written after the scan.
- A block appended to an arrival hour after the hour's parts were written and before its commit (a clock set back past `CloseHour`): if `Hours` is then lost, the rebuild commits the parts without that block's rows, and the deletion of the hour takes the block.
- `WriteMoved` (part 4) acquires its database unpinned and marks it dirty, like `RemoveStaleParts` did before it was pinned: a database of the newest week it writes into is flushed at its close, not when the next week starts.
