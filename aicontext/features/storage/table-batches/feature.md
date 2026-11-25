# Feature: Table batches (format and store)

> Owner: storage | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: the byte format of a stored batch of a table sensor (#1850, part 1; ADR-0021 points 9 and 11): the header, the payload, the column streams, the rule that chooses a stream, the exception list, nulls, versioning, and the codec `TableBatchCodec` that writes and reads it. And the batch store (#1850, part 2; ADR-0021 points 10 and 12): where batches live, how a merged hour is committed, the week databases and their bound, and what the block store gives the merge. The merge job and the read that joins batches with blocks are part 3; retention and the move between period classes are part 4. The block a collector sends: `aicontext/features/api/table-block/feature.md`. The block store: `aicontext/features/storage/table-blocks/feature.md`.

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

1. If the hour is committed, nothing (a repeat changes nothing).
2. Per part, in class order: in the week database of the class, unless it holds the hour's mark already, one synced write of the index records, the batches and the mark.
3. The record into `Hours`, synced: the hour is merged from here on.

A crash before step 3 leaves the hour not merged (served from its blocks); the parts written stay and a repeat skips them by their mark, so the hour is committed once with each part once. A database closed without a flush replays its log at the next open. Pinned by `TableBatchDatabaseTests.A_crash_at_any_point_of_the_hour_write_...` (after the first of two parts, before the commit, after it) and `A_store_that_stopped_without_closing_its_databases_...`.

### What is in memory

Per product opened: the merged hours with, per class, the minimum and maximum row time (a read selects hours by them: a late block makes an hour reach back), the watermarks of the newest hour, the week databases on disk. Loaded from `Hours` at the product's first use, without opening a week database. An hour's class whose week database was dropped is left out. If `Hours` is missing, it is rebuilt from the marks of the week databases (an hour whose mark is in every database its record names); if its open fails with LevelDB's corruption or a record does not decode, it is moved aside to `Hours.damaged-<ticks>` and rebuilt the same way. An empty `Hours` that exists is the truth: a first hour whose commit was not written is not merged.

### Week databases: the bound, the flush

- The databases of a product's newest week written are pinned. When a later week is written, the databases of the earlier ones are flushed (a `CompactRange` over an empty key range writes the memtable out and merges no file) and unpinned: a closed week has no log to replay when it is opened again.
- Every other database is opened on demand (a read, retention, a rebuild) through one cache for the whole store, at most `MaxOpenDatabases` open (`TableSensors.MaxOpenBatchDatabases`, 32 by default), least recently used closed first. A database is held only while a read or a write uses it; a read of many weeks holds one week at a time. Pinned and held databases are never closed, so the count can be above the bound while they are more than it.
- A database is opened with `MaxOpenFiles` 100.
- Opens go through `LevelDbOpen.Open` (`src/database/HSMDatabase.LevelDB/LevelDbOpen.cs`), the block store's retry made shared: an IO error that names the temporary `.dbtmp` file (in practice the replace of `CURRENT`) is retried four times with pauses of 10, 30, 60 and 120 ms, any other error fails at once; a Warn line per retry.

### Reading

`ReadSensor(product, sensor, period class, hours)`: per week of the hours, the database of that week and class (none: nothing, nothing created); per hour one seek to `hour | 0x00 | sensor`, then one seek into the region of moved batches of the sensor; each index record with its batch. In hour order, then the hour region before the moved region, then by ordinal. An index record that does not decode or whose batch is missing is counted (`ReadFailures`) and skipped.

### Dropping a week

`DropWeek(product, week)` waits for the reads that hold the database, closes it and deletes its folder. The hours of that week lose the class; an hour left with none is deleted from `Hours`.

### The block store's part

- `CloseHour(hour)`: no block is appended to that hour or an earlier one afterwards, also with the clock behind (held in memory: the merge closes its newest hour again after a restart).
- `DeleteMergedHour(hour, keep, watermarks)`: under the append lock, raises the watermarks, deletes the hour's blocks except `keep` (blocks the merge could not read) from `Blocks` (synced) and then `BlockIndex` (a repeat removes index entries left by a crash between the two), and drops them from the in-memory index and the duplicate map; then compacts the range outside the append lock.
- Watermarks per product and producer, in memory (`RaiseWatermarks`, `GetWatermark`): an append at or below its producer's watermark is a duplicate with `Merged`, which the block service answers as `Duplicate` with `RefusalsKnown` false, no sequence and no refusals (`RefusalsKnown` is true by default and false also for a duplicate whose stored envelope cannot be read; the same shape as the wire step's). The durable watermarks are in the records of the merged hours (`GetWatermarks`); the merge raises them before the first put after a start (part 3).
- `TableSensors.DeleteMergedBlocksAfterHours` (24 by default, 0..8,760): how long the blocks of a merged hour stay after its commit; reads of a merged hour take the batches and ignore them (part 3).

### Self-monitoring

Under `Database/Tables`, once a product of the batch store was opened in this process: `Batch databases open`, `Batch week databases`, `Merged hours`, `Batch write failures` and `Batch read failures` (Error in the sample in which they grew).

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

## What the merge (part 3) and retention (part 4) must do

Built in parts 1 and 2 and meant to be called as described here. The decisions behind it are in issue #1850 (the owner's decisions of 2026-11-25 and the architect's answers to the phase A report) and ADR-0021 points 9 to 12.

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
| `TableSensors.MaxOpenBatchDatabases`, `DeleteMergedBlocksAfterHours` | `src/server/HSMServer/ServerConfiguration/Sections/TableSensorsConfig.cs`, `TableBlockOptions` | The bound on open week databases (set on the store by `TableBlockService`'s constructor) and the delay before the blocks of a merged hour are deleted (not used yet). |

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

- The watermark of a product and producer is the highest producer sequence merged. It is durable in the records of the merged hours; in the block store it is in memory only (`RaiseWatermarks`, raised again by `DeleteMergedHour`). After a start the merge must raise it from `GetWatermarks` for every product before the first put, or a resend of a merged block would be stored again.
- A put at or below the watermark is a duplicate with `RefusalsKnown` false. `TableBlockPutResult.AcceptedTables` still counts every table of such a duplicate (`TableCount` minus no refusals): callers must look at `RefusalsKnown` first (to settle with the wire step).
- It relies on a producer delivering its blocks in order and never taking its sequence back (#1845: a collector that cannot read its producer state takes a new producer id).

### Memory and time of the merge

Measured in phase A (in memory, one busy hour of 2.4 M rows): about 1.7 s on one core (0.2–0.35 s reading and splitting the 360 blocks, 1.2–1.3 s encoding, under 0.1 s verifying) and about 200 MB of managed heap with list-based accumulators; with arrays about 40 bytes per row. The budget idea: when an hour's rows (the sum of `TableBlockIndexEntry.RowCount` of its blocks) times 40 bytes exceed a configured budget (256 MB suggested), merge the hour's tables in several passes, each pass reading the blocks again (0.2–0.35 s a pass) and keeping only its share of the tables. One merge at a time, below normal priority, never holding the block store's append lock while it reads or encodes.

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

## Tests

`src/tests/HSMDatabase.SealedHistory.Tests`:

- `TableBatchRoundTripTests`: extreme values of every type (NaN payloads, ±0.0, ±Infinity, `long.MinValue`/`MaxValue`), one row, 4,096 rows (and 4,097 refused), a column of nulls only, columns of one value, a null in every position of columns of 1..17 rows (and all positions but one), the value under a null ignored, equal times keep their order, rows out of time order; exceptions: none, one, the maximum (16 of 4,096; 1 of 100), one more than the maximum gives the decimal mode up, exceptions counted among present values; the rule codes quote columns and takes raw for noise; the additive coder helpers write what `IntColumn.Read` and `DoubleColumn.Read` decode; 400 random batches from a fixed seed.
- `TableBatchCorruptionTests`: every error by name (magic, version, checksum at ten positions, a cut at every length, bytes after, every length that disagrees, header fields, the frame, column and row counts that do not match the payload, a raw and a varint stream cut short, unknown types and encodings, exception lists, presence padding, bytes after the last column, first and last time); a payload length of 2 GB refused with under 64 KB allocated; every single-byte change behind a rewritten checksum is refused by name or decodes whole; the encoder's argument checks.
- `TableBatchGoldenTests`: eight fixed inputs with their own seeds (full quotes, quotes with nulls, one row, extremes, outliers, noise, null and constant columns, equal times); the set holds compressed and uncompressed batches, raw and coded streams of every kind, exceptions and nulls; the zstd levels pinned.
- `TableBatchBenchmark` (`Category=Benchmark`, `HSM_TABLE_BATCH_BENCH=1`): the comparison table above on a synthetic hour.

The store (`src/tests/HSMDatabase.LevelDB.Tests/TableBatchTests/TableBatchDatabaseTests.cs`): nothing created before the first write; an hour read back by sensor in its order with its ranges and watermarks; two period classes in two databases; two weeks in two databases joined in hour order; a second write of a committed hour changes nothing; a crash after the first of two parts, before the commit and after it, then a reopen and a repeat (merged or not, every part once); a stop without a flush replays every committed hour; hours and the newest watermarks after a restart; `Hours` rebuilt from the marks without the hour whose part is missing; a damaged `Hours` put aside and rebuilt; a week flushed when a later one is written (no log left); a read across more weeks than the bound returns every batch and leaves the bound respected; a dropped week gone from disk, from the hours and from the ranges, also after a reopen; two products apart; the region of moved batches after every hour key and read with the hour region; merged hours cause no real compaction (LevelDB's `LOG`); the open retried on the `.dbtmp` failure and on no other.

The block store (`TableBlockMergeTests`): a closed hour takes no block, also with the clock behind; a merged hour deleted except the kept blocks, its resends duplicates by the watermark (not another producer's or product's), also after a reopen; watermarks only grow and need no open store; a crash between the deletes of the blocks and of their index entries is completed by a repeat; a delete in a store never opened only raises the watermarks. The service (`TableBlockServiceTests`): a resend after the merge is a duplicate with its refusals unknown; the bound on open databases comes from the options. Configuration and self-monitoring: `TableBlockOperationsTests`.

## Known Issues / Limitations

- The choice against raw at zstd 1 was measured on synthetic quotes, where raw wins only for a few tiny batches; a column of unrelated whole numbers (sizes) is where raw should win.
- Not measured on real data: the check of the codec over the two private hours of the epic was skipped (their converted files cannot be read without the old benchmark); it stays open for the rollout step (#1849).
- The merge job, the read that joins batches with blocks, retention and the move between period classes are the later parts of #1850.
