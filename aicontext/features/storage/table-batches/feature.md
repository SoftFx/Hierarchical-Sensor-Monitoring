# Feature: Table batch format

> Owner: storage | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: the byte format of a stored batch of a table sensor (#1850, part 1; ADR-0021 points 9 and 11): the header, the payload, the column streams, the rule that chooses a stream, the exception list, nulls, versioning, and the codec `TableBatchCodec` that writes and reads it. Where batches are stored, the merge that writes them and retention are later parts of #1850. The block a collector sends: `aicontext/features/api/table-block/feature.md`. The block store: `aicontext/features/storage/table-blocks/feature.md`.

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
| `TableBatchEncodeStats` | What the encoder chose, summed over the batches given: streams written coded or raw per kind of column (time, int64, double), exception cells, double columns that kept the decimal mode thanks to exceptions or have none, null cells, batches, rows, bytes. For the merge's self-monitoring and for measurements. Not thread-safe. |

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

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.SealedHistory/TableBatchCodec.cs` | The codec |
| `src/database/HSMDatabase.SealedHistory/TableBatch.cs` | Columns, batch, header, statistics, errors |
| `src/database/HSMDatabase.SealedHistory/Format/IntColumn.cs`, `DoubleColumn.cs` | The shared coders and the additive helpers |
| `src/tests/HSMDatabase.SealedHistory.Tests/TableBatch*.cs` | Round trips, corruption, golden batches, benchmark |

## Tests

`src/tests/HSMDatabase.SealedHistory.Tests`:

- `TableBatchRoundTripTests`: extreme values of every type (NaN payloads, ±0.0, ±Infinity, `long.MinValue`/`MaxValue`), one row, 4,096 rows (and 4,097 refused), a column of nulls only, columns of one value, a null in every position of columns of 1..17 rows (and all positions but one), the value under a null ignored, equal times keep their order, rows out of time order; exceptions: none, one, the maximum (16 of 4,096; 1 of 100), one more than the maximum gives the decimal mode up, exceptions counted among present values; the rule codes quote columns and takes raw for noise; the additive coder helpers write what `IntColumn.Read` and `DoubleColumn.Read` decode; 400 random batches from a fixed seed.
- `TableBatchCorruptionTests`: every error by name (magic, version, checksum at ten positions, a cut at every length, bytes after, every length that disagrees, header fields, the frame, column and row counts that do not match the payload, a raw and a varint stream cut short, unknown types and encodings, exception lists, presence padding, bytes after the last column, first and last time); a payload length of 2 GB refused with under 64 KB allocated; every single-byte change behind a rewritten checksum is refused by name or decodes whole; the encoder's argument checks.
- `TableBatchGoldenTests`: eight fixed inputs with their own seeds (full quotes, quotes with nulls, one row, extremes, outliers, noise, null and constant columns, equal times); the set holds compressed and uncompressed batches, raw and coded streams of every kind, exceptions and nulls; the zstd levels pinned.
- `TableBatchBenchmark` (`Category=Benchmark`, `HSM_TABLE_BATCH_BENCH=1`): the comparison table above on a synthetic hour.

## Known Issues / Limitations

- The choice against raw at zstd 1 was measured on synthetic quotes, where raw wins only for a few tiny batches; a column of unrelated whole numbers (sizes) is where raw should win, and it was checked on one real hour only (#1850).
- The merge, the batch store, the read from batches and retention are the later parts of #1850.
