# Feature: Table block format

> Owner: api | Last reviewed: 2026-11-24 | Canonical: yes
> Scope: the byte format of a table block (ADR-0021, points 2, 3 and 5; step 1 of epic #1842, #1843), its limits, its error codes, its versioning rule, the table schema and its id (#1844), null cells and their maps (#1859), and the library `HSMTableBlocks` that writes and reads it.

---

## Overview

A **block** is what a collector sends for table sensors at every tick: the rows of all its table sensors, grouped by table (glossary: Block). It is the only table format outside the server: the managed collector writes it, the server reads it on ingest and writes it when it answers a range read, and the client library reads it. A future C++ collector writes the same format with nothing but zstd.

A block is a fixed 44-byte header followed by exactly one zstd frame. Inside the frame: a directory with one entry per table (identifier, schema id, row count, column types), then the columns as raw fixed-width little-endian arrays, column-major: column 0 of every table, then column 1 of every table that has one, and so on, then the null maps of the columns that have a null in this block. No HSM column coders are used.

Every column except the first (the time) may have cells without a value (ADR-0021 point 2, as changed on 2026-11-25). The schema does not say which columns are nullable, all but the time are, so the schema and its id do not change. A column of a table carries a **null map** (glossary: Null map) only in a block where it has at least one null; a block without nulls is byte for byte what it was before nulls existed.

`HSMTableBlocks` (`src/api/HSMTableBlocks`, netstandard2.0) is a pure library: no storage, no transport, no server or collector dependency. The server uses `TableSchema` for table sensors (#1844, `aicontext/features/server/table-sensors/feature.md`); the server's block store, its read path and the collectors will use the block reader and writer.

This document is the format specification: it is meant to be precise enough to write a second writer or reader from it.

## Invariants

- **Fidelity.** Every value reads back bit for bit: 64-bit integers, time ticks, and doubles by their IEEE 754 bits (NaN payloads, ±Infinity and -0.0 included).
- **Order as given.** Within a table the rows keep the order the writer was given. A block may hold rows out of time order and repeated times; neither the writer nor the reader sorts or requires order (ADR-0021, point 3: the server's merge sorts). Tables keep the order they were added.
- **Every column has every row; cells may be null.** Every column of a table holds exactly `rowCount` values of 8 bytes. A cell of any column but the first may be null: it holds 8 zero bytes and its bit is set in the column's null map. The time of a row (column 0) is never null. A row whose every other cell is null is a valid row.
- **One payload for one set of rows.** A map is written only for a column with at least one null, every null cell holds zero bytes, and a map's padding bits are zero; the reader refuses anything else. So two writers given the same rows produce the same payload.
- **No empty blocks.** A block holds at least one table and every table at least one row and one column. A tick with no rows sends no block. This keeps the header's first and last time always defined.
- **Unique identifiers.** A table identifier appears at most once in a block. Writer (in `ToArray`) and reader check it by sorting the identifiers bytewise and comparing neighbours: O(n log n) comparisons for any input.
- **Fail loudly, never partially.** Every read verifies the whole block and throws `TableBlockFormatException` (one type, with a `TableBlockError` code) for anything wrong. It never returns a partial block. Block bytes are untrusted input: every read is bounds-checked.
- **Bounded memory.** The limits (below) are checked from the header before anything is allocated or decompressed. Decompression writes into a buffer of exactly the declared decompressed length and stops there: a frame that would produce more is refused. A reader allocates at most the declared decompressed length (rented) plus the arrays it returns.
- **Thread safety.** `TableBlockReader` is stateless and safe from any thread. A `TableBlockWriter` is not thread-safe: one instance per thread (or per tick loop); it is meant to be reused with `Reset()`. `TableBlockLimits` and `TableSchema` are immutable.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Write the rows of every table sensor of a tick into one block | collector (step 2) |
| 2 | Read a block's header to check its size and limits before anything else | server ingest (step 3) |
| 3 | Read the directory of a block (`ReadDirectory`) to check it against the registered sensors and store it | server ingest (#1844) |
| 4 | Read one table out of a block | server range read (step 4), client library |
| 5 | Write a block of one table as the answer to a range read | server range read (step 4) |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `TableBlockWriter(int compressionLevel = 3, TableBlockLimits limits = null)` | `src/api/HSMTableBlocks/TableBlockWriter.cs` | `IDisposable` (holds a zstd context). Level within ZstdSharp's min..max (−131072..22). |
| `BeginTable(ReadOnlySpan<byte> tableId, uint schemaId, int rowCount)` then `AddTimeColumn(ReadOnlySpan<long>)`, `AddInt64Column(ReadOnlySpan<long>)`, `AddDoubleColumn(ReadOnlySpan<double>)` | same | The first column must be time. Values are copied at once; the caller's arrays can be reused. Input outside the rules or the limits throws `ArgumentException` (or `InvalidOperationException` for a call out of order); after any exception call `Reset()`. |
| `AddTimeColumn(ReadOnlySpan<long> ticks, ReadOnlySpan<byte> nulls)`, `AddInt64Column(ReadOnlySpan<long>, ReadOnlySpan<byte> nulls)`, `AddDoubleColumn(ReadOnlySpan<double>, ReadOnlySpan<byte> nulls)` | same | A column with nulls. `nulls` is empty (no nulls: exactly the overload without it) or a map of `TableNullMap.ByteLength(rowCount)` bytes in the bit order of `TableNullMap`, padding bits zero (`ArgumentException` otherwise). Values of null rows are ignored and written as 0; a null time is not range-checked. A map with no row set writes no map. `AddTimeColumn` with a non-empty map for the first column throws `InvalidOperationException`. Nothing is allocated per call once the writer's buffers have grown (pinned on net8.0). |
| `TableNullMap.ByteLength(rows)`, `IsNull(map, row)`, `SetNull(map, row)` | `src/api/HSMTableBlocks/TableNullMap.cs` | The bit order of a null map, in one place. |
| `byte[] ToArray()`, `Reset()`, `TableCount`, `RowCount`, `DecompressedLength` | same | `ToArray` throws `ArgumentException` when two tables share an identifier. `DecompressedLength` lets the caller split before a limit is reached. |
| `TableBlockReader.ReadHeader(ReadOnlySpan<byte>, TableBlockLimits = null) -> TableBlockHeader` | `src/api/HSMTableBlocks/TableBlockReader.cs` | Verifies the header, checksum, limits and frame shape; no decompression. |
| `TableBlockReader.Read(ReadOnlySpan<byte>, TableBlockLimits = null) -> TableBlock` | same | Header and every `BlockTable` in directory order. |
| `TableBlockReader.ReadTable(ReadOnlySpan<byte>, ReadOnlySpan<byte> tableId, TableBlockLimits = null) -> BlockTable` | same | One table, or null if the block has none with that identifier. Verifies the whole block; copies out only that table's columns. |
| `TableBlockReader.ReadDirectory(ReadOnlySpan<byte>, TableBlockLimits = null) -> TableBlockDirectory` | same | The header and every `TableDirectoryEntry` (identifier, schema id, row count, column types, `HasNulls(column)`) in directory order. Verifies the whole block exactly as `Read` does, values, null maps and times included, but copies no column: what the server's ingest check needs, so a malformed map is refused at the put. `ColumnTypes` are the plain types (the null flag cleared): they compare with a schema's as they are. |
| `BlockColumn`: `Type`, `Int64Values` or `DoubleValues`, `NullMap`, `HasNulls`, `IsNull(row)` | `src/api/HSMTableBlocks/TableBlock.cs` | `NullMap` is the column's map as the block carries it (a copy, the caller's), null when the column has no null in this block: nothing is allocated for it. `Type` is the plain type. A null row holds 0 in the value array. |
| `TableSchema(IEnumerable<TableSchemaColumn>, TableBlockLimits = null)`: `Id`, `Columns`, `ColumnTypes`, `CanonicalBytes`, `HasColumnTypes(types)`, `SameAs(other)`; `TableSchemaColumn(name, type)` | `src/api/HSMTableBlocks/TableSchema.cs` | The schema of a table sensor and its id (section "Table schema"). Throws `ArgumentException` for a schema outside the rules. |
| `TableBlockLimits(maxDecompressedBytes, maxTables, maxRows, maxColumnsPerTable, maxTableIdLength)` | `src/api/HSMTableBlocks/TableBlockLimits.cs` | All optional; `TableBlockLimits.Default`. |
| `TableBlockFormatException.Error : TableBlockError` | `src/api/HSMTableBlocks/TableBlockFormatException.cs` | Derives from `IOException`. |
| `TableColumnType { Time = 1, Int64 = 2, Double = 3 }` | `src/api/HSMTableBlocks/TableColumnType.cs` | The numbers are format constants. |

## Byte Layout (format version 1)

All integers are little-endian. Offsets are in bytes.

### Header (44 bytes)

| Offset | Width | Field | Rule |
|---|---|---|---|
| 0 | 4 | magic | ASCII `HTBK` (`48 54 42 4B`). Same meaning in every version. |
| 4 | 1 | format version | `1`. Same position in every version. |
| 5 | 3 | reserved | zero |
| 8 | 4 | row count (int32) | sum of the directory's row counts; ≥ table count |
| 12 | 4 | table count (int32) | number of directory entries; ≥ 1 |
| 16 | 8 | first time (int64 ticks) | minimum over the first (time) column of every table |
| 24 | 8 | last time (int64 ticks) | maximum over the first column of every table; first ≤ last, both within `0..3155378975999999999` |
| 32 | 4 | compressed length (int32) | length of the zstd frame; ≥ 1; the block is exactly `44 + compressed length` bytes |
| 36 | 4 | decompressed length (int32) | length of the payload (directory, columns and null maps); ≥ 1 |
| 40 | 4 | checksum (uint32) | CRC-32 (IEEE 802.3, the zlib/gzip CRC: polynomial `0x04C11DB7` reflected, init `0xFFFFFFFF`, final xor `0xFFFFFFFF`; check value of `"123456789"` is `0xCBF43926`) over header bytes 0..39 followed by the compressed bytes 44..end |

### Frame

Bytes 44..end are exactly one zstd frame (RFC 8878): it starts with the frame magic `28 B5 2F FD`, no skippable frame, nothing after it. Its frame content size may be absent; if present it must equal the decompressed length. The frame decompresses to exactly the decompressed length. A zstd checksum inside the frame is allowed and not required. The library writes level 3 by default.

### Payload (decompressed)

```
directory: table count entries, in order
  uint16  idLength           1..65535 (limit applies)
  byte[]  id                 idLength opaque bytes, unique in the block
  uint32  schemaId           opaque to the format
  int32   rowCount           >= 1
  uint8   columnCount        >= 1 (limit applies)
  uint8[] columnTypes        columnCount bytes: type | flag
                               type (bits 0..6): 1 time, 2 int64, 3 double; the first column's is 1
                               flag (bit 7, 0x80): this column of this table has a null map in this block;
                               never on the first column; set only when the column has at least one null
columns, column-major: for pass c = 0, 1, 2, ... up to the largest column count,
  for each table in directory order that has a column c (columnCount > c),
    its column c: rowCount values of 8 bytes, little-endian
    (int64 two's complement, or IEEE 754 binary64 bits); a null cell is 8 zero bytes
null maps, in the same order: for pass c = 1, 2, ... up to the largest column count,
  for each table in directory order that has a column c whose type byte carries the flag,
    its map: ceil(rowCount / 8) bytes; bit i (byte i / 8, bit i % 8, least significant first) set = row i is null;
    at least one bit set; the bits from rowCount up (padding) are zero
```

There is no padding and no alignment: the columns start right after the last directory entry, the null maps right after the last column, and the payload ends exactly after the last map (after the last column when no column is flagged). Column *c* of table *t* starts at `directoryLength + Σ(passes p < c: Σ(tables u with columnCount > p: rowCount_u × 8)) + Σ(tables u before t with columnCount > c: rowCount_u × 8)`, whether or not the block has maps. The map of column *c* of table *t* starts at `directoryLength + (all column bytes) + Σ(maps of earlier (pass, table) pairs in the order above)`. A table simply does not appear in the passes beyond its last column. Every time column (in any position) holds ticks within `0..DateTime.MaxValue.Ticks` (`3155378975999999999`); a null cell of a later time column holds 0, which is within that range.

Example: one table `"a"` (schema 7) with rows (t1, 1.5) and (t2, −0.0) is the directory `01 00 61 07 00 00 00 02 00 00 00 02 01 03` (14 bytes), then `t1 t2` as two int64 and `1.5 −0.0` as two doubles: a 46-byte payload. With a second table `"b"` of one row (t3) and only a time column, the columns are `t1 t2 t3` (pass 0) and then `1.5 −0.0` (pass 1).

Example with a null: see "What a writer in another language must do" for a byte-for-byte payload.

**Why the maps sit at the end and not before their columns.** Measured on a synthetic block shaped like the feed (table "Size of null maps"), a map placed before its column is no smaller (0.4% larger at 5% nulls, the same at 0.05%), and the end region keeps every column offset what it is without nulls: a reader computes the columns exactly as before, and a writer emits the maps in a second loop over the same (pass, table) order.

**Why a reader refuses an empty map and a non-zero null cell.** So that one set of rows has one payload (conformance compares decompressed payloads), and a writer that sets a stray flag or leaves a value under a null is refused at once. The cost is one pass over each map and 8 bytes per null cell, for flagged columns only; a block without maps costs nothing.

**A reader that does not look at the maps reads 0** for a null cell: the value arrays hold 0 there, and only the map says it is no value. Every consumer of the values has to look at the map.

### Size of null maps

Synthetic (not the real feed): 1,000 tables, about 6,880 rows per block (1 to 26 rows per table, skewed), time, bid and ask from a random walk with a quarter of the time steps going back; nulls independent per cell on bid and ask; zstd 3; mean of 40 blocks with the same rows at every rate. The absolute bytes per row are higher than the real feed's (10.6 against 3.8) because the walk is noisier; the differences are what this measures.

| Null rate | Null cells per block | Maps per block | Block, maps at the end | Maps before their columns | Cost against no nulls |
|---|---|---|---|---|---|
| 0 | 0 | 0 | 73,022 B | 73,022 B | 0 (the same bytes: the golden blocks `mixed` and `single-row` are unchanged) |
| 0.05% (the real feed's rate of placeholders) | 6.4 | 6.4 | 73,036 B | 73,042 B | +14 B, +0.02% |
| 5% | 688 | 514 | 74,405 B | 74,710 B | +1,383 B, +1.89% |

At 5%, the maps alone cost about 1.56% (measured with the true values left under the nulls) and the zeros in the null cells about 0.33% more.

**Why column-major.** Measured on two real hours (#1842), the table-major layout (all columns of one table, then the next table) is 1.5–2.2% larger with three columns and 5.8% larger with five, because like columns of different tables sit next to each other for zstd. Encoding, decoding and taking one table out cost the same in both layouts. Keeping the rows in arrival order costs under 0.1%.

## Table schema

A table sensor's schema (ADR-0021, point 2) is an ordered list of named, typed columns. Its **schema id** is what a block's directory entry carries in `schemaId`: the CRC-32 of the schema's **canonical bytes**, the same CRC-32 as the block checksum (zlib's `crc32()`). A collector and the server compute it independently; it is never exchanged. The server does not trust the id alone: on ingest it also compares the entry's column types with the registered schema.

Rules (`TableSchema` throws `ArgumentException` for any other):

- 1 to `MaxColumnsPerTable` columns (64 by default, 255 at most);
- the first column has the type time (1); every type is 1, 2 or 3;
- every name is non-empty, contains no control character (`char.IsControl`: U+0000..U+001F and U+007F..U+009F), is valid UTF-16 (no lone surrogate), takes at most 255 bytes in UTF-8, and is unique within the schema (ordinal: `Bid` and `bid` are two names).

Canonical bytes, version 1:

```
uint8   canonical version     1
uint8   column count          1..255
per column, in order:
  uint8   type                1 time, 2 int64, 3 double
  uint8   name length         1..255: the bytes of the name in UTF-8
  byte[]  name                UTF-8, no BOM, no Unicode normalization
```

Example: time, bid, ask (double), size (int64) is `01 04 | 01 04 74 69 6D 65 | 03 03 62 69 64 | 03 03 61 73 6B | 02 04 73 69 7A 65`, and its id is `0x8BF94DAC`. A schema of the single column `time` has the id `0x7FF3E67F`. The tests pin both, computed with zlib outside .NET.

Names are not normalized: a precomposed and a decomposed `é` are two names with two ids. Any change to the canonical bytes or to the rules changes ids that collectors already send, so it needs a new canonical version and a server that accepts both.

## Limits

Limits are part of the API. A reader takes them as a parameter (`null` means the defaults); a writer keeps to its own, so a block written with the default limits reads with the default limits. A caller may pass stricter (or, within the format maxima, looser) limits.

| Limit | Default | Format maximum | Checked |
|---|---|---|---|
| decompressed length | 64 MiB | 1 GiB | header, before allocation |
| tables per block | 65,536 | int32 | header, before allocation |
| rows per block | 8,388,608 | int32 | header, before allocation |
| columns per table | 64 | 255 | directory |
| table identifier length | 1,024 bytes | 65,535 | directory |

Before it allocates, the reader also checks that the header's counts fit the declared length: every table needs at least 13 bytes of directory and every row at least 8 bytes. The directory is parsed inside a buffer whose size is already bounded, and all of its size arithmetic, the null maps included, is done in 64 bits. The null maps add nothing sized from a row count: the reader keeps one offset per column type byte of the directory for them, only when the block has a map, and checks the maps in place.

A typical tick (1,000 tables, about 24,000 rows, 3–5 columns) is 0.6–1 MB decompressed.

## Errors

Every refused block throws `TableBlockFormatException` with one of these codes, checked in this order where they overlap: length to magic, magic, version, header length, compressed length, checksum, header fields, limits, frame, decompression, directory (a flag on the first column is found here), null maps, times.

| Code | Meaning |
|---|---|
| `BadMagic` | the first four bytes are not `HTBK` |
| `UnknownVersion` | the version byte is not one this build reads |
| `Truncated` | shorter than 5 bytes, than the header, or than the declared compressed length |
| `LengthMismatch` | bytes after the frame; the frame's content size or its decompressed output differs from the declared length; the counts need more bytes than declared; bytes after the last column or the last null map (also a map whose flag is missing) |
| `ChecksumMismatch` | the CRC-32 does not match |
| `LimitExceeded` | a limit of the table above |
| `CorruptHeader` | reserved bytes not zero, a length or count not positive, fewer rows than tables, first/last time out of range or out of order |
| `CorruptFrame` | not exactly one zstd frame, or the frame does not decompress |
| `CorruptDirectory` | an empty identifier, a table with no rows or no columns, an unknown column type, a first column that is not time |
| `DirectoryOutOfBounds` | the directory, or the columns and null maps it describes, run past the payload (also a flag whose map is missing) |
| `DuplicateTable` | two entries with the same identifier |
| `InvalidTime` | a time value outside `0..DateTime.MaxValue.Ticks` |
| `HeaderMismatch` | the directory's row total, or the minimum and maximum time, differ from the header |
| `CorruptNullMap` | a null flag on the first (time) column; padding bits of a map set; a map with no null row; a null cell whose 8 bytes are not zero. The message names the fault, the table and the column (and the row of a non-zero cell). |

## Versioning

- The format version is byte 4; bytes 0..4 keep their meaning in every version, so any future reader can tell a block's version.
- A reader reads the versions it knows and refuses the others with `UnknownVersion`. Every released version must keep decoding: its golden blocks stay in the repository forever (`src/tests/HSMTableBlocks.Tests/Golden/format{N}/`).
- **A new format version is required** for any change a reader of the current version would decode differently or refuse: a new column type, a new header field or a header of another size, a meaning for the reserved bytes, a different directory entry, padding or alignment, another compression or checksum, a change in a limit's format maximum, a change in what the checksum covers.
- **No new version is needed** for different compressed bytes of the same payload (another zstd library, version or level) or for different default limits.
- The golden tests pin the header fields (bytes 0..31 and 36..39) and the decompressed payload of fixed inputs, not the compressed bytes or the checksum. Payload or header bytes that change for the same input mean a format change.
- Adding a column to an existing sensor is a new schema id, not a new format (ADR-0021, point 2).
- The null flag and the map region (#1859) are part of version 1: they were added before version 1 was released, so no released reader decodes a block differently. A build from before them refuses a block with nulls (`CorruptDirectory`, an unknown column type) and never misreads it. Once a version has shipped, such a change needs a new version.

## What a writer in another language must do

1. Build the payload: the directory entries in table order, then the columns column-major: the time column of every table in directory order, then column 1 of every table that has one, then column 2, and so on; values as 8-byte little-endian (on a big-endian host, swap). Doubles are copied by their bits; never convert through text or another width. A writer that collects columns per table copies them into this order once, when it builds the payload.
2. Keep the rules: at least one table; per table an identifier of 1..65,535 bytes (and within the reader's limit; 1,024 by default), unique in the block; at least one row; at least one column, the first of type time; every column `rowCount` values; time values within `0..3155378975999999999`; at most 255 columns (64 by default).
3. Nulls. For a column other than the first that has at least one null in this block: OR `0x80` into its type byte, write 8 zero bytes in each null cell, and after the last column append its map (`ceil(rowCount / 8)` bytes, bit `i % 8` of byte `i / 8` set for a null row `i`, padding bits zero). The maps follow in the order of the columns: pass 1 (column 1 of every table in directory order), then pass 2, and so on, only the flagged columns. A column without a null gets no flag and no map, even if the application marked it nullable: never write a map with no bit set. The decompressed length covers the maps.
4. Compute the header's row count, table count, first and last time (minimum and maximum of the first column of every table).
5. Compress the payload as one zstd frame (`ZSTD_compress2` or `ZSTD_compress`, level 3). Declaring the content size is recommended (the zstd defaults do it); streaming without it is accepted.
6. Write the 44-byte header, append the frame, then compute CRC-32 (zlib's `crc32()`) over header bytes 0..39 and the frame, and store it at offset 40.
7. Stay within the default limits unless the receiving server is known to accept more; split a tick's rows into several blocks rather than exceed them. The maps count toward the decompressed length.

Two writers need not produce the same bytes: conformance compares decoded rows and the decompressed payload, not compressed bytes (ADR-0021, Consequences). The payload is unique for a set of rows, null maps included.

**Worked example with a null** (pinned by `NullMapTests.The_worked_example_of_the_specification_byte_for_byte` and the first two tables of the golden block `nulls`). Table `"a"` (schema 7): rows (t1, 1.5) and (t2, null). Table `"b"` (schema 7): one row (t3, 2.5). Both have the columns time and double. The payload is 77 bytes:

```
directory  01 00 61 07 00 00 00 02 00 00 00 02 01 83     "a": id length 1, "a", schema 7, 2 rows, 2 columns,
                                                          time, double with a map (0x03 | 0x80)
           01 00 62 07 00 00 00 01 00 00 00 02 01 03     "b": 1 row, time, double without a map
pass 0     t1 t2 t3                                       three int64, little-endian (24 bytes)
pass 1     00 00 00 00 00 00 F8 3F                        a: 1.5
           00 00 00 00 00 00 00 00                        a: row 1 is null: 8 zero bytes
           00 00 00 00 00 00 04 40                        b: 2.5
maps       02                                             a, column 1: bit 1 set = row 1 is null; bits 2..7 padding
```

Without the null (row 1 holding a value), the same tables are 76 bytes: `0x03` in place of `0x83`, the value in place of the zeros, no map: the format from before nulls, unchanged.

**A reader** that does not check a column for nulls reads 0 where a cell is null: it must look at the column's flag and map before it uses a value.

## Key Files

| File | Purpose |
|---|---|
| `src/api/HSMTableBlocks/TableBlockFormat.cs` | Constants, header writer, checksum, value copies |
| `src/api/HSMTableBlocks/TableBlockWriter.cs` | Builds a block from tables |
| `src/api/HSMTableBlocks/TableBlockReader.cs` | Verifies and decodes a block; single-table read |
| `src/api/HSMTableBlocks/TableBlockLimits.cs` | Limits and their defaults |
| `src/api/HSMTableBlocks/TableNullMap.cs` | The bit order of a null map |
| `src/api/HSMTableBlocks/TableBlockFormatException.cs` | The exception and its codes |
| `src/api/HSMTableBlocks/TableSchema.cs` | The table schema, its rules and its id |
| `src/tests/HSMTableBlocks.Tests/` | Round trips, golden blocks, malformed input, fuzz, benchmark |

## Data Flow

Collector (rows per table sensor) → `TableBlockWriter` → block bytes → transport (step 2/3) → `TableBlockReader` on the server → stored as it arrived (step 3) → range read answers blocks of one table (step 4) → `TableBlockReader` in the client library.

## Storage / Persistence

The format is stored as is by the server until the hourly merge (ADR-0021, point 8); this library does not store anything.

## UI / Operator Visibility

Not operator-visible.

## Dependencies

- Depends on: `ZstdSharp.Port` 0.8.8 (zstd, the version the sealed history uses), `System.IO.Hashing` 8.0.0 (CRC-32). Both bring `System.Memory` to netstandard2.0. CRC-32 and not the CRC-32C of the sealed chunks: CRC-32C is only in `System.IO.Hashing` 11.0.0, whose build warns "doesn't support net6.0/net8.0" in every net6.0–net9.0 project that consumes the collector package; 8.0.0 builds without a warning on net472, net6.0 and net8.0.
- Used by: the server (`TableSchema`, #1844); the server's block store and read path, the collectors and the client library in the later steps of #1842.

## Tests

`src/tests/HSMTableBlocks.Tests`, on net8.0 and net472, run by the `HSMServer build` workflow (`dotnet test HSMServer.sln`):

- round trips of every column type with extreme values (NaN payloads, ±Infinity, −0.0, `long.MinValue`/`MaxValue`, time 0 and `DateTime.MaxValue`), one table, a thousand tables, one row, repeated and unordered times, opaque identifiers, compression levels, a frame without a content size;
- the column-major layout pinned against a hand-written payload; tables of different column counts; one table read from the middle of a hand-made block;
- golden blocks `Golden/format1/*.htb` (decode forever; header fields and decompressed payload pinned): `mixed`, `single-row`, and `nulls` (the worked example, a two-byte map with padding, a later time column with a null, a row of nulls only, a column null in every row, a table without nulls between tables with them); `HSM_WRITE_GOLDEN_TABLE_BLOCKS=1` writes the cases whose file is missing and never overwrites one (adding a case to a set is legitimate only while its version is unreleased);
- null maps (`NullMapTests`): every refusal by its code through `Read`, `ReadTable` and `ReadDirectory` (a flag on the time column, padding bits in a one- and a two-byte map, a map with no null, a non-zero null cell including −0.0, a missing map, a byte after the last map, a map without its flag, maps in the wrong order, flagged columns whose size passes 32 bits); a map on a later time column; the worked example byte for byte; round trips with extreme values beside nulls; a block without nulls written through the overloads with empty and all-zero maps is the same bytes; the writer's argument checks and its payload limit with maps; a reused writer forgets the maps; adding columns with nulls on a reused writer allocates nothing (net8.0); `ReadDirectory` gives plain types that pass the schema check and `HasNulls`;
- malformed input, one fault per case with its code: truncation at every length, wrong lengths, header fields out of range, counts that overflow, a directory outside the payload, duplicate identifiers, invalid times, limits, damaged frames, damaged checksum, zip bombs (declared above the limit; frame content size above the declared length; no content size and 100 MB of zeros) with managed allocation bounded on net8.0;
- uniqueness of identifiers at 65,536 tables (the default limit) with 64-byte identifiers that share a 59-byte prefix, valid and with one duplicate, with the number of identifier comparisons counted (not timed) and bounded by 3·n·log2(n): both the writer and the reader sort by identifier bytes and compare neighbours, O(n log n) comparisons whatever the identifiers are (no unkeyed hash a client could collide). The test hands the writer and the reader an internal `IdComparisonCounter`; production code passes none;
- `ReadDirectory`: every malformed-block case and the fuzz test check that it refuses with the same code as `Read`, and gives the entries of every block `Read` accepts;
- the table schema: canonical bytes and ids pinned against zlib's crc32 (ASCII and Cyrillic names), every rule refused, the column limit;
- a fuzz test with a fixed seed: 20,000 mutations (raw bytes, header fields re-checksummed, payload re-compressed; one source block carries null maps) either read as a valid block or throw `TableBlockFormatException`; a block that reads, written again, gives the same payload;
- a benchmark (`Category=Benchmark`, runs only with `HSM_TABLE_BLOCK_BENCH=1`).

## Known Issues / Limitations

- Reading one table decompresses the whole block: the cost is the block's, not the table's (ADR-0021).
- The block carries no sequence number; deduplication of a block sent twice (ADR-0021, point 6) belongs to the transport step.
- The format treats a table identifier as opaque bytes. The server reads it as the sensor path in UTF-8 (#1844, `aicontext/features/server/table-sensors/feature.md`).
