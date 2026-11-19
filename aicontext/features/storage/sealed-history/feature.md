# Feature: Sealed history chunk format

> Owner: storage | Last reviewed: 2026-11-19 | Canonical: yes
> Scope: the byte format of a chunk of sealed sensor history (ADR-0020, step 1 of epic #1800), its fidelity rules, its versioning rule, and when the encoder refuses.

---

## Overview

A **chunk** is a bounded run of one sensor's history rows encoded as one value: a fixed header, then column streams compressed with zstd. The codec (`HSMDatabase.SealedHistory.ChunkCodec`) is a pure library: it encodes rows into chunks and decodes chunks back into rows. It does not store anything; the sealed week store (#1803) and the sealing job (#1804) are later steps.

A row is `(Key, Value)`: `Key` is the tick part of the storage key `sensorId | tick`, `Value` one of the `BaseValue` records from `HSMCommon`. File values are never sealed.

This document is the format specification: it is meant to be precise enough to write a second decoder from it.

## Invariants

- **Fidelity.** A decoded row has the same key, the same value record type and every persisted field bit for bit equal to its source (doubles by their IEEE 754 bits: NaN payloads, -0.0 and infinities included; `DateTime` by ticks and kind bits; null and empty strings distinct; null and absent members distinct), with one exception: `ReceivingTime` is stored as `d = round((ReceivingTime - Time) / 1 ms)`, half away from zero, and rebuilt as `Time + d * 1 ms` with the source's kind bits. It moves by at most 5,000 ticks and never to the other side of `Time`. `RowFidelity.FindDifference` is this rule as code.
- **Padding is not a field.** MemoryPack writes a `Nullable<T>` member as its raw struct, padding bytes included, and those bytes can hold leftovers of the writer's memory (seen in production: `IntegerBarValue.FirstValue`, 1,788 of 10,074 rows of the measured shard). The codec keeps the value, not the padding, so the MemoryPack bytes of a decoded row can differ from the source blob while every field is equal. Verification compares fields (`RowFidelity`), not blobs.
- **Refuse, never approximate.** `Encode` checks the whole input first; if any row cannot be represented exactly it returns a refusal (reason and row index) and no chunk. It never throws for an unsupported row.
- **Fail loudly.** `Decode` and `ReadHeader` throw `ChunkFormatException` for an unknown version, a bad checksum, a truncated or a corrupt chunk. They never return an empty or partial list, and every read is bounds-checked: chunk bytes are untrusted input.
- **Versioned bytes.** A change to the bytes written for the same input is a new format version; every released version keeps decoding. Bytes 0..2 of a chunk (magic and version) have the same meaning in every version.
- **Thread safety.** Encode and decode share no mutable state and are safe to call concurrently.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Encode the rows of one sensor in one closed week into chunks | sealing job (#1804) |
| 2 | Read a chunk's header (row count, first and last key) to plan reads and deletes | sealed week store (#1803) |
| 3 | Decode a chunk on a history read | sealed week store (#1803) |
| 4 | Verify a seal: decode and compare every row with its source | sealing job (#1804) |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `ChunkCodec.Encode(IReadOnlyList<HistoryRow>) -> ChunkEncodeResult` | `src/database/HSMDatabase.SealedHistory/ChunkCodec.cs` | Rows of one sensor, keys strictly ascending. Chunks in key order, or a `ChunkRefusal`. |
| `ChunkCodec.Decode(ReadOnlySpan<byte>) -> List<HistoryRow>` | same | Throws `ChunkFormatException` (`Error`: `NotAChunk`, `UnknownVersion`, `Truncated`, `ChecksumMismatch`, `Corrupt`). |
| `ChunkCodec.ReadHeader(ReadOnlySpan<byte>) -> ChunkHeader` | same | Verifies magic, version, length, checksum and header fields; does not decompress. |
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
| 4 | 1 | Compression: `0` = payload stored as is, `1` = payload is one zstd frame. |
| 5 | 3 | Reserved, zero. |
| 8 | 4 | Row count n, int32, `1 <= n <= 4096`. |
| 12 | 4 | Payload length (uncompressed), int32, `1 ..= 67,108,864`. With compression 0 it equals the stored length. |
| 16 | 4 | Stored length, int32: the chunk is exactly `40 + stored length` bytes. |
| 20 | 8 | First key, int64 (the key of row 0). |
| 28 | 8 | Last key, int64 (the key of row n-1); `first <= last`, equal when n = 1. |
| 36 | 4 | CRC-32C (Castagnoli: reflected polynomial `0x82F63B78`, initial value and final XOR `0xFFFFFFFF`; `"123456789"` gives `0xE3069283`) over bytes 0..35 followed by bytes 40..end. |
| 40 | stored length | The stored payload. |

A reader checks, in this order: at least 3 bytes (else `Truncated`), magic (`NotAChunk`), version (`UnknownVersion`), at least 40 bytes and the stored length against the actual length (`Truncated` when shorter, `Corrupt` when longer), the checksum (`ChecksumMismatch`), then the field ranges above (`Corrupt`). After decompression the payload must be exactly the declared length, must be consumed exactly, and its first and last keys must equal the header's.

### Payload primitives

- **varint**: unsigned LEB128, at most 10 bytes; the 10th byte may only be 0 or 1.
- **zigzag**: `z = (v << 1) ^ (v >> 63)` (arithmetic shift), inverse `v = (z >> 1) ^ -(z & 1)`.
- **bit stream**: bits are written most significant first, filling each byte from its high bit; the last byte is padded with zero bits. A reader requires the stream to be consumed to its last byte and the padding to be zero.
- All integer arithmetic below wraps modulo 2^64.

### Integer column (n >= 1 values, int64)

First byte = mode.

- `0` constant: one zigzag varint; every row has this value.
- `1` varint: one byte `order`, then n zigzag varints of the residual series.
- `2` bits: one byte `order`, a varint byte length L, then L bytes of bit stream holding n bucket codes of the zigzag residuals.

`order` (0, 1 or 2) defines the residual series r from the values v: `0` raw, `r[i] = v[i]`; `1` first difference, `r[0] = v[0]`, `r[i] = v[i] - v[i-1]`; `2` second difference, `r[0] = v[0]`, `r[1] = v[1] - v[0]`, `r[i] = (v[i] - v[i-1]) - (v[i-1] - v[i-2])`.

Bucket code: bucket widths `0, 4, 8, 12, 16, 24, 32, 64` bits for buckets 0..7. Bucket k < 7 is announced by k one-bits and a zero-bit, bucket 7 by seven one-bits; then the zigzag value in the bucket's width (bucket 0 carries no value bits: zero).

Encoder choice (pinned by the golden tests): constant if all values are equal. Otherwise the order with the fewest varint bytes (ties: lower order) and the order with the fewest bits (ties: lower order); the bit stream is written when `2 + len(varint(L)) + L` is strictly smaller than `2 + varint bytes`, else the varints. The encoder always uses the smallest bucket that fits.

### Double column (n >= 1 values, handled as their 64 raw bits)

First byte = mode.

- `0` constant: 8 bytes, the raw bits little-endian.
- `1` XOR bytes: for each value, `x = bits[i] XOR bits[i-1]` (`bits[-1] = 0`). `x = 0` is the byte `0xFF`; otherwise one control byte `(lz << 4) | tz`, where lz and tz are the counts of whole zero bytes at the top and bottom of x (`lz + tz <= 7`), then bytes `tz .. 7-lz` of x, least significant first.
- `2` decimal: one byte e (0..15), then an integer column of mantissas m; value `i` is `(double)m[i] / 10^e` (IEEE 754 division, round to nearest; 10^e exact).
- `3` Gorilla: a varint byte length L, then L bytes of bit stream: the first value in 64 bits; then per value `0` = same as previous; `10` + the `64 - lead - trail` meaningful bits of the XOR inside the previous window; `11` + 5 bits leading zeros (capped at 31) + 6 bits meaningful length (0 means 64) + the meaningful bits, which also become the window. The window must be defined before `10` is used.

Encoder choice: constant if all bits are equal. Otherwise the decimal candidate exists when for the smallest e in 0..15 every value is finite, `m = round(value * 10^e)` has `|m| <= 2^53`, and `(double)m / 10^e` gives the original bits. Mode 1, 2 (if it exists) and 3 are built and the smallest is written; ties go to the lower mode.

### String column (n >= 1 nullable strings)

- `0` constant: one entry for every row.
- `1` dictionary: varint count c (1 <= c <= n), c entries in order of first appearance, then an integer column of n indices (< c).

Entry: varint h; `h = 0` is null, otherwise h - 1 bytes of UTF-8 follow (strict: invalid UTF-8 is `Corrupt`).

### Presence (nullable members)

One byte: `0` all rows have a value, `1` no row has one, `2` a bitmap of `ceil(n / 8)` bytes follows, row i in bit `i mod 8` (least significant first) of byte `i / 8`, unused bits zero. The member's value column then holds only the rows that have a value, in row order, and is absent when no row has one.

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
| `UnrepresentableValue` | A bool stored as a byte other than 0 or 1, or a `Time` whose kind is not UTC. |
| `ChunkTooLarge` | One chunk's payload would exceed 64 MiB. |

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

## Data Flow

Rows of one sensor (key order) → validation of the whole input → split into chunks of at most 4,096 rows → per chunk: column streams → zstd level 3 when it makes the payload smaller → header and CRC-32C. Decode reverses it after the header checks.

## Storage / Persistence

The codec stores nothing. ADR-0020 puts chunks in a sibling LevelDB database `SensorValuesSealedV1_<from>_<to>` under the key `sensorId | firstKey` (#1803).

## UI / Operator Visibility

Not operator-visible. The benchmark reports refusals and mismatches for a shard.

## Dependencies

- Depends on: `HSMCommon` value records; `ZstdSharp.Port` 0.8.8 (MIT, pure managed port of zstd, no native binaries).
- Used by: sealed week store (#1803), sealing job (#1804).

## Tests

`src/tests/HSMDatabase.SealedHistory.Tests` (in `HSMServer.sln`, run by the server CI lane):

- round trip of every sealed type with ordinary, extreme and randomized (fixed seed) inputs, one row, the row limit, splitting;
- every MemoryPack-persisted member of every sealed record round-trips and is compared by `RowFidelity` (fails when a record gains a member the codec drops);
- golden chunks under `Golden/` (format v1, synthetic inputs): the committed bytes decode, and encoding the fixed inputs gives the same header and column streams;
- every bit flip and every truncation of a chunk is detected; fuzzed payloads behind a valid checksum fail with `ChunkFormatException` only;
- every refusal reason.

To add a format version: keep the v1 decoder and its golden files, write `v2-*` golden files next to them, and dispatch on byte 2.

## Notes

- Departures from the measured prototype: residual orders are the plain first and second difference for every integer column (the prototype hard-wired delta-of-delta for time); the decimal mantissas use a full integer column (byte or bit level), not always the bit level; the double column picks the smallest of XOR bytes, decimal and Gorilla; `CloseTime` is stored relative to `OpenTime`; the key and `Time` are separate columns, so a `Time` that differs from the key round-trips; strings are a dictionary per column with indices in an integer column; powers of ten are literals, not `Math.Pow`.
- Encoding costs about 0.6 µs per row and decoding about 0.25 µs per row on the measured shard; the densest real sensor (10,061 Rate rows, 3 chunks) decodes in about 0.7 ms.

## Known Issues / Limitations

- Rows with leftover bytes in `Nullable` padding decode with zero padding; their MemoryPack bytes differ from the source blob though every value is equal (see Invariants).
- zstd output is stable for one `ZstdSharp.Port` version. An upgrade may change the stored bytes of new chunks without changing the format; the golden test then fails only on its last assertion.
