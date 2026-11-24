# Feature: Table block format

> Owner: api | Last reviewed: 2026-11-24 | Canonical: yes
> Scope: the byte format of a table block (ADR-0021, points 2, 3 and 5; step 1 of epic #1842, #1843), its limits, its error codes, its versioning rule, and the library `HSMTableBlocks` that writes and reads it.

---

## Overview

A **block** is what a collector sends for table sensors at every tick: the rows of all its table sensors, grouped by table (glossary: Block). It is the only table format outside the server: the managed collector writes it, the server reads it on ingest and writes it when it answers a range read, and the client library reads it. A future C++ collector writes the same format with nothing but zstd.

A block is a fixed 44-byte header followed by exactly one zstd frame. Inside the frame: a directory with one entry per table (identifier, schema id, row count, column types), then the columns as raw fixed-width little-endian arrays, column-major: column 0 of every table, then column 1 of every table that has one, and so on. No HSM column coders are used.

`HSMTableBlocks` (`src/api/HSMTableBlocks`, netstandard2.0) is a pure library: no storage, no transport, no server or collector dependency. Nothing uses it yet; the collector (step 2), the server ingest (step 3) and the read path (step 4) will.

This document is the format specification: it is meant to be precise enough to write a second writer or reader from it.

## Invariants

- **Fidelity.** Every value reads back bit for bit: 64-bit integers, time ticks, and doubles by their IEEE 754 bits (NaN payloads, ±Infinity and -0.0 included).
- **Order as given.** Within a table the rows keep the order the writer was given. A block may hold rows out of time order and repeated times; neither the writer nor the reader sorts or requires order (ADR-0021, point 3: the server's merge sorts). Tables keep the order they were added.
- **Dense columns.** Every column of a table holds exactly `rowCount` values; there are no nulls.
- **No empty blocks.** A block holds at least one table and every table at least one row and one column. A tick with no rows sends no block. This keeps the header's first and last time always defined.
- **Unique identifiers.** A table identifier appears at most once in a block. Writer (in `ToArray`) and reader check it by sorting the identifiers bytewise and comparing neighbours: O(n log n) comparisons for any input.
- **Fail loudly, never partially.** Every read verifies the whole block and throws `TableBlockFormatException` (one type, with a `TableBlockError` code) for anything wrong. It never returns a partial block. Block bytes are untrusted input: every read is bounds-checked.
- **Bounded memory.** The limits (below) are checked from the header before anything is allocated or decompressed. Decompression writes into a buffer of exactly the declared decompressed length and stops there: a frame that would produce more is refused. A reader allocates at most the declared decompressed length (rented) plus the arrays it returns.
- **Thread safety.** `TableBlockReader` is stateless and safe from any thread. A `TableBlockWriter` is not thread-safe: one instance per thread (or per tick loop); it is meant to be reused with `Reset()`. `TableBlockLimits` is immutable.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Write the rows of every table sensor of a tick into one block | collector (step 2) |
| 2 | Read a block's header to check its size and limits before anything else | server ingest (step 3) |
| 3 | Read every table of a block to check it against the registered sensors and store it | server ingest (step 3) |
| 4 | Read one table out of a block | server range read (step 4), client library |
| 5 | Write a block of one table as the answer to a range read | server range read (step 4) |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `TableBlockWriter(int compressionLevel = 3, TableBlockLimits limits = null)` | `src/api/HSMTableBlocks/TableBlockWriter.cs` | `IDisposable` (holds a zstd context). Level within ZstdSharp's min..max (−131072..22). |
| `BeginTable(ReadOnlySpan<byte> tableId, uint schemaId, int rowCount)` then `AddTimeColumn(ReadOnlySpan<long>)`, `AddInt64Column(ReadOnlySpan<long>)`, `AddDoubleColumn(ReadOnlySpan<double>)` | same | The first column must be time. Values are copied at once; the caller's arrays can be reused. Input outside the rules or the limits throws `ArgumentException` (or `InvalidOperationException` for a call out of order); after any exception call `Reset()`. |
| `byte[] ToArray()`, `Reset()`, `TableCount`, `RowCount`, `DecompressedLength` | same | `ToArray` throws `ArgumentException` when two tables share an identifier. `DecompressedLength` lets the caller split before a limit is reached. |
| `TableBlockReader.ReadHeader(ReadOnlySpan<byte>, TableBlockLimits = null) -> TableBlockHeader` | `src/api/HSMTableBlocks/TableBlockReader.cs` | Verifies the header, checksum, limits and frame shape; no decompression. |
| `TableBlockReader.Read(ReadOnlySpan<byte>, TableBlockLimits = null) -> TableBlock` | same | Header and every `BlockTable` in directory order. |
| `TableBlockReader.ReadTable(ReadOnlySpan<byte>, ReadOnlySpan<byte> tableId, TableBlockLimits = null) -> BlockTable` | same | One table, or null if the block has none with that identifier. Verifies the whole block; copies out only that table's columns. |
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
| 36 | 4 | decompressed length (int32) | length of the payload (directory and columns); ≥ 1 |
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
  uint8[] columnTypes        columnCount bytes: 1 time, 2 int64, 3 double; the first is 1
columns, column-major: for pass c = 0, 1, 2, ... up to the largest column count,
  for each table in directory order that has a column c (columnCount > c),
    its column c: rowCount values of 8 bytes, little-endian
    (int64 two's complement, or IEEE 754 binary64 bits)
```

There is no padding and no alignment: the columns start right after the last directory entry, and the payload ends exactly after the last column. Column *c* of table *t* starts at `directoryLength + Σ(passes p < c: Σ(tables u with columnCount > p: rowCount_u × 8)) + Σ(tables u before t with columnCount > c: rowCount_u × 8)`. A table simply does not appear in the passes beyond its last column. Every time column (in any position) holds ticks within `0..DateTime.MaxValue.Ticks` (`3155378975999999999`).

Example: one table `"a"` (schema 7) with rows (t1, 1.5) and (t2, −0.0) is the directory `01 00 61 07 00 00 00 02 00 00 00 02 01 03` (14 bytes), then `t1 t2` as two int64 and `1.5 −0.0` as two doubles: a 46-byte payload. With a second table `"b"` of one row (t3) and only a time column, the columns are `t1 t2 t3` (pass 0) and then `1.5 −0.0` (pass 1).

**Why column-major.** Measured on two real hours (#1842), the table-major layout (all columns of one table, then the next table) is 1.5–2.2% larger with three columns and 5.8% larger with five, because like columns of different tables sit next to each other for zstd. Encoding, decoding and taking one table out cost the same in both layouts. Keeping the rows in arrival order costs under 0.1%.

## Limits

Limits are part of the API. A reader takes them as a parameter (`null` means the defaults); a writer keeps to its own, so a block written with the default limits reads with the default limits. A caller may pass stricter (or, within the format maxima, looser) limits.

| Limit | Default | Format maximum | Checked |
|---|---|---|---|
| decompressed length | 64 MiB | 1 GiB | header, before allocation |
| tables per block | 65,536 | int32 | header, before allocation |
| rows per block | 8,388,608 | int32 | header, before allocation |
| columns per table | 64 | 255 | directory |
| table identifier length | 1,024 bytes | 65,535 | directory |

Before it allocates, the reader also checks that the header's counts fit the declared length: every table needs at least 13 bytes of directory and every row at least 8 bytes. The directory is parsed inside a buffer whose size is already bounded, and all of its size arithmetic is done in 64 bits.

A typical tick (1,000 tables, about 24,000 rows, 3–5 columns) is 0.6–1 MB decompressed.

## Errors

Every refused block throws `TableBlockFormatException` with one of these codes, checked in this order where they overlap: length to magic, magic, version, header length, compressed length, checksum, header fields, limits, frame, decompression, directory, times.

| Code | Meaning |
|---|---|
| `BadMagic` | the first four bytes are not `HTBK` |
| `UnknownVersion` | the version byte is not one this build reads |
| `Truncated` | shorter than 5 bytes, than the header, or than the declared compressed length |
| `LengthMismatch` | bytes after the frame; the frame's content size or its decompressed output differs from the declared length; the counts need more bytes than declared; bytes after the last column |
| `ChecksumMismatch` | the CRC-32 does not match |
| `LimitExceeded` | a limit of the table above |
| `CorruptHeader` | reserved bytes not zero, a length or count not positive, fewer rows than tables, first/last time out of range or out of order |
| `CorruptFrame` | not exactly one zstd frame, or the frame does not decompress |
| `CorruptDirectory` | an empty identifier, a table with no rows or no columns, an unknown column type, a first column that is not time |
| `DirectoryOutOfBounds` | the directory or the columns it describes run past the payload |
| `DuplicateTable` | two entries with the same identifier |
| `InvalidTime` | a time value outside `0..DateTime.MaxValue.Ticks` |
| `HeaderMismatch` | the directory's row total, or the minimum and maximum time, differ from the header |

## Versioning

- The format version is byte 4; bytes 0..4 keep their meaning in every version, so any future reader can tell a block's version.
- A reader reads the versions it knows and refuses the others with `UnknownVersion`. Every released version must keep decoding: its golden blocks stay in the repository forever (`src/tests/HSMTableBlocks.Tests/Golden/format{N}/`).
- **A new format version is required** for any change a reader of the current version would decode differently or refuse: a new column type, a new header field or a header of another size, a meaning for the reserved bytes, a different directory entry, padding or alignment, another compression or checksum, a change in a limit's format maximum, a change in what the checksum covers.
- **No new version is needed** for different compressed bytes of the same payload (another zstd library, version or level) or for different default limits.
- The golden tests pin the header fields (bytes 0..31 and 36..39) and the decompressed payload of fixed inputs, not the compressed bytes or the checksum. Payload or header bytes that change for the same input mean a format change.
- Adding a column to an existing sensor is a new schema id, not a new format (ADR-0021, point 2).

## What a writer in another language must do

1. Build the payload: the directory entries in table order, then the columns column-major: the time column of every table in directory order, then column 1 of every table that has one, then column 2, and so on; values as 8-byte little-endian (on a big-endian host, swap). Doubles are copied by their bits; never convert through text or another width. A writer that collects columns per table copies them into this order once, when it builds the payload.
2. Keep the rules: at least one table; per table an identifier of 1..65,535 bytes (and within the reader's limit; 1,024 by default), unique in the block; at least one row; at least one column, the first of type time; every column `rowCount` values; time values within `0..3155378975999999999`; at most 255 columns (64 by default).
3. Compute the header's row count, table count, first and last time (minimum and maximum of the first column of every table).
4. Compress the payload as one zstd frame (`ZSTD_compress2` or `ZSTD_compress`, level 3). Declaring the content size is recommended (the zstd defaults do it); streaming without it is accepted.
5. Write the 44-byte header, append the frame, then compute CRC-32 (zlib's `crc32()`) over header bytes 0..39 and the frame, and store it at offset 40.
6. Stay within the default limits unless the receiving server is known to accept more; split a tick's rows into several blocks rather than exceed them.

Two writers need not produce the same bytes: conformance compares decoded rows and the decompressed payload, not compressed bytes (ADR-0021, Consequences).

## Key Files

| File | Purpose |
|---|---|
| `src/api/HSMTableBlocks/TableBlockFormat.cs` | Constants, header writer, checksum, value copies |
| `src/api/HSMTableBlocks/TableBlockWriter.cs` | Builds a block from tables |
| `src/api/HSMTableBlocks/TableBlockReader.cs` | Verifies and decodes a block; single-table read |
| `src/api/HSMTableBlocks/TableBlockLimits.cs` | Limits and their defaults |
| `src/api/HSMTableBlocks/TableBlockFormatException.cs` | The exception and its codes |
| `src/tests/HSMTableBlocks.Tests/` | Round trips, golden blocks, malformed input, fuzz, benchmark |

## Data Flow

Collector (rows per table sensor) → `TableBlockWriter` → block bytes → transport (step 2/3) → `TableBlockReader` on the server → stored as it arrived (step 3) → range read answers blocks of one table (step 4) → `TableBlockReader` in the client library.

## Storage / Persistence

The format is stored as is by the server until the hourly merge (ADR-0021, point 8); this library does not store anything.

## UI / Operator Visibility

Not operator-visible.

## Dependencies

- Depends on: `ZstdSharp.Port` 0.8.8 (zstd, the version the sealed history uses), `System.IO.Hashing` 8.0.0 (CRC-32). Both bring `System.Memory` to netstandard2.0. CRC-32 and not the CRC-32C of the sealed chunks: CRC-32C is only in `System.IO.Hashing` 11.0.0, whose build warns "doesn't support net6.0/net8.0" in every net6.0–net9.0 project that consumes the collector package; 8.0.0 builds without a warning on net472, net6.0 and net8.0.
- Used by: nothing yet; the collector, server and client library in steps 2–4 of #1842.

## Tests

`src/tests/HSMTableBlocks.Tests`, on net8.0 and net472, run by the `HSMServer build` workflow (`dotnet test HSMServer.sln`):

- round trips of every column type with extreme values (NaN payloads, ±Infinity, −0.0, `long.MinValue`/`MaxValue`, time 0 and `DateTime.MaxValue`), one table, a thousand tables, one row, repeated and unordered times, opaque identifiers, compression levels, a frame without a content size;
- the column-major layout pinned against a hand-written payload; tables of different column counts; one table read from the middle of a hand-made block;
- golden blocks `Golden/format1/*.htb` (decode forever; header fields and decompressed payload pinned); `HSM_WRITE_GOLDEN_TABLE_BLOCKS=1` writes a new set and refuses to overwrite one;
- malformed input, one fault per case with its code: truncation at every length, wrong lengths, header fields out of range, counts that overflow, a directory outside the payload, duplicate identifiers, invalid times, limits, damaged frames, damaged checksum, zip bombs (declared above the limit; frame content size above the declared length; no content size and 100 MB of zeros) with managed allocation bounded on net8.0;
- uniqueness of identifiers at 65,536 tables (the default limit) with 64-byte identifiers that share a 59-byte prefix, valid and with one duplicate, with the number of identifier comparisons counted (not timed) and bounded by 3·n·log2(n): both the writer and the reader sort by identifier bytes and compare neighbours, O(n log n) comparisons whatever the identifiers are (no unkeyed hash a client could collide);
- a fuzz test with a fixed seed: 20,000 mutations (raw bytes, header fields re-checksummed, payload re-compressed) either read as a valid block or throw `TableBlockFormatException`;
- a benchmark (`Category=Benchmark`, runs only with `HSM_TABLE_BLOCK_BENCH=1`).

## Known Issues / Limitations

- Reading one table decompresses the whole block: the cost is the block's, not the table's (ADR-0021).
- No per-row null: what stands for "no value" is the application's choice (ADR-0021, Consequences).
- The block carries no sequence number; deduplication of a block sent twice (ADR-0021, point 6) belongs to the transport step.
- Which identifier a table carries (sensor path or a server-issued id) is decided in a later step; the format treats it as opaque bytes.
