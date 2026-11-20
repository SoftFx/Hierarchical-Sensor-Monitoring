# Feature: Sealed history chunk format

> Owner: storage | Last reviewed: 2026-11-19 | Canonical: yes
> Scope: the byte format of a chunk of sealed sensor history (ADR-0020, step 1 of epic #1800), its fidelity rules, its versioning rule, and when the encoder refuses; the sealed week store that reads and changes a sealed week (step 2, #1803).

---

## Overview

A **chunk** is a bounded run of one sensor's history rows encoded as one value: a fixed header, then column streams compressed with zstd. The codec (`HSMDatabase.SealedHistory.ChunkCodec`) is a pure library: it encodes rows into chunks and decodes chunks back into rows. It does not store anything: the sealed week store (#1803, section below) keeps and reads chunks, and the sealing job (#1804) is a later step.

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

A reader checks, in this order: at least 3 bytes (else `Truncated`), magic (`NotAChunk`), version (`UnknownVersion`), at least 40 bytes and the stored length against the actual length (`Truncated` when shorter, `Corrupt` when longer), the checksum (`ChecksumMismatch`), then the field ranges above (`Corrupt`). With compression 1 it then checks, before allocating anything for decompression, that the stored bytes start with the zstd frame magic `0xFD2FB528` (a skippable frame is rejected), that the frame header declares a content size equal to the payload length, and that the frame spans exactly the stored bytes (concatenated frames, a trailing skippable frame or trailing bytes are rejected); all `Corrupt`. After decompression the payload must be exactly the declared length, must be consumed exactly, and its first and last keys must equal the header's. The encoder revision (byte 5) is covered by the checksum like every other header byte but is not range-checked. The zstd compressed bytes for a given payload are those of the pinned `ZstdSharp.Port` version at level 15 (encoder revision 1; level 3 until the pre-release golden set was regenerated on 2026-11-20); a library upgrade that changes them is not a format change, since any valid single frame decodes, but it is a new encoder revision.

### Versioning

Two numbers, with different rules. The **format version** (byte 2) changes only when a reader of the current version could not decode the new chunks correctly: a new column encoding, a changed layout, a changed meaning of a field. A reader refuses a format version it does not know (`UnknownVersion`), and every reader keeps decoding every older format version forever. The **encoder revision** (byte 5) changes whenever the bytes the encoder writes for the same input change while a reader of the same format version still decodes them: a better choice among the existing column encodings, other parameters, a compressor upgrade that changes its output. It records which encoder wrote a chunk (to find chunks worth re-encoding, to trace a bad chunk to its encoder); readers ignore it. Golden chunks are kept per encoder in `Golden/format{F}-encoder{R}/`: every set of a released encoder revision stays in the repository as a decode test forever, and only the set of the current format version and encoder revision is also an encode test ("the encoder writes exactly these bytes"). Until the first release there is one set, `format1-encoder1` (ADR-0020 point 12). It was regenerated deliberately on 2026-11-20, when encoder revision 1 gained the string template mode and zstd level 15: nothing had ever been sealed with format 1, so no stored chunk carries the old bytes, and the format version and encoder revision stayed 1/1. After the first release a set is never regenerated.

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

First byte = mode.

- `0` constant: one entry for every row.
- `1` dictionary: varint count c (1 <= c <= n), c entries in order of first appearance, then an integer column of n indices (< c).
- `2` template: strings as literal segments around numeric slots, below.

Entry: varint h; `h = 0` is null, otherwise h - 1 bytes of UTF-8 follow (strict: invalid UTF-8 is `Corrupt`). A dictionary with duplicate entries or entries no index uses is accepted (the encoder writes neither); a count of 0 or above n, or an index `>= c`, is rejected. Modes 0 and 1 produce at most one string per entry, and every row of an entry shares that instance, so what they allocate is bounded by the payload (64 MiB) in UTF-8 bytes.

#### Mode 2, template

Layout, in order:

1. varint template count c, `1 <= c <= n`;
2. c templates. Each is a varint k: `k = 0` is the null template (the row is null); otherwise `1 <= k <= 256` and k segments follow, each a varint byte length and that many bytes of strict UTF-8 (a segment may be empty). A template of k segments has k - 1 slots, between consecutive segments;
3. an integer column of n template indices (< c);
4. for every template t in dictionary order that has at least one slot and at least one row (u_t rows use it), for every slot s of t in order: three integer columns of u_t values each, over the rows that use t in row order: `M` (signed mantissa), `F` (fraction digits), `Z` (extra leading zeros).

Restoration of a row: the null template gives null; a template of one segment gives that segment (every row of the template shares one string instance); otherwise segment 0, then for each slot its text followed by the next segment. The text of a slot `(M, F, Z)`:

1. `'-'` if `M < 0`;
2. Z characters `'0'`;
3. the decimal digits of `|M|` (ASCII, no sign, no leading zeros, `"0"` for 0);
4. when `F > 0`: those digits left-padded with `'0'` to at least F + 1 digits, and a `'.'` inserted before the last F of them (`M = 50, F = 2` is `0.50`; `M = 5, F = 3` is `0.005`; `M = -50, F = 2` is `-0.50`).

Bounds (each violation is `Corrupt`, checked before the memory it guards is allocated): `1 <= c <= n`; `k <= 256`; a segment length within the remaining payload; an index `< c`; the sum over templates of `u_t * (k_t - 1)` (slot values) at most 262,144, checked before any slot column is read; every M not `-2^63`, every F and every Z in `0..64`; then, before any string is built, the restored length of every row that uses a template with slots (computed from the segment lengths and the slot texts) at most 65,536 UTF-16 code units, and the sum of those lengths at most 4,194,304 (4 Mi) code units. A template of one segment (no slots) has no length limit other than the payload: it is decoded once and shared by its rows, like a dictionary entry, and it is how the encoder stores a string longer than 65,536 code units. A column is read exactly: missing values, extra bytes and slot columns of the wrong length end as `Corrupt` (the payload must also be consumed exactly). A template that no index uses, or a duplicate template, is accepted (the encoder writes neither); a template with slots that no row uses has no slot columns. A row with M < 0 and Z > 0 is restored as written (`'-'`, then the zeros), though the encoder never writes one.

Non-canonical streams. Restoration is a pure function of the stream, so one string can have several valid encodings; the encoder writes exactly one of them (its tokenization, below), and a reader restores whatever it is given by the rules above, without checking that the stream is the encoder's. Accepted, though the encoder never writes them: a slot directly followed by a segment that starts with an ASCII digit, or preceded by one that ends with a digit (slot `1` then segment `"2"` restores `"12"`, which the encoder writes as one slot); a slot followed by a segment that starts with `'.'` and a digit (the encoder would have made one decimal slot); a `'-'` at the end of a segment before a slot with `M > 0` and `Z = 0` (the encoder folds it); duplicate or unused templates; `M < 0` with `Z > 0`. Two slots side by side with an empty segment between them are valid and the encoder does write them: `"5-3"` is the slot `5`, an empty segment, the slot `-3`. Two slots side by side always mean their texts are concatenated.

Why these limits. Template rows are new strings, unlike dictionary rows: without a limit a few bytes (one long segment and a slot used by 4,096 rows, or a huge Z) would make a decoder allocate gigabytes. 4 Mi code units (8 MiB) per column is about 1,000 characters for each of 4,096 rows; a production deployment's week holds strings of at most about 800 characters, typically 30. 65,536 per string keeps one row far below that. 262,144 slot values (6 MiB of mantissa, fraction and zero arrays) is 64 per row at 4,096 rows; the production week has at most about 15 per row. 64 fraction digits and 64 leading zeros are far above any number someone formats, and keep the slot text at most 131 characters. 256 segments (255 slots) bounds the per-template arrays. The encoder respects every limit by construction (below) and writes a dictionary for a column that would pass the column limits.

#### Tokenizing (encoder revision 1)

A string longer than 65,536 code units is one literal segment (a zero-slot template, stored once, shared like a dictionary entry). Otherwise it is scanned left to right; only ASCII `'0'..'9'` form digit runs, every other character (any other Unicode digit, `'+'`, `','`, `'e'`, a lone `'.'`) is literal text and is copied as UTF-16 code units. Strings with a lone surrogate never reach the tokenizer (`InvalidString` refusal); the split points are ASCII characters, so a surrogate pair is never cut. For each maximal run A, while the string has fewer than 255 slots:

1. **Decimal**: if A is followed by `'.'` and another digit run B: Z = the leading `'0'`s of A except its last digit, N = A without them, F = length of B. It is one slot when `F <= 64`, `Z <= 64` and N followed by B has at most 18 significant digits; M is their integer value (`"007.250"` is `M = 7250, F = 3, Z = 2`; `"0.50"` is `M = 50, F = 2, Z = 0`). The scan continues after B.
2. **Integer** otherwise: Z = the leading `'0'`s of A except its last digit; it is a slot when `Z <= 64` and the rest fits in an int64 (at most 19 digits, at most `2^63 - 1`), with `F = 0`. A decimal that failed rule 1 is A by this rule (or rule 3), then `'.'` as literal, then B scanned as a new run.
3. Otherwise A stays literal; runs after the 255th slot stay literal too.
4. **Sign**: a `'-'` that is the last literal character before the slot folds into it (M becomes -M) when `Z = 0` and `M != 0`. So `"-5"` is one slot `-5`; `"-0"`, `"-0.0"` and `"-000.1"` keep a literal `'-'`; `"--3"` is `"-"` and the slot `-3`; `"+5"` keeps `'+'`.

Examples: `"1."` is a slot and `"."`; `".5"` is `"."` and a slot; `"1..2"` two slots around `".."`; `"5-3"` two slots side by side around an empty segment (`5`, then `-3`, the `'-'` folded); `"1.2.3"` is the slot `1.2` (`M = 12, F = 1`), `"."`, the slot `3`; `"10.0.0.1"` is `10.0`, `"."`, `0.1`; a 20-digit run stays literal; `"12345678901234567.8"` (18 significant digits) is one slot, `"123456789012345678.9"` (19) is two slots around `"."`.

Restoring the tokens gives back the string exactly (tested on every shape the tests generate, and on every row of a production week). Templates are collected in order of first appearance; a template is identified by its segment list.

#### Encoder choice (encoder revision 1)

Constant when all rows are equal (ordinal, null equal to null). Otherwise the dictionary encoding is built, and the template encoding when the column qualifies: at least one string has a slot, at most 262,144 slot values, and the strings that use a template with slots at most 4 Mi code units together. The one with fewer bytes is written (before compression); on a tie, or when the template does not qualify, the dictionary. No choice compresses anything. On a production deployment's week (11.9 M rows, 3,990 sensors) this stored 464 B (0.001%) more than the best per-column choice made after zstd, in 42 of 5,423 chunks.

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

The figures above were measured with the zstd level 3 encoder and before the string template mode. On a production deployment's week (11.9 M rows, 3,990 sensors) with the template mode, 8,192 / 16,384 rows per chunk or one chunk per sensor saved 1.1% / 2.0% / 2.0% while the 95th-percentile decode of one String chunk grew from 0.54 ms to 1.3-1.6 ms and the largest decode allocation from 2 to 5 MB; 4,096 stays.

## Measured (2026-11-20, `SealedHistoryBench --seal-shard`)

A production deployment's week: 11.9 M rows, 3,990 sensors (4 File sensors, 28 rows, refused). Encoder revision 1 as specified here (templates, zstd 15), every chunk decoded with `ChunkCodec.Decode` and every row compared by `RowFidelity`, Comment and String values included: 0 mismatches.

| Value type | Sensors | Rows | Stored B | B/row | Share |
|---|---|---|---|---|---|
| String | 1,784 | 7,053,334 | 50,342,545 | 7.14 | 54.8% |
| DoubleBar | 539 | 1,858,502 | 20,912,852 | 11.25 | 22.8% |
| Integer | 432 | 1,328,759 | 7,818,604 | 5.88 | 8.5% |
| IntegerBar | 481 | 700,360 | 5,213,303 | 7.44 | 5.7% |
| Rate | 147 | 483,969 | 2,864,828 | 5.92 | 3.1% |
| TimeSpan | 61 | 146,004 | 2,603,148 | 17.83 | 2.8% |
| Double | 361 | 315,938 | 2,030,876 | 6.43 | 2.2% |
| Boolean, Version, Enum | 181 | 445 | 21,453 | 48.2 | 0.0% |
| **Week** | 3,986 | 11,887,311 | **91,807,609** | **7.72** | |

Against 125,812,706 B (10.58 B/row) with dictionary-only strings and zstd 3, 27.0% less. The source MemoryPack values were 1,335,914,490 B. String value columns: 2,432 chunks template, 50 dictionary, 89 constant; Comment columns: 192 template, 224 dictionary, 5,007 constant. Encode 0.81 µs per row (both string encodings built, zstd 15), decode 0.17 µs per row; decoding one String chunk takes about 0.4 ms (median), 0.54 ms (95th percentile). Remaining bytes: the key and `ReceivingTime` columns are about half of the week (see `docs/initiatives/sensor-history-storage.md`).

## Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.SealedHistory/ChunkCodec.cs` | Public API, header, checksum, zstd, chunking. |
| `src/database/HSMDatabase.SealedHistory/Format/PayloadWriter.cs`, `PayloadReader.cs` | Column order per value type. |
| `src/database/HSMDatabase.SealedHistory/Format/IntColumn.cs`, `DoubleColumn.cs`, `StringColumn.cs`, `StringTemplate.cs` | Column encodings, presence; the string template mode (tokenizing, restoration, bounds). |
| `src/database/HSMDatabase.SealedHistory/Format/RowValidator.cs` | Refusals; the `ReceivingTime` rounding. |
| `src/database/HSMDatabase.SealedHistory/RowFidelity.cs` | The fidelity rule as a comparison. |
| `src/tests/HSMDatabase.SealedHistory.Tests/` | Round trips, persisted-member guard, golden chunks, corruption, refusals. |
| `src/sandbox/SealedHistoryBench/` | Measures a shard: bytes per row, timings, mismatches; `--seal-shard` streams a whole week through the codec with memory bounded by one sensor. |
| `src/database/HSMDatabase.LevelDB/DatabaseImplementations/SealedSensorValuesDatabase.cs` | The sealed week store: merged reads, writes, range-delete rewrite. |
| `src/database/HSMDatabase.LevelDB/Sealed/` | `MergedRowCursor`, `ChunkRowCursor`, `SealedChunkReader` (checks, errors, counters), `SealedWeekWriter` (chunks, then the marker). |
| `src/database/HSMDatabase.AccessManager/SealedWeekMarker.cs`, `SealedHistoryDiagnostics.cs` | The sealed-and-complete signal; process-wide counters. |
| `src/tests/HSMDatabase.LevelDB.Tests/SealedWeekTests/` | Differential tests (plain week vs sealed week), corruption, the signal, File values, concurrency, the compaction marker; `WeekSealer`, the test-only sealer. |

## Data Flow

Rows of one sensor (key order) → validation of the whole input → split into chunks of at most 4,096 rows → per chunk: column streams → zstd level 15 when it makes the payload smaller → header and CRC-32C. Decode reverses it after the header checks.

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

What #1804 must do with it:

- Write chunks while the source rows stay in the overlay, mark the week complete only after verification, and delete source rows only after that. A crash at any point before the marker leaves the week read from its rows alone.
- The switch from rows to the merged view happens when the week is opened; a week already open when it is marked keeps being served as rows until it is reopened.
- A delete or edit of the week between encoding and the marker must not be lost: the job has to detect it (for example with the shard's `ChangeCount`, as `TryMarkCompacted` does) and redo the affected sensor, otherwise a chunk would bring back a deleted row.
- **The marker is not durable against power loss on its own.** The rename that publishes it is not followed by an fsync of the directory (.NET offers none), so on Linux a power loss can lose the rename while the chunks it describes survive. Source rows must therefore never be deleted on the strength of a marker written in the same run without the week having been reopened as sealed since; the keep period covers this in practice. A sealed directory that lost its marker is visible only as the "incomplete seal" warning when the week is opened, and is rebuilt by the next seal.

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

No lock on reads. A read creates the overlay iterator first and the chunk iterator second; each LevelDB iterator reads the snapshot of its creation. A range delete changes the chunks first and the overlay second. A reader therefore sees the old chunks with the old overlay, or the new chunks with an overlay the delete has partly or fully gone through: each row it returns is a row the week held before, or the delete removed it; it never sees a duplicate, a mix inside one chunk, or a chunk row an overlay row used to hide. Chunk writers (range deletes) are serialized by a lock, because a rewrite is planned from the chunks it read.

### Errors

A chunk that fails to decode, or does not fit its key (the header's first key differs from the stored key, chunks overlap), raises `SealedChunkReadException` (in `HSMDatabase.AccessManager`, so the server catches it by the access layer's type). Every failure is counted per week (`ChunkReadFailures`) and process-wide (`SealedHistoryDiagnostics.ChunkReadFailures`); a given chunk is logged as an error once per process (by database and chunk key) and at debug level after that, because a corrupt chunk stays corrupt and its readers retry. The counters are not wired into the server's self-monitoring sensors yet.

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

## UI / Operator Visibility

Not operator-visible. The benchmark reports refusals and mismatches for a shard.

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
- zstd payloads that are not exactly one frame with a declared content size are rejected before decompression;
- string template mode (`StringTemplateTests`): every restoration rule and every tokenizing rule table-driven; tokenize-then-restore is the identity on 50,000 generated shapes; the caps leave runs literal; "whatever `Encode` accepts, `Decode` returns" for 1,500 String and Integer inputs whose values and comments come from templates and numeric shapes (leading zeros, "-0", "-0.0", "-000.1", "1.", ".5", "1..2", "1.2.3", "10.0.0.1", "--3", "+5", runs of 17 to 20 digits, F and Z at and beyond their caps, non-ASCII digits, null against empty, zero-slot strings, a different template on every row; seeded, the seed logged, `HSM_STRING_TEST_SEED` replays another); a string longer than 65,536 characters with digits among rows with slots, and strings exactly at and one above the limit, end to end through `ChunkCodec` for a String value column and a Comment column; the slot value and column character limits exactly at and one past them, for the encoder (qualifies or falls back to the dictionary) and the decoder (decodes or `Corrupt`, hand-built columns); the encoder choice pinned per column kind and against both encodings built directly; every decoder bound violated one at a time is `Corrupt`, the slot and character limits before the guarded memory is allocated (allocation measured); payload fuzzing behind a valid checksum fails cleanly and never allocates 32 MB in one decode; every bit flip and truncation of a template chunk is detected; bit flips with the checksum recomputed never crash the parser (some decode to other valid strings: the checksum is what catches those);
- the golden set pins the string column choice (every template case is mode 2, the set holds modes 0, 1 and 2) and the zstd level (`ChunkCodec.ZstdLevel` is 15; the encode test catches a level change only where the two levels write different bytes, 14 against 15 on one case).

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

## Notes

- Departures from the measured prototype: residual orders are the plain first and second difference for every integer column (the prototype hard-wired delta-of-delta for time); the decimal mantissas use a full integer column (byte or bit level), not always the bit level; the double column picks the smallest of XOR bytes, decimal and Gorilla; `CloseTime` is stored relative to `OpenTime`; the key and `Time` are separate columns, so a `Time` that differs from the key round-trips; strings are a dictionary per column with indices in an integer column, or templates with numeric slots (mode 2, added 2026-11-20 from two measurement rounds on a production week, `docs/initiatives/sensor-history-storage.md`); powers of ten are literals, not `Math.Pow`.
- Encoding costs about 0.8 µs per row and decoding about 0.17 µs per row on a production deployment's week (see Measured).

## Known Issues / Limitations

- Rows with leftover bytes in `Nullable` padding decode with zero padding; their MemoryPack bytes differ from the source blob though every value is equal (see Invariants).
- zstd output is stable for one `ZstdSharp.Port` version. An upgrade may change the stored bytes of new chunks without changing the format; the golden encode test then fails only on its last assertion, and the upgrade needs a new encoder revision.
