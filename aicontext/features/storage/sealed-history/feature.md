# Feature: Sealed history chunk format

> Owner: storage | Last reviewed: 2026-11-19 | Canonical: yes
> Scope: the byte format of a chunk of sealed sensor history (ADR-0020, step 1 of epic #1800), its fidelity rules, its versioning rule, and when the encoder refuses; the sealed week store that reads and changes a sealed week (step 2, #1803); the sealing job that seals, folds and unseals weeks (step 3, #1804).

---

## Overview

A **chunk** is a bounded run of one sensor's history rows encoded as one value: a fixed header, then column streams compressed with zstd. The codec (`HSMDatabase.SealedHistory.ChunkCodec`) is a pure library: it encodes rows into chunks and decodes chunks back into rows. It does not store anything: the sealed week store (#1803, section below) keeps and reads chunks, and the sealing job (#1804, section below) seals closed weeks, removes their source rows after a keep period, folds late values into the chunks and unseals weeks.

A row is `(Key, Value)`: `Key` is the tick part of the storage key `sensorId | tick`, `Value` one of the `BaseValue` records from `HSMCommon`. File values are never sealed.

This document is the format specification: it is meant to be precise enough to write a second decoder from it.

## Invariants

- **Fidelity.** A decoded row has the same key, the same value record type and every persisted field bit for bit equal to its source (doubles by their IEEE 754 bits: NaN payloads, -0.0 and infinities included; `DateTime` by ticks and kind bits; null and empty strings distinct; null and absent members distinct), with one exception: `ReceivingTime` is stored as `d = round((ReceivingTime - Time) / 1 ms)`, half away from zero, and rebuilt as `Time + d * 1 ms` with the source's kind bits. It moves by at most 5,000 ticks and never to the other side of `Time`. `RowFidelity.FindDifference` is this rule as code.
- **Padding is not a field.** MemoryPack writes a `Nullable<T>` member as its raw struct, padding bytes included, and those bytes can hold leftovers of the writer's memory (seen in production: `IntegerBarValue.FirstValue`, 1,788 of 10,074 rows of the measured shard). The same holds for the value field of a null `Nullable<T>`. The codec keeps the value, not the padding, so the MemoryPack bytes of a decoded row can differ from the source blob while every field is equal; values built in code can carry such leftovers too. Verification compares fields (`RowFidelity`), not blobs.
- **Refuse, never approximate.** `Encode` checks the whole input first; if any row cannot be represented exactly it returns a refusal (reason and row index) and no chunk. It never throws for an unsupported row, an oversized payload included.
- **Whatever `Encode` accepts, `Decode` returns.** Every range the decoder checks has an encoder-side refusal (DateTime ticks, bool and `hasValue` bytes, UTF-16 validity, payload size); a randomized test over raw-byte-patched MemoryPack rows states the contract directly.
- **Fail loudly.** `Decode` and `ReadHeader` throw `ChunkFormatException` for an unknown version, a bad checksum, a truncated or a corrupt chunk. They never return an empty or partial list, and every read is bounds-checked: chunk bytes are untrusted input.
- **Versioned bytes.** A change a current reader could not decode is a new format version; a change to the bytes written that it still decodes is a new encoder revision (see Versioning). Every released format version keeps decoding. Bytes 0..2 of a chunk (magic and version) have the same meaning in every version.
- **Thread safety.** Encode and decode share no mutable state and are safe to call concurrently.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Encode the rows of one sensor in one closed week into chunks | sealing job (#1804) |
| 2 | Read a chunk's header (row count, first and last key) to plan reads and deletes | sealed week store (#1803) |
| 3 | Decode a chunk on a history read | sealed week store (#1803) |
| 4 | Verify a seal: decode and compare every row with its source | sealing job (#1804) |
| 5 | Seal a closed week, remove its source rows after the keep period, fold late values, unseal | sealing job (#1804), operator (unseal) |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `ChunkCodec.Encode(IReadOnlyList<HistoryRow>) -> ChunkEncodeResult` | `src/database/HSMDatabase.SealedHistory/ChunkCodec.cs` | Rows of one sensor, keys strictly ascending. Chunks in key order, or a `ChunkRefusal`. |
| `ChunkCodec.Decode(ReadOnlySpan<byte>) -> List<HistoryRow>` | same | Throws `ChunkFormatException` (`Error`: `NotAChunk`, `UnknownVersion`, `Truncated`, `ChecksumMismatch`, `Corrupt`). |
| `ChunkCodec.ReadHeader(ReadOnlySpan<byte>) -> ChunkHeader` | same | Verifies magic, version, length, checksum and header fields; does not decompress. `ChunkHeader.EncoderRevision` is header byte 5 as stored, any value. |
| `ChunkCodec.FormatVersion`, `ChunkCodec.EncoderRevision` | same | What this build writes: format version 1, encoder revision 1. |
| `RowFidelity.FindDifference(source, decoded)` | `src/database/HSMDatabase.SealedHistory/RowFidelity.cs` | The fidelity rule; returns the first differing field or null. |
| Chunk byte format v1 | this document | Compatibility-sensitive: a permanent storage format. |

## Byte format, version 1

All integers in the header are little-endian.

### Header (40 bytes)

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | Magic `0x48 0x43` (`"HC"`). |
| 2 | 1 | Format version, `1`. |
| 3 | 1 | Value type: the `SensorType` byte (`Boolean 0, Integer 1, Double 2, String 3, IntegerBar 4, DoubleBar 5, TimeSpan 7, Version 8, Rate 9, Enum 10`; `File 6` is invalid). |
| 4 | 1 | Compression: `0` = payload stored as is, `1` = payload is exactly one zstd frame (RFC 8878) that declares its content size. |
| 5 | 1 | Encoder revision: which revision of the encoder wrote the chunk (`1` today). Informational: a reader accepts any value 0..255 and decodes the same way whatever it holds. |
| 6 | 2 | Reserved, zero. A reader rejects a non-zero byte here as `Corrupt`. |
| 8 | 4 | Row count n, int32, `1 <= n <= 4096`. |
| 12 | 4 | Payload length (uncompressed), int32, `1 ..= 67,108,864`. With compression 0 it equals the stored length. |
| 16 | 4 | Stored length, int32: the chunk is exactly `40 + stored length` bytes. |
| 20 | 8 | First key, int64 (the key of row 0). |
| 28 | 8 | Last key, int64 (the key of row n-1); `first <= last`, equal when n = 1. |
| 36 | 4 | CRC-32C (Castagnoli: reflected polynomial `0x82F63B78`, initial value and final XOR `0xFFFFFFFF`; `"123456789"` gives `0xE3069283`) over bytes 0..35 followed by bytes 40..end. |
| 40 | stored length | The stored payload. |

A reader checks, in this order: at least 3 bytes (else `Truncated`), magic (`NotAChunk`), version (`UnknownVersion`), at least 40 bytes and the stored length against the actual length (`Truncated` when shorter, `Corrupt` when longer), the checksum (`ChecksumMismatch`), then the field ranges above (`Corrupt`). With compression 1 it then checks, before allocating anything for decompression, that the stored bytes start with the zstd frame magic `0xFD2FB528` (a skippable frame is rejected), that the frame header declares a content size equal to the payload length, and that the frame spans exactly the stored bytes (concatenated frames, a trailing skippable frame or trailing bytes are rejected); all `Corrupt`. After decompression the payload must be exactly the declared length, must be consumed exactly, and its first and last keys must equal the header's. The encoder revision (byte 5) is covered by the checksum like every other header byte but is not range-checked. The zstd compressed bytes for a given payload are those of the pinned `ZstdSharp.Port` version at level 3; a library upgrade that changes them is not a format change, since any valid single frame decodes, but it is a new encoder revision.

### Versioning

Two numbers, with different rules. The **format version** (byte 2) changes only when a reader of the current version could not decode the new chunks correctly: a new column encoding, a changed layout, a changed meaning of a field. A reader refuses a format version it does not know (`UnknownVersion`), and every reader keeps decoding every older format version forever. The **encoder revision** (byte 5) changes whenever the bytes the encoder writes for the same input change while a reader of the same format version still decodes them: a better choice among the existing column encodings, other parameters, a compressor upgrade that changes its output. It records which encoder wrote a chunk (to find chunks worth re-encoding, to trace a bad chunk to its encoder); readers ignore it. Golden chunks are kept per encoder in `Golden/format{F}-encoder{R}/`: every set of a released encoder revision stays in the repository as a decode test forever, and only the set of the current format version and encoder revision is also an encode test ("the encoder writes exactly these bytes"). Until the first release there is one set, `format1-encoder1`.

### Payload primitives

- **varint**: unsigned LEB128, at most 10 bytes; the 10th byte may only be 0 or 1 (reject otherwise). Non-minimal forms such as `0x80 0x00` for 0 are accepted.
- **zigzag**: `z = (v << 1) ^ (v >> 63)` (arithmetic shift), inverse `v = (z >> 1) ^ -(z & 1)`.
- **bit stream**: bits are written most significant first, filling each byte from its high bit; the last byte is padded with zero bits. A reader requires the stream to be consumed to its last byte and the padding to be zero.
- **Wrapping and range checks.** Residual integration (integer columns) and the offsets `Time - Key`, `LastReceivingTime - Time` and `CloseTime - OpenTime` wrap modulo 2^64; the resulting DateTime ticks are then range-checked (`0 ..= DateTime.MaxValue.Ticks`, reject otherwise). `ReceivingTime` is not wrapped: a step count d with `|d| > 315,537,897,600,000` (one more than `MaxValue.Ticks / 10,000`) is rejected, and `Time + d * 10,000` is then range-checked like any DateTime.

### Integer column (n >= 1 values, int64)

First byte = mode.

- `0` constant: one zigzag varint; every row has this value.
- `1` varint: one byte `order`, then n zigzag varints of the residual series.
- `2` bits: one byte `order`, a varint byte length L, then L bytes of bit stream holding n bucket codes of the zigzag residuals.

`order` (0, 1 or 2) defines the residual series r from the values v: `0` raw, `r[i] = v[i]`; `1` first difference, `r[0] = v[0]`, `r[i] = v[i] - v[i-1]`; `2` second difference, `r[0] = v[0]`, `r[1] = v[1] - v[0]`, `r[i] = (v[i] - v[i-1]) - (v[i-1] - v[i-2])`.

Bucket code: bucket widths `0, 4, 8, 12, 16, 24, 32, 64` bits for buckets 0..7. Bucket k < 7 is announced by k one-bits and a zero-bit, bucket 7 by seven one-bits; then the zigzag value in the bucket's width (bucket 0 carries no value bits: zero). A value in a larger bucket than it needs is accepted. An unknown mode or order is rejected.

Encoder choice (encoder revision 1, pinned by its golden set; a different choice that writes the same encodings is a new encoder revision, not a new format version): constant if all values are equal. Otherwise the order with the fewest varint bytes (ties: lower order) and the order with the fewest bits (ties: lower order); the bit stream is written when `2 + len(varint(L)) + L` is strictly smaller than `2 + varint bytes`, else the varints. The encoder always uses the smallest bucket that fits.

### Double column (n >= 1 values, handled as their 64 raw bits)

First byte = mode.

- `0` constant: 8 bytes, the raw bits little-endian.
- `1` XOR bytes: for each value, `x = bits[i] XOR bits[i-1]` (`bits[-1] = 0`). `x = 0` is the byte `0xFF`; otherwise one control byte `(lz << 4) | tz`, where lz and tz are the counts of whole zero bytes at the top and bottom of x, then bytes `tz .. 7-lz` of x, least significant first. A control byte other than `0xFF` with `lz + tz > 7` is rejected; smaller lz or tz than the true counts (zero bytes written out) are accepted.
- `2` decimal: one byte e (0..15, reject otherwise), then an integer column of mantissas m; value `i` is `(double)m[i] / 10^e`: the int64 to double conversion rounds to nearest, ties to even, then IEEE 754 division rounds to nearest, ties to even; 10^e is exact. Any int64 mantissa is accepted, including `|m| > 2^53` (the conversion then rounds), though the encoder never writes one.
- `3` Gorilla: a varint byte length L, then L bytes of bit stream: the first value in 64 bits; then per value `0` = same as previous; `10` + the `64 - lead - trail` meaningful bits of the XOR inside the previous window; `11` + 5 bits leading zeros (capped at 31) + 6 bits meaningful length (0 means 64) + the meaningful bits, which also become the window. Rejected: `10` before any window is defined, and a window with `leading + length > 64`.

Encoder choice (encoder revision 1): constant if all bits are equal. Otherwise the decimal candidate exists when for the smallest e in 0..15 every value is finite, `m = round(value * 10^e)` (the double product rounded to an integer half to even, `Math.Round`'s default) has `|m| <= 2^53`, and `(double)m / 10^e` gives the original bits. Mode 1, 2 (if it exists) and 3 are built and the smallest is written; ties go to the lower mode.

### String column (n >= 1 nullable strings)

- `0` constant: one entry for every row.
- `1` dictionary: varint count c (1 <= c <= n), c entries in order of first appearance, then an integer column of n indices (< c).

Entry: varint h; `h = 0` is null, otherwise h - 1 bytes of UTF-8 follow (strict: invalid UTF-8 is `Corrupt`). A dictionary with duplicate entries or entries no index uses is accepted (the encoder writes neither); a count of 0 or above n, or an index `>= c`, is rejected.

### Presence (nullable members)

One byte: `0` all rows have a value, `1` no row has one, `2` a bitmap of `ceil(n / 8)` bytes follows, row i in bit `i mod 8` (least significant first) of byte `i / 8`, unused bits zero (reject otherwise). A bitmap with every bit set or none set is accepted and means the same as mode 0 or 1 (the encoder writes those modes instead). The member's value column then holds only the rows that have a value, in row order, and is absent when no row has one.

### Column order

Common to every value type, in this order:

1. Key: integer column.
2. `Time.Ticks - Key`: integer column. `Time` always has kind UTC.
3. `ReceivingTime` steps d: integer column (see Fidelity).
4. `ReceivingTime` kind bits (0..3, the top two bits of the raw DateTime: 0 unspecified, 1 UTC, 2 local, 3 local in an ambiguous DST hour): integer column.
5. `Status` (byte): integer column.
6. `Comment`: string column.
7. `IsTimeout` (0/1): integer column.
8. `LastReceivingTime`: presence; then, over the rows that have one, `LastReceivingTime.Ticks - Time.Ticks` (integer column) and its kind bits (integer column).
9. `AggregatedValuesCount`: integer column.

Instant types (`Boolean, Integer, Double, String, TimeSpan, Version, Rate, Enum`) continue with:

10. `EmaValue`: presence, then a double column.
11. `Value`: Boolean 0/1, Integer and Enum int32, TimeSpan ticks: integer column. Double and Rate: double column. String: string column. Version: presence, then four integer columns over the present rows: Major, Minor, Build, Revision (Build and Revision -1 when absent; Revision is -1 whenever Build is).

Bar types (`IntegerBar, DoubleBar`) continue with:

10. `Count`: integer column.
11. `OpenTime` ticks, then `OpenTime` kind bits: integer columns.
12. `CloseTime.Ticks - OpenTime.Ticks`, then `CloseTime` kind bits: integer columns.
13. `EmaMin`, `EmaMax`, `EmaMean`, `EmaCount`: each presence + double column.
14. `Min`, `Max`, `Mean`: integer columns (IntegerBar, int32) or double columns (DoubleBar).
15. `FirstValue`: presence + integer column (IntegerBar) or double column (DoubleBar).
16. `LastValue`: integer or double column.
17. `StdDev`: presence + double column.

A decoder range-checks every field before building a value (int32 fields, byte `Status`, bool 0/1, DateTime ticks within `0 ..= DateTime.MaxValue.Ticks`, kind bits 0..3, keys strictly ascending, Version components) and reports a violation as `Corrupt`.

## Encoder refusals

`ChunkRefusalReason`, for the whole input:

| Reason | When |
|---|---|
| `EmptyInput` | No rows. |
| `NullValue` | A row's value is null. |
| `UnsupportedValueType` | `FileValue`, or a runtime type that is not one of the ten sealed value records (a subclass included). |
| `MixedValueTypes` | Not all rows have the same record type. |
| `KeysNotAscending` | A key is not greater than the previous one. |
| `ReceivingTimeOutOfRange` | `Time + d * 1 ms` would leave the DateTime range. The rebuilt value is within half a millisecond of the source, so this needs a `ReceivingTime` within 5,000 ticks of `DateTime.MinValue` or `MaxValue`; a default (`MinValue`) `ReceivingTime` is refused when `Time`'s sub-millisecond part is 5,000 ticks or more. |
| `InvalidString` | `Comment` or a String value contains a lone UTF-16 surrogate. |
| `UnrepresentableValue` | A bool (`IsTimeout`, a Boolean value) or the `hasValue` byte of a `Nullable` member stored as a byte other than 0 or 1; a `Time` whose kind is not UTC; a DateTime (`Time`, `ReceivingTime`, `LastReceivingTime`, `OpenTime`, `CloseTime`) whose ticks lie outside `0 ..= DateTime.MaxValue.Ticks` (MemoryPack reads DateTime as raw bits, so a stored row can hold them). |
| `ChunkTooLarge` | One chunk's payload would exceed 64 MiB. The encoder stops writing at the limit; it does not build the oversized payload first. |

## Row limit per chunk

Fixed at 4,096 rows (part of version 1). Rows above the limit are split into `ceil(n / 4096)` chunks of balanced size (sizes differ by at most one row), in key order. Measured on 2026-11-19 with `src/sandbox/SealedHistoryBench`:

Production shard (531,480 rows, 492 sensors; File rows refused), stored bytes per row including the header:

| Limit | 1,024 | 2,048 | 4,096 | 8,192 | 16,384 |
|---|---|---|---|---|---|
| B/row | 7.44 | 7.38 | 7.38 | 7.38 | 7.39 |

Synthetic dense sensor, one value per second for a week (604,800 rows):

| Limit | 1,024 | 2,048 | 4,096 | 8,192 | 16,384 | 65,536 |
|---|---|---|---|---|---|---|
| Double (2 decimals), B/row | 7.26 | 7.22 | 7.19 | 7.18 | 7.17 | 6.87 |
| Rate (computed ratios), B/row | 12.36 | 12.25 | 12.20 | 12.19 | 12.13 | 11.95 |
| Decode the whole week, ms (Double) | 42.7 | 44.2 | 41.0 | 111.1 | 106.9 | 129.0 |
| Decode one chunk, ms (Double) | 0.06 | 0.13 | 0.27 | 0.53 | 1.07 | 5.03 |

Above 4,096 the gain in size is under 0.5% while a single-chunk decode doubles with each step, and from 8,192 rows the decoded list and arrays exceed the 85 KB large-object threshold, which made a whole-week decode 2.5 times slower.

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.SealedHistory/ChunkCodec.cs` | Public API, header, checksum, zstd, chunking. |
| `src/database/HSMDatabase.SealedHistory/Format/PayloadWriter.cs`, `PayloadReader.cs` | Column order per value type. |
| `src/database/HSMDatabase.SealedHistory/Format/IntColumn.cs`, `DoubleColumn.cs`, `StringColumn.cs` | Column encodings, presence. |
| `src/database/HSMDatabase.SealedHistory/Format/RowValidator.cs` | Refusals; the `ReceivingTime` rounding. |
| `src/database/HSMDatabase.SealedHistory/RowFidelity.cs` | The fidelity rule as a comparison. |
| `src/tests/HSMDatabase.SealedHistory.Tests/` | Round trips, persisted-member guard, golden chunks, corruption, refusals. |
| `src/sandbox/SealedHistoryBench/` | Measures a shard: bytes per row, timings, mismatches. |
| `src/database/HSMDatabase.LevelDB/DatabaseImplementations/SealedSensorValuesDatabase.cs` | The sealed week store: merged reads, writes, range-delete rewrite. |
| `src/database/HSMDatabase.LevelDB/Sealed/` | `MergedRowCursor`, `ChunkRowCursor`, `SealedChunkReader` (checks, errors, counters), `SealedWeekWriter` (chunks, then the marker). |
| `src/database/HSMDatabase.AccessManager/SealedWeekMarker.cs`, `SealedHistoryDiagnostics.cs` | The sealed-and-complete signal; process-wide counters. |
| `src/tests/HSMDatabase.LevelDB.Tests/SealedWeekTests/` | Differential tests (plain week vs sealed week), corruption, the signal, File values, concurrency, the compaction marker; `WeekSealer`, the test-only sealer; the sealing job's planner, life cycle, interruptions and races. |
| `src/database/HSMDatabase.LevelDB/Sealed/SensorValuesWeek.cs` | The week handle: gate, delete logs, leases, live swap (#1804). |
| `src/database/HSMDatabase.LevelDB/Sealed/WeekSealingJob.cs` | Seal, source-row removal, fold, unseal of one week (#1804). |
| `src/database/HSMDatabase.LevelDB/Sealed/RowsLeftRecord.cs`, `DurableFile.cs` | The sensors left as rows; marker writes with a directory sync. |
| `src/database/HSMDatabase.AccessManager/SealedWeekStages.cs`, `DatabaseSettings/HistorySealing.cs` | Marker names and the stage of a week; settings and the pass report. |
| `src/database/HSMDatabase/DatabaseWorkCore/HistorySealingPlanner.cs`, `DatabaseCore.cs` (region "Sealing job") | Which week and action next; the pass and the unseal. |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/HistorySealingService.cs`, `ServerConfiguration/Sections/HistorySealingConfig.cs`, `.../Nodes/Database/HistorySealingSensors.cs` | The service, its configuration, its self-monitoring. |

## Data Flow

Rows of one sensor (key order) → validation of the whole input → split into chunks of at most 4,096 rows → per chunk: column streams → zstd level 3 when it makes the payload smaller → header and CRC-32C. Decode reverses it after the header checks.

## Storage / Persistence

The codec stores nothing. The sealed week store below keeps chunks in a sibling LevelDB database `SensorValuesSealedV1_<from>_<to>` under the key `sensorId | firstKey`.

## Sealed week store (#1803)

`SealedSensorValuesDatabase` (`src/database/HSMDatabase.LevelDB/DatabaseImplementations/`) is the second implementation of `ISensorValuesDatabase` (ADR-0020, decision 11). It lives in `HSMDatabase.LevelDB`, which now references `HSMDatabase.SealedHistory`; the codec depends only on `HSMCommon`, so there is no cycle. Nothing above `DatabaseCore` knows which implementation serves a week. Sealing itself (writing and verifying chunks, deleting source rows) is the sealing job, #1804.

### On-disk layout

| Directory | Holds |
|---|---|
| `SensorValuesV2_<from>_<to>` | The week's row database. In a sealed week it is the **overlay**: every write to the week after sealing (late value, edit, sensor-move merge) is an ordinary row there; during the keep period it also still holds the source rows. File sensor values and sensors the codec refused always stay here. The `hsm-compacted` marker (#1801) stays here too. |
| `SensorValuesSealedV1_<from>_<to>` | The chunk database: key `sensorId (16) | firstKey (8, big-endian)`, the same 24-byte form as a row key, where `firstKey` is the key of the chunk's first row; value = one chunk (format v1 above). Chunks of a sensor never overlap and are in key order. |
| `SensorValuesSealedV1_<from>_<to>/hsm-sealed` | **The sealed-and-complete signal** (`SealedWeekMarker`). |

**The signal.** A week is sealed only when `hsm-sealed` exists inside the chunk directory. `SealedWeekWriter` writes every chunk batch with a sync, closes the database, writes the marker as a temporary file flushed to disk and renames it; the marker is therefore never over chunks that a crash or a power loss can take back. `LevelDBManager.GetSensorValuesDatabaseInstance(name, sealedName, from, to)` opens a sealed week when the marker is there and the plain row worker otherwise, with a warning in the log when the chunk directory exists without it (an interrupted or running seal; it is not read). The marker lives with the chunks, so it travels with them on a copy or restore. The weekly shard dictionary also lists a week that has a complete chunk directory but no row directory (opening it creates an empty overlay); a chunk directory without the marker is not a week by itself. The pre-V2 dictionary used by `MigrateDatabaseV2` never attaches sealed weeks.

`SealedWeekWriter.Open` always starts from an empty chunk database: a directory without the marker is what an interrupted seal left, not data (its source rows are still in the row database), and stale if those rows changed since, so it is deleted first. A directory with the marker is refused, and so is any directory whose name does not start with `SensorValuesSealedV1_`, before anything is touched: a wrong path (the row database, the database folder) is never deleted.

The hand-off of step 2 to the sealing job, and how #1804 meets it (details in the section "Sealing job"):

- Chunks are written while the source rows stay in the overlay, the week is marked complete only after verification, and source rows are deleted only after the keep period. A crash at any point before the marker leaves the week read from its rows alone.
- A week is opened as sealed or as rows by its marker. Since #1804 the shard dictionary holds a `SensorValuesWeek` handle per week, and the job swaps a week to the sealed store live, in the same step that writes the marker.
- A delete of the week between encoding and the marker must not come back from a chunk: the job keeps a delete log while it encodes and drops the chunks of every sensor a delete touched (a write, as opposed to a delete, is harmless: the overlay wins).
- **The marker's durability.** On Linux and macOS, `SealedWeekWriter.Complete`/`WriteMarker` and every marker of the job now sync the directory after the rename (`fsync` on the directory through libc; .NET has none) and sync the database folder for the chunk directory's own entry. On Windows NTFS journals the rename and there is nothing to call. The source rows are removed only once the keep period (at least one week by configuration) has passed and `hsm-sealed` is still on disk. See "Deviations" in the job section for how this reads the "reopened as sealed" rule.

### Merged reads

Every read opens a `MergedRowCursor` over two children: the overlay's LevelDB iterator and a `ChunkRowCursor` over the chunk database. The merged stream is in key order; on an equal key the overlay row is returned and the chunk row skipped, in both directions (the children are positioned like LevelDB's own merging iterator). The read methods then run the algorithms of `LevelDBDatabaseAdapter` line for line over that stream, quirks included (`Get` returns the first row at or after the key, `GetLastAndFirstValues` finds no last value when the seek runs past the end), so a sealed week answers exactly what a row database holding the same rows answers. `FillLatestValues` seeks to the last row before `toKey` instead of walking the sensor's rows (the same answer whenever `toKey` starts with the sensor prefix, as `DatabaseCore` always passes; otherwise it walks).

Values leave the interface as MemoryPack bytes: a chunk row is decoded and re-serialized. Such a value equals its source by `RowFidelity` (field by field, `ReceivingTime` to the millisecond), not necessarily byte for byte; an overlay row comes back byte for byte.

Cost: a chunk is read only when needed. Arriving at a chunk's first row costs nothing (its key is the chunk's key; the chunk's bytes are not even fetched); the key of its last row reads the verified header; a value or a step inside the chunk decodes it. A seek decodes at most the one chunk that can hold the target. Range reads are lazy in both directions, chunk by chunk, and a page read stops after the chunks its rows come from (plus, going backwards, the header of the chunk before, if it belongs to the same sensor).

**A chunk of another sensor is never read on the way.** Every row of a chunk carries the sensor prefix of the chunk's key, so against a key of another sensor the chunk is ordered by that prefix alone: a seek does not read the header of the previous sensor's last chunk, a backward read stops at a sensor boundary without reading the chunk beyond it, and a range delete does not read the chunk before `from` when it belongs to another sensor. The merged cursor decides which child holds its current row only when the row's key or value is needed or the cursor moves; a prefix or order question both children answer alike is answered without deciding. So a read of a sensor that lands backwards on the previous sensor's rows (it has no row at or before the target, as an idle sensor's history load does) learns "not mine" from their prefix, without ordering that sensor's overlay row against its chunk. A corrupt chunk therefore fails only reads and deletes of its own sensor, and costs no CRC pass on its neighbours' reads.

### Writes and range deletes

`PutSensorValue` / `PutSensorValueOrThrow` write the overlay, exactly as the row worker does. `RemoveSensorValues(from, to)`:

1. Under the chunk write lock, plan from the chunks that have a row in the range: a chunk wholly inside is deleted; a chunk on a border is decoded, its rows outside the range re-encoded (verified by decoding and `RowFidelity` before anything is written) and stored under the key of its new first row; an unreadable chunk is deleted only when it provably lies wholly inside the range: its key is in the range, and either the next chunk's key or `sensor | (To - 1)` is too. The second bound is an invariant of a weekly database: every row has `From <= tick < To` (`To` is the next Monday 00:00), because `DatabaseCore.AddSensorValue` stores a value in the week of its own `Time` under that same tick and the sensor-move merge copies a row within its week under the same tick. So a retention or clear-history delete that reaches the end of the week removes an unreadable chunk; one that ends inside the week is refused, as it must be (the chunk might hold rows after the cutoff).
2. Apply all of it as **one synced LevelDB write batch** to the chunk database.
3. Delete the overlay rows in the range, as the row worker does.

Crash states: before step 2, nothing has changed. Step 2 is atomic: every touched chunk is old or every one is new, so rows outside the range are never lost and rows inside never return from a chunk. Between step 2 and the end of step 3, overlay rows inside the range may remain; each of them is the value the week held before the delete, and the next delete of the range (retention re-runs) removes them. The opposite order would be worse: overlay rows deleted first would let the chunk rows they hid (older values of edited keys) reappear. Step 2 is synced so that a power loss cannot drop it while keeping the overlay deletes of step 3, which live in another database.

If a border chunk cannot be decoded or re-encoded, the whole delete is refused: nothing is written, the overlay is untouched, the error is logged and counted (`SealedHistoryDiagnostics.ChunkRewriteFailures`).

There is no single-row delete on `ISensorValuesDatabase`; every history delete (retention, clear history, sensor removal) is a range, so a sealed week needs no tombstones.

### Concurrency

No lock on reads. A read creates the overlay iterator first and the chunk iterator second; each LevelDB iterator reads the snapshot of its creation. A range delete changes the chunks first and the overlay second. A reader therefore sees the old chunks with the old overlay, or the new chunks with an overlay the delete has partly or fully gone through: each row it returns is a row the week held before, or the delete removed it; it never sees a duplicate, a mix inside one chunk, or a chunk row an overlay row used to hide. Chunk writers (range deletes, and the job's folds) are serialized by a lock, because a rewrite is planned from the chunks it read. Since #1804 every write and range delete also holds the week handle's gate shared for its whole duration, both phases of a range delete included, and the sealing job's commit steps take that gate exclusively (section "Sealing job", parts C and D); reads still take no lock.

### Errors

A chunk that fails to decode, or does not fit its key (the header's first key differs from the stored key, chunks overlap), raises `SealedChunkReadException` (in `HSMDatabase.AccessManager`, so the server catches it by the access layer's type). Every failure is counted per week (`ChunkReadFailures`) and process-wide (`SealedHistoryDiagnostics.ChunkReadFailures`); a given chunk is logged as an error once per process (by database and chunk key) and at debug level after that, because a corrupt chunk stays corrupt and its readers retry. Both process-wide counters (`ChunkReadFailures`, `ChunkRewriteFailures`) are posted by the self-monitoring under `Database/Sealed history` since #1804.

How it reaches the caller, per method:

| Method | On an unreadable chunk |
|---|---|
| `GetValuesFrom`, `GetValuesTo`, `GetKeysValuesTo`, `GetAll` | Throw from the enumeration: a range read never ends early as if the data ended. |
| `Get`, `GetLatest`, `GetFirst` | Throw. The row worker answers "no value" for a failed read, but `DatabaseCore.GetLatestValue` / `GetFirstValue` read null as "this week has none" and would answer with another week's value; a stale newest or oldest value must not be taken for the sensor's own. Any other failure keeps the row worker's fallback. |
| `FillLatestValues`, `GetLastAndFirstValues` | Per sensor: the sensor whose chunk cannot be read gets nothing from this week (its entry stays as the caller had it), every other sensor is answered, and the dictionary the caller accumulated is always returned. Neither has a caller in the server today; `DatabaseCore` would fill such a sensor from another week. |
| `CalculateHistorySize` | Never throws (it is informational: the history info of a node, the delete preview). An unreadable chunk adds its stored bytes; the count falls back to a lower bound (overlay rows, the readable chunks' rows no overlay row hides, and for an unreadable chunk wholly in the range with a readable header its row count less the overlay rows in its span). |
| `RemoveSensorValues` | Refused when a border chunk cannot be rewritten (logged, counted in `ChunkRewriteFailures`); an unreadable chunk wholly inside the range is deleted with it (see above). |

What the server does with a throwing `GetLatestValue` / `GetFirstValue` (`HSMServer.Core`):

| Caller | On the exception |
|---|---|
| The sensor history load (`BaseSensorModelT.LoadHistoryUnderLock`, at first use and from `RetryFailedHistoryLoad`) | The load fails as for any storage error: the sensor is not taken as loaded, self-destroy is deferred (`ShouldDestroy` is false), and the maintenance sweep retries with a backoff (1 h after the failure, then 2, 4, 8, 16 h, then every 24 h, a capped number of retries per sweep). A chunk that stays corrupt keeps the sensor in that state; it is never destroyed on a guessed age. |
| `RefreshAfterMerge` (after a sensor-move merge) | The queue logs the failed item; the survivor keeps the newest value it had. |
| The previous-path move check (`HaveValuesInTheSamePeriod`) | The path is not applied, with its own warning: the overlap check cannot be made and a merge is for good. |
| `IsBorderedValue` (clear history and retention of an aggregating sensor) | Treated as "no bordering value": logged, and the delete goes on with its range unadjusted, so a corrupt chunk never blocks the delete that removes it. |

### Sizes, export, the database list

- `ISensorValuesDatabase.CalculateHistorySize(from, to)` (new): the row worker returns what `DatabaseCore.CalculateSensorHistorySize` summed before (rows, key bytes, value bytes); a sealed week returns the number of values a read returns and the bytes stored: overlay keys and values plus chunk keys and chunk bytes (a lower-bound count if a chunk cannot be read, see Errors).
- `DatabaseCore.SensorHistoryDbSize` adds each week's chunk directory (also an incomplete one: it takes disk space). `TotalDbSize` already counted it. The sizes `DatabaseCore` logs around a compaction and writes into the compaction marker include the chunk directory too.
- The CSV export (`ExportValuesDatabase`) reads `GetAll`, i.e. the merged stream; the size column is then the re-serialized value size of a chunk row.
- The Configuration page lists weeks by the row database name, which a sealed week keeps; export looks it up by that name.

### Compaction (#1801)

`Compact()` compacts the overlay and the chunk database. The marker logic is the row worker's: a write, or a range delete that changes the overlay or rewrites a chunk, removes `hsm-compacted` first and counts in `ChangeCount`; a delete that changes nothing leaves it.

### Measured (2026-11-19, `SealedHistoryBench --store-synthetic` / `--store-shard`)

Both weeks compacted, warm medians; the overlay holds only refused (File) sensors.

| | Synthetic: 300 sensors, 791,716 rows, densest 259,199 | Production shard copy: 492 sensors, 531,480 rows, densest 10,061 |
|---|---|---|
| On disk, plain → sealed | 29.9 MB → 6.6 MB | 22.9 MB → 4.0 MB |
| Page of 100, newest first | 0.041 → 0.258 ms | 0.041 → 0.275 ms |
| Page of 100, oldest first | 0.031 → 0.240 ms | 0.029 → 0.239 ms |
| `GetLatest` | 0.002 → 0.241 ms | 0.002 → 0.214 ms |
| Whole densest sensor | 48.8 → 31.3 ms | 2.1 → 3.6 ms |
| History load of every sensor: `GetLatest` + `GetFirst` per sensor | 10.9 → 58.1 ms (36 → 194 µs per sensor) | 9.6 → 77.7 ms (20 → 158 µs per sensor) |
| `GetLastAndFirstValues`, all sensors (no caller) | 10.1 → 34.3 ms | 8.9 → 35.2 ms |
| Full week (`GetAll`) | 149 → 108 ms | 106 → 85 ms |

A point read or a first page costs one chunk decode (about 0.2 ms) instead of microseconds; whole-week and dense-sensor reads get faster. What runs when a sensor's history loads (at its first use after a start) is `DatabaseCore.GetLatestValue` plus `GetFirstValue`. `GetLatestValue` walks the weeks newest first and stops at the first with a value, so a sealed week is reached only for a sensor without a value in any newer week; `GetFirstValue` walks the oldest week first, so it reaches a sealed week for every sensor whose oldest week is sealed. Each such sealed week costs about 0.1 to 0.2 ms per sensor instead of 20 to 40 µs (two chunk decodes). `GetLastAndFirstValues` has no caller in the server (its only caller is commented out in `TreeValuesCache`); it went from 49 to 35 ms on the production copy once a seek stopped reading the previous sensor's chunk header.

## Sealing job (#1804)

`HistorySealingService` (in the server, every 7 minutes, first pass 15 minutes after start) calls `IDatabaseCore.RunHistorySealingPass(utcNow, settings)`. A pass does at most one action on one week; `DatabaseCore` picks it with `HistorySealingPlanner` (pure: the week states and the clock are parameters) and runs it with `WeekSealingJob` (`src/database/HSMDatabase.LevelDB/Sealed/`). When `HistorySealing.Enabled` is false the service does not call the database, and the database would answer `Disabled` without looking at anything: nothing is created on disk.

### Stages and markers (A)

A week's stage is read from files in its own directories (`SealedWeekStages.Read`), never from a separate database. Every file is small, human-readable text, written as a temporary file flushed to disk and renamed over the target, after which the directory is synced (see "The marker's durability" above).

| File | Where | Meaning |
|---|---|---|
| `hsm-sealing` | chunk directory | A seal started (informational; a chunk directory without `hsm-sealed` is an incomplete seal anyway). Deleted after the swap. |
| `hsm-rows-left` | chunk directory | The sensors left as rows on purpose: id, reason, a fingerprint of their rows (count and SHA-256 of keys and values). Written before `hsm-sealed` and rewritten by folds. Missing or unreadable only costs a retry. |
| `hsm-keep-source-rows-until` | chunk directory | First line: the UTC time after which the source rows may be removed. Written before `hsm-sealed`. Missing or unreadable: the source rows are kept. An operator may edit it to keep them longer. |
| `hsm-sealed` | chunk directory | Step 2's signal, unchanged in meaning and still the only file a read acts on: the chunks are complete and the week is read merged. Its text now describes the seal (rows, sensors, chunks, bytes, keep time). |
| `hsm-source-rows-removed` | chunk directory | The source rows that equal their chunk rows are gone. |
| `hsm-unsealing` | chunk directory | An unseal is in progress. |
| `hsm-unsealed` | row directory | An operator unsealed the week; the job never seals it again until the file is deleted. |

| Stage | Files |
|---|---|
| Not sealed | no chunk directory |
| Incomplete seal (running or interrupted) | chunk directory without `hsm-sealed` and without `hsm-unsealing` |
| Sealed, source rows kept until T | `hsm-sealed`, `hsm-keep-source-rows-until` |
| Source rows removed | `hsm-sealed`, `hsm-source-rows-removed` |
| Being unsealed | `hsm-unsealing` (with `hsm-sealed` while the copy runs; without it once the week is rows again) |

Step 2's rule (marker present = sealed week) stays true at every instant: `hsm-sealed` is written only after the chunks are complete and verified, and removed (by an unseal) only after every chunk row is in the overlay and before any chunk is deleted.

### The week handle and the live swap (B)

The shard dictionary holds a `SensorValuesWeek` per week instead of the row worker or the sealed store directly. It serves the week through its current instance and can replace it:

- **Same native row database.** The replacement is built over the open row database of the instance it replaces (LevelDB allows one handle per directory). The swap therefore closes nothing a running read uses; the old instance is told it no longer owns the row database.
- **Leases.** Every read takes a lease on the instance current when it starts (a range read when its enumeration starts) and returns it when it ends; a replaced instance is disposed when its last lease ends. After a seal the replaced row worker owns nothing, so disposing it frees nothing; after an unseal the replaced sealed store closes its chunk database only then.
- **When.** The seal swaps in the same exclusive step that writes `hsm-sealed`: the sealed store is opened over the chunk directory first (if that fails, nothing has changed and no marker exists), then the marker is written, then the reference is replaced. From then on every delete goes to the sealed store and rewrites chunks. A read that started on the row worker finishes on it and sees the whole week: during the keep period the row database still holds every row.
- **Window.** Writes and deletes wait only while the exclusive step runs: the drop of the chunks a delete touched (rare), three small marker writes with their syncs, and opening the chunk database.

### A delete that races a seal (C)

Every write and range delete holds the week's gate shared for its whole duration; the job takes it exclusively for its commit steps. Before it reads anything, a seal opens a **delete log** on the week (under the exclusive gate, so a delete already running has finished and is reflected in what the seal reads); from then on every range delete records its range before it touches anything. The seal encodes from one LevelDB snapshot outside the gate. Its commit, under the exclusive gate, deletes from the chunk database every chunk of each sensor a recorded range touches (in one synced batch), and only then writes the markers and swaps. A delete cannot run between that check and the swap, and every delete after the swap goes to the sealed store. The dropped sensors stay rows (counted as `DeletedDuringSeal`) and are sealed later by a fold.

A write during encoding is not recorded and aborts nothing: the row is in the overlay, which wins over the chunk row on every read, and the source-row removal keeps it because it differs from its chunk row (a new key has no chunk row at all). A later fold takes it into the chunks. So a week that keeps receiving late values is sealed on the first attempt.

### A delete that races a fold (D)

A fold runs the same way: delete log first, then snapshots of the overlay and the chunks, encoding outside the gate, and a commit per sensor under the exclusive gate that is skipped (`DeletedDuringFold`) if a recorded range touches the sensor. A step-2 range delete holds the gate shared across both of its phases (chunks, then overlay rows), so no commit and no snapshot of the job lands between them: the log is opened under the exclusive gate, which waits for a running delete to finish both phases. The reader guarantee of step 2 is unchanged: a fold replaces a sensor's chunks in one synced batch with chunks that hold exactly the merged rows, and the overlay rows it folded stay until a later step deletes those that equal their new chunk rows.

### Removing the source rows (E)

When the keep time has passed (and `hsm-sealed` is still on disk), the job walks the chunk database chunk by chunk. Each step, under the exclusive gate, re-reads the chunk, decodes it and, for each of its rows, reads the overlay row with the same key and deletes it only if it equals the chunk row by `RowFidelity`. A row written after the seal differs and stays; a chunk that cannot be read keeps all its overlay rows, is counted, and the week stays in its stage (the pass is a failure, retried after the backoff). When every chunk has been read, `hsm-source-rows-removed` is written. Every step is idempotent, so a crash anywhere leaves a correct week and the next pass finishes the job. The deletes clear the compaction marker, and step 0's background compaction compacts the week once it has been quiet for 3 hours. The job does not compact it itself: compaction is its own one-shard-per-pass step with its own failure handling, and compacting inside the sealing pass would hold the shared maintenance state for a much longer step.

### Folding (F)

A fold is considered only for a week whose source rows are gone. The job counts the week's foldable overlay rows: rows of sensors that are not File sensors and not listed in `hsm-rows-left` with an unchanged fingerprint. The count is kept in memory and redone only when the week's change counter moves or the instance serving it changes. A week is folded at once when it has at least `FoldMinRows` such rows, otherwise once the job has seen it unchanged for `FoldQuietPeriod` (by the job's own clock: after a restart the period starts again). So a trickle of late values does not rewrite the week on every pass.

Per sensor with foldable rows: its overlay rows merged with its chunk rows (the overlay wins) are encoded in batches of at most one chunk, decoded and compared with the merged rows field by field, and committed (old chunks deleted, new chunks put, one synced batch, under the chunk write lock and the exclusive gate); the new chunks are then read back and compared byte for byte, and if they differ the old chunks are put back. Then the overlay rows of the sensor that equal the new chunk rows are deleted by the rule of E. A sensor without chunks (left as rows earlier because a delete raced its seal, or written to the week only after the seal) is sealed by a fold in the same way. Folded rows are removed as soon as the new chunks verify; they get no keep period. File values stay rows.

### Unseal (G)

`IDatabaseCore.UnsealHistoryWeek(name, utcNow)`, behind the Unseal button on the Configuration page (`HomeController.Unseal`, admin only, POST with an antiforgery token; the controller only calls the core). It works from whatever stage an earlier unseal reached:

1. Write `hsm-unsealing`.
2. Copy: chunk by chunk, under the exclusive gate, every chunk row whose key the overlay does not hold is written into the overlay (MemoryPack of the decoded value, the same bytes a merged read returns), synced. An overlay row always wins. Rows come back with the codec's fidelity (`ReceivingTime` to the millisecond). An unreadable chunk stops the unseal: its rows exist nowhere else.
3. Under the exclusive gate: write `hsm-unsealed` in the row directory, delete `hsm-sealed`, swap the week to a row worker over the same row database.
4. Delete the chunk directory once no read uses the replaced sealed store (it waits up to 30 s; if a read still holds it, the directory is deleted by the next sealing pass or the next unseal).

A crash before 3 leaves a sealed week whose overlay holds some or all chunk rows (reads unchanged); a crash after the marker deletion leaves a week of rows with an unread chunk directory. In both cases `hsm-unsealing` makes the next pass finish the unseal before anything else (`FinishUnseal`), or the operator presses Unseal again. A sealed marker over a partly deleted chunk directory cannot happen: chunks are deleted only in 4.

### Coexistence and shutdown (H)

A pass and an unseal enter `DatabaseCore`'s compaction state (`TryEnterCompact`), the same one the manual Compact and the background compaction step use: none of the five ever runs at the same time as another, on any week, and the Compact button answers "already running" during a pass. `Dispose` refuses new passes and waits for a running one. A pass is also interruptible: it checks for shutdown between sensors, chunk steps and copy steps, and stops there (outcome `Interrupted`, nothing to undo; the next pass resumes). The longest uninterruptible piece is the encoding and verification of one sensor (a whole week, its densest sensor included, seals in 1.2 to 2.2 s, see Measured).

### Memory and pacing (I)

A week is sealed sensor by sensor from one snapshot: a first pass over the sensor's keys counts its rows, so batches have the codec's own balanced sizes; then one batch of at most 4,096 rows is decoded at a time, and only the encoded chunks of the current sensor are held (about 7.5 bytes per row). Verification reads the chunks back and streams the source rows again. A fold holds the old and new chunks of one sensor. The planner works in UTC working hours (`WorkingHoursStartUtc`..`WorkingHoursEndUtc`, the whole day when equal, may wrap past midnight); an operator's unseal ignores them. One action per pass, every 7 minutes.

The order of a pass: finish an interrupted unseal; remove the source rows of a week whose keep time passed; seal a closed week (its end, the next Monday 00:00 UTC, plus `SealAfter` is not in the future); fold. Within each kind the oldest week first.

### Failure handling (J)

- A week whose action fails (I/O error, an unreadable chunk during source-row removal, a fold whose chunks read back wrong, a store that cannot open) is logged with its name, held back for a day (`HistorySealingPlanner.FailureBackoff`, in memory like the compaction's), and the next weeks go on.
- A sensor the codec refuses (`Refused:<reason>`), whose written chunks do not decode back to its rows (`VerificationFailed`), with a row that cannot be deserialized (`UnreadableSourceRow`), with an unexpected key length (`UnexpectedKey`) or an unreadable chunk during a fold (`UnreadableChunk`) stays rows, is counted by reason in the pass and listed in `hsm-rows-left` with a fingerprint of its rows. It is tried again only when its rows change. A sensor whose work was dropped because of a delete (`DeletedDuringSeal`, `DeletedDuringFold`) is counted but not listed, so the next fold takes it.
- File sensors are skipped by the type of their first row and not counted.
- An unreadable source row leaves its sensor as rows instead of failing the whole week (see Deviations).

### Configuration

Section `HistorySealing` of `Config/appsettings.json`, read once at startup (`HistorySealingConfig`, validated at startup with an error that names the key):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | ADR-0020, decision 10: off until the rollout step decides otherwise. |
| `SealAfterDays` | `7` | Days after a week closed before it is sealed (1..3650). Our choice: late values are harmless after a seal, this only keeps the job off weeks collectors are still catching up on. |
| `KeepSourceRowsWeeks` | `4` | Weeks the source rows stay after a verified seal (1..520). ADR-0020 leaves the number to configuration; the minimum of one week carries the durability rule of step 2. The keep time is fixed into each week's `hsm-keep-source-rows-until` when it is sealed; a later change applies to weeks sealed afterwards. |
| `WorkingHoursStartUtc`, `WorkingHoursEndUtc` | `"00:00"`, `"00:00"` | Daily UTC window, `HH:mm`; equal values mean all day. |
| `FoldMinRows` | `10000` | A sealed week with this many rows to fold is folded at the next pass. |
| `FoldQuietHours` | `24` | Fewer rows are folded once the week's overlay has not changed for this long. |

### Self-monitoring

Under `Database/Sealed history` in the "HSM Server Monitoring" product (`HistorySealingSensors`), posted from the 5-minute self-monitoring sample; each pass once:

| Sensor | Value |
|---|---|
| `Last run` (string) | action and outcome; the comment is the full pass report; Error status on a failed pass. Posted for every pass while the job is on (including "nothing due"). |
| `Sealed weeks` (int) | complete sealed weeks on disk after the pass |
| `Rows sealed` (int) | rows written into chunks by the last seal or fold |
| `Bytes before` / `Bytes after` (MB) | key and value bytes of those rows / key and chunk bytes written for them |
| `Sensors left as rows` (int) | with the reasons in the comment, for the last seal or fold |
| `Verification failures` (int) | sensors whose chunks did not decode back to their rows in the last seal or fold; Error status above 0 |
| `Chunk read failures`, `Chunk rewrite failures` (int) | `SealedHistoryDiagnostics` counters since the process started, posted when they change once there is a pass or one is above 0; Error status above 0 |

### Measured (2026-11-19, `SealedHistoryBench --job-synthetic` / `--job-shard`, Release, one machine)

| | Synthetic: 3 weeks, 300 sensors, 683,300 sealed rows per week | Production shard copy: 1 week, 492 sensors, 531,475 sealed rows |
|---|---|---|
| Seal one week | 1.2–1.9 s; managed heap +24 MB, working set +40–60 MB | 2.2 s; heap +25 MB, working set +78 MB |
| Bytes before → after (logical, per week) | 73.1 MB → 4.8 MB | 68.3 MB → 3.9 MB |
| History on disk: rows compacted → sealed with source rows kept → removed and compacted | 75.2 → 89.1 → 18.5 MB (3 weeks; File rows stay) | 21.8 → 25.6 → 3.8 MB |
| Remove the source rows of one week | 0.9–1.1 s; heap +18 MB | 0.7 s; heap +20 MB |
| Fold about 1,000 late values and edits over 20 sensors | 0.10 s (35,146 rows re-encoded) | 0.07 s (12,467 rows re-encoded) |
| Unseal one week | 1.4 s; heap +18 MB | 1.5 s; heap +18 MB |

Before the compaction that follows source-row removal, the week is larger than before (deletes are tombstones): 107 MB for the three synthetic weeks.

### Deviations and readings of ADR-0020

- **"Reopened as sealed".** Step 2 asked that source rows never be deleted on the strength of a marker written in the same run without the week having been reopened as sealed. The live swap opens the sealed store in the same step as the marker (it cannot reopen the chunk database later without closing it under running reads). What protects the source rows instead: the directory sync after every marker on Linux and macOS, the keep period of at least one week, and the check that `hsm-sealed` is on disk when the removal starts. On Windows there is no directory sync to call.
- **Unreadable source row.** It leaves its sensor as rows (counted, listed) instead of failing the whole week: the other sensors of the week are sealed.
- **Folded rows get no keep period.** As specified for folds: they are removed once the new chunks verified, so a sensor sealed late by a fold has no source-row grace either.
- **An unseal is sticky** (`hsm-unsealed`): otherwise the job would seal the week again at its next pass.
- **Keep time fixed per week**: changing `KeepSourceRowsWeeks` does not move the keep time of weeks already sealed (edit their file instead).

## UI / Operator Visibility

- Configuration page, "Values Database" row: the **Unseal** button (admins) turns the selected week back into rows; the result or the error is shown next to the buttons.
- Self-monitoring sensors under `Database/Sealed history` (above).
- The log: every pass that did something logs its report at Info (Error when it failed), every sensor left as rows a Warn line with its reason.
- The benchmark reports refusals and mismatches for a shard.

## Dependencies

- Depends on: `HSMCommon` value records; `ZstdSharp.Port` 0.8.8 (MIT, pure managed port of zstd, no native binaries).
- Used by: sealed week store (#1803), sealing job (#1804).

## Tests

`src/tests/HSMDatabase.SealedHistory.Tests` (in `HSMServer.sln`). The server CI lane runs it on pushes to `master` and `develop` only, not on pull requests: run `dotnet test src/tests/HSMDatabase.SealedHistory.Tests -c Release` locally before pushing any codec change.

- round trip of every sealed type with ordinary, extreme and randomized (fixed seed) inputs, one row, the row limit, splitting;
- every MemoryPack-persisted member of every sealed record round-trips and is compared by `RowFidelity` (fails when a record gains a member the codec drops);
- golden chunks under `Golden/format{F}-encoder{R}/` (synthetic, fixed inputs): every committed set decodes to the inputs and carries its directory's format version and encoder revision; encoding the fixed inputs gives exactly the bytes of the current set;
- the encoder revision: written by the encoder, returned by `ReadHeader`, any value (0, 1, 2, 255) decodes to the same rows, covered by the checksum; the reserved bytes 6..7 are written as zero and rejected when not;
- every bit flip and every truncation of a chunk is detected; fuzzed payloads behind a valid checksum fail with `ChunkFormatException` only;
- every refusal reason, including a stored DateTime beyond the range for every DateTime member found by reflection, and a payload above 64 MiB;
- "whatever `Encode` accepts, `Decode` returns": 3,000 randomized inputs of every type, a third of the rows re-read from randomly patched MemoryPack bytes; either a refusal or a decode that `RowFidelity` cannot fault;
- failures caught by `Decode` must come from its own checks, zstd or UTF-8 decoding, never from a reader bug (the inner exception is checked);
- zstd payloads that are not exactly one frame with a declared content size are rejected before decompression.

To change the encoder's output: bump `ChunkCodec.EncoderRevision` (or `FormatVersion`, keeping the old decoder and dispatching on byte 2, when a current reader could not decode the new bytes), run `GoldenChunkTests` with `HSM_WRITE_GOLDEN_CHUNKS=1` to write the new set next to the old ones (the writer refuses an existing directory), and commit it. The old sets stay as decode tests. Never change the input of an existing golden case; add a case instead.

The sealed week store is tested in `src/tests/HSMDatabase.LevelDB.Tests/SealedWeekTests/` (`dotnet test src/tests/HSMDatabase.LevelDB.Tests -c Release --filter FullyQualifiedName~SealedWeekTests`), each test in its own temporary directory:

- differential: the same rows in a plain week and in a week sealed by the test-only `WeekSealer`; every read of the interface (point reads at every probe key and its neighbours, range reads both ways with and without early stop, borders on chunk edges, ranges across sensors, empty and reversed ranges, first/last values with and without prefilled results, `FillLatestValues`, `GetAll`, counts) compared after late values, rewrites of sealed keys, range deletes (inside a chunk, across a chunk border, exactly one chunk, leaving one row of a chunk, a whole sensor, across sensors) and sensor-move merges, then after a reopen; once with the source rows deleted and once kept (the keep period, where no chunk row may come back); plus a randomized run with a fixed, logged seed;
- the same at the `DatabaseCore` level: page reads both ways, latest/first values, `GetLatestValuesFromTo`, `MergeSensorValues`, `ClearSensorValues`, `RemoveSensorWithMetadata`, `CalculateSensorHistorySize`, `SensorHistoryDbSize`, the CSV export, the database list, a restart;
- a corrupt chunk: range and point reads that reach it throw and are counted, reads that do not reach it work, reads and deletes of the next sensor never touch it, the history size does not throw and counts a lower bound, the batch point reads lose only its sensor, a delete with a border in it is refused and changes nothing, a delete that covers it removes it (also a retention delete to the end of the week, and a sensor removal ahead of UTC), one that ends inside the week does not; at the `DatabaseCore` level `GetLatestValue` and `GetFirstValue` throw instead of answering from another week; in `HSMServer.Core.Tests` (`SealedHistoryReadFailureTests`) a clear of an aggregating sensor still deletes when the bordering value cannot be read;
- the signal: a chunk directory without `hsm-sealed` is not read and is not a week; the writer refuses a sealed week and a directory that is not a sealed week directory; a re-seal after an interrupted one keeps nothing of it;
- a corrupt last chunk of a sensor with overlay rows does not reach the idle sensor after it in key order (no rows, or rows only after the target): its point, range and `FillLatestValues` reads match the plain week with no chunk read failure;
- File values stay rows and read back byte for byte;
- concurrency, deterministically through two test seams: a reader started before a range delete sees the old week; a reader between the chunk rewrite and the overlay delete sees only rows the week held before; a whole delete landing between a read's two iterators never shows a chunk row the overlay hid (this test fails when the iterator order is swapped);
- the compaction marker: chunk-only deletes clear it, empty deletes do not; the sizes written into it include the chunk directory.

Seeded tests take every sensor id and value from their generator, so a failure replays from the logged seed; the randomized run fails if it deleted more than half of the week (it would compare almost nothing).

The sealing job (#1804), in the same project (`--filter FullyQualifiedName~HistorySealing`):

- `HistorySealingPlannerTests`: the seal delay to the tick, incomplete seals, unsealed and sealed weeks, the keep time (and a missing one), the fold threshold and quiet period, the order of actions and weeks, the failure backoff of every action, working hours with and without a wrap past midnight.
- `HistorySealingJobTests`, through `DatabaseCore` with an injected clock, against a plain database that receives the same writes and deletes, every read of every week and the point reads of `DatabaseCore` compared with step 2's `FidelityComparer`: the whole life (seal both weeks oldest first with a refused sensor and a File sensor, keep period, source-row removal, late values and an edit, a fold after the quiet period, convergence, unseal, no reseal, a restart); the switch off (nothing created on disk); working hours; a verification failure (test seam `SealingChunksBeforeWrite`) and a codec refusal left as rows, counted, not retried until the rows change, then folded.
- A crash after each stage (test seam `SealingCheckpoint` throws, the test restarts the database): seal after opening, mid-way through the sensors, after encoding, after the marker, after the swap; source-row removal mid-way and after its marker; fold after encoding, after the commit, mid-way through the overlay deletes, at the end; unseal after its marker, mid-way through the copy, after `hsm-unsealed`, after the swap, at the end. Each restarts correct and completes.
- The races, each through a checkpoint: a range delete during a seal's encoding (C) and during a fold (D) do not come back (mutation-checked: without the delete-log check both fail on the very first comparison); a write during encoding is kept and the sensor is still sealed; a read across the seal's swap finishes on the row worker with every row; a read across the unseal's swap keeps the chunk database open and the directory is deleted only after it ends.
- Three randomized runs (logged seeds) of late writes, partial range deletes and retention deletes between 30 passes, with an unseal and restarts.
- In `HSMServer.Core.Tests` (`HistorySealingSensorsTests`): the self-monitoring values of a pass, a failed pass, the counters, the service not calling the database when off, the configuration defaults and validation.

## Notes

- Departures from the measured prototype: residual orders are the plain first and second difference for every integer column (the prototype hard-wired delta-of-delta for time); the decimal mantissas use a full integer column (byte or bit level), not always the bit level; the double column picks the smallest of XOR bytes, decimal and Gorilla; `CloseTime` is stored relative to `OpenTime`; the key and `Time` are separate columns, so a `Time` that differs from the key round-trips; strings are a dictionary per column with indices in an integer column; powers of ten are literals, not `Math.Pow`.
- Encoding costs about 0.6 µs per row and decoding about 0.25 µs per row on the measured shard; the densest real sensor (10,061 Rate rows, 3 chunks) decodes in about 0.7 ms.

## Known Issues / Limitations

- Rows with leftover bytes in `Nullable` padding decode with zero padding; their MemoryPack bytes differ from the source blob though every value is equal (see Invariants).
- zstd output is stable for one `ZstdSharp.Port` version. An upgrade may change the stored bytes of new chunks without changing the format; the golden encode test then fails only on its last assertion, and the upgrade needs a new encoder revision.
