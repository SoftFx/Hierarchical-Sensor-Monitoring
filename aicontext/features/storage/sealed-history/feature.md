# Feature: Sealed history chunk format

> Owner: storage | Last reviewed: 2026-11-20 | Canonical: yes
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
| `src/tests/HSMDatabase.LevelDB.Tests/SealedWeekTests/` | Differential tests (plain week vs sealed week), corruption, the signal, File values, concurrency, the compaction marker; `WeekSealer`, the test-only sealer; the sealing job's planner, life cycle, interruptions and races. |
| `src/database/HSMDatabase.LevelDB/Sealed/SensorValuesWeek.cs` | The week handle: gate, delete logs, leases, live swap (#1804). |
| `src/sandbox/SealedHistoryBench/KeepPeriodBench.cs` | `--keep-period <dir> <phase>` (phases `build`, `reads`, `memory`, `hours`, `compression`): sealed weeks in their keep period against the same weeks unsealed; read cost, process memory, a long run looking for a leak (#1805 follow-up). |
| `src/database/HSMDatabase.LevelDB/Sealed/WeekSealingJob.cs` | Seal, source-row removal, fold, unseal of one week (#1804). |
| `src/database/HSMDatabase.LevelDB/Sealed/RowsLeftRecord.cs`, `DurableFile.cs` | The sensors left as rows; marker writes with a directory sync. |
| `src/database/HSMDatabase.AccessManager/SealedWeekStages.cs`, `DatabaseSettings/HistorySealing.cs` | Marker names and the stage of a week; settings and the pass report. |
| `src/database/HSMDatabase/DatabaseWorkCore/HistorySealingPlanner.cs`, `DatabaseCore.cs` (region "Sealing job") | Which week and action next; the pass and the unseal. |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/HistorySealingService.cs`, `ServerConfiguration/Sections/HistorySealingConfig.cs`, `.../Nodes/Database/HistorySealingSensors.cs` | The service, its configuration, its self-monitoring. |

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
| chunk database, key `sensorId` (16 bytes) | The sensor's **delete watermark** (#1805, `SensorWatermark`): every chunk row of the sensor with a key at or below `sensorId | tick` is deleted. Written by a prefix delete (below). The key cannot be a chunk key (24 bytes) and sorts right before the sensor's chunks. Value, version 1, 13 bytes: the version byte (1), the tick (8 bytes, big-endian), a CRC-32C (little-endian) over the key and the first 9 bytes. Any other version, length or checksum is unreadable. |

**The signal.** A week is sealed only when `hsm-sealed` exists inside the chunk directory. `SealedWeekWriter` writes every chunk batch with a sync, closes the database, writes the marker as a temporary file flushed to disk and renames it; the marker is therefore never over chunks that a crash or a power loss can take back. `LevelDBManager.GetSensorValuesDatabaseInstance(name, sealedName, from, to)` opens a sealed week when the marker is there and the plain row worker otherwise, with a warning in the log when the chunk directory exists without it (an interrupted or running seal; it is not read). The marker lives with the chunks, so it travels with them on a copy or restore. The weekly shard dictionary also lists a week that has a complete chunk directory but no row directory (opening it creates an empty overlay); a chunk directory without the marker is not a week by itself. The pre-V2 dictionary used by `MigrateDatabaseV2` never attaches sealed weeks.

`SealedWeekWriter.Open` always starts from an empty chunk database. Since #1804 it writes `hsm-sealing` into the new directory before the database exists, and it deletes an existing directory only when it is provably the remains of an interrupted seal (`SealedWeekStatus.MayDeleteChunkDirectory`, section "Sealing job", A): such a directory is not data (its source rows are still in the row database) and stale if those rows changed since. Any other existing directory without the marker is refused untouched (`WeekNeedsAttentionException`): after source-row removal it may hold the only copy of the week. A directory with the marker is refused, and so is any directory whose name does not start with `SensorValuesSealedV1_`, before anything is touched: a wrong path (the row database, the database folder) is never deleted.

The hand-off of step 2 to the sealing job, and how #1804 meets it (details in the section "Sealing job"):

- Chunks are written while the source rows stay in the overlay, the week is marked complete only after verification, and source rows are deleted only after the keep period. A crash at any point before the marker leaves the week read from its rows alone.
- A week is opened as sealed or as rows by its marker. Since #1804 the shard dictionary holds a `SensorValuesWeek` handle per week, and the job swaps a week to the sealed store live, in the same step that writes the marker.
- A delete of the week between encoding and the marker must not come back from a chunk: the job keeps a delete log while it encodes and drops the chunks of every sensor a delete touched (a write, as opposed to a delete, is harmless: the overlay wins).
- **The marker's durability** (`DurableFile`). On Windows every marker is renamed into place with `MoveFileExW(..., MOVEFILE_WRITE_THROUGH)`, which returns once the move is on the disk. On Linux and macOS the directory is synced after the rename (`fsync` on the directory through libc; .NET has none), and the database folder is synced for the chunk directory's own entry when the directory is created. A failed sync after `hsm-sealed` (including a missing libc symbol) fails the seal before the swap: the marker is deleted again, the week stays rows and is sealed again after the backoff. A failed sync after any other marker is logged as an error once per process and counted (`SealedHistoryDiagnostics.DirectorySyncFailures`, self-monitoring `Chunk directory sync failures`): those markers only shorten or repeat a step when lost. The source rows are removed only once the keep period (at least one week by configuration) has passed and `hsm-sealed` is still on disk. See "Deviations" in the job section for how this reads the "reopened as sealed" rule.
- **One residual assumption: the wall clock.** The keep period ends at a wall-clock time written at seal time. A clock that jumps forward by weeks shortens it accordingly.

### Merged reads

Every read outside the keep period (see "Overlay-only reads" below for the keep period) opens a `MergedRowCursor` over two children: the overlay's LevelDB iterator and a `ChunkRowCursor` over the chunk database. The merged stream is in key order; on an equal key the overlay row is returned and the chunk row skipped, in both directions (the children are positioned like LevelDB's own merging iterator). The read methods then run the algorithms of `LevelDBDatabaseAdapter` line for line over that stream, quirks included (`Get` returns the first row at or after the key, `GetLastAndFirstValues` finds no last value when the seek runs past the end), so a sealed week answers exactly what a row database holding the same rows answers. `FillLatestValues` seeks to the last row before `toKey` instead of walking the sensor's rows (the same answer whenever `toKey` starts with the sensor prefix, as `DatabaseCore` always passes; otherwise it walks).

Values leave the interface as MemoryPack bytes: a chunk row is decoded and re-serialized. Such a value equals its source by `RowFidelity` (field by field, `ReceivingTime` to the millisecond), not necessarily byte for byte; an overlay row comes back byte for byte.

**Delete watermarks.** A chunk row at or below its sensor's watermark is not there: the chunk cursor steps over the 16-byte watermark keys and filters the rows of the one chunk per sensor that reaches below its watermark (the writers keep it so: only a sensor's first chunk can, and never with all its rows; a chunk that would be wholly below it is a read failure like a corrupt chunk). The watermark is read from the same snapshot as the chunks (the cursor takes an explicit snapshot for both), once per sensor, and only when a row key or value of that sensor is needed. The merged cursor steps back from the forward direction without deciding which child holds the current row (each child is at its first row at or after it, so each steps back once; review W1 of #1805): a read of a sensor that lands on the next sensor's rows and finds them not its own by their prefix (the latest-value reads, a page newest first) neither reads that sensor's watermark nor decodes its chunk, also when that sensor has overlay rows. Pinned by `ReadsOfASensor_NeverReadTheNextSensorsWatermarkOrChunk`: `Get`, `GetLatest`, `GetFirst`, both range directions, `GetLastAndFirstValues` and `FillLatestValues` of a sensor answer like the plain week with the next sensor's watermark or partly deleted chunk damaged, count no read failure, and on healthy data decode none of the next sensor's chunks. Overlay rows are never hidden: a value written at an old timestamp after a delete is a real value, as in a row database. An unreadable watermark fails the reads of its own sensor that need its chunk rows (`SealedChunkReadException`, logged once and counted in `ChunkReadFailures`); ignoring it would bring deleted rows back.

Cost: a chunk is read only when needed. Arriving at a chunk's first row costs nothing (its key is the chunk's key; the chunk's bytes are not even fetched); the key of its last row reads the verified header; a value or a step inside the chunk decodes it. A seek decodes at most the one chunk that can hold the target. Range reads are lazy in both directions, chunk by chunk, and a page read stops after the chunks its rows come from (plus, going backwards, the header of the chunk before, if it belongs to the same sensor).

**A chunk of another sensor is never read on the way.** Every row of a chunk carries the sensor prefix of the chunk's key, so against a key of another sensor the chunk is ordered by that prefix alone: a seek does not read the header of the previous sensor's last chunk, a backward read stops at a sensor boundary without reading the chunk beyond it, and a range delete does not read the chunk before `from` when it belongs to another sensor. The merged cursor decides which child holds its current row only when the row's key or value is needed or the cursor moves; a prefix or order question both children answer alike is answered without deciding. So a read of a sensor that lands backwards on the previous sensor's rows (it has no row at or before the target, as an idle sensor's history load does) learns "not mine" from their prefix, without ordering that sensor's overlay row against its chunk. A corrupt chunk therefore fails only reads and deletes of its own sensor, and costs no CRC pass on its neighbours' reads.

### Overlay-only reads during the keep period (#1805 follow-up)

Found on the first rollout: during the keep period the overlay still holds every source row, an overlay row wins on an equal key, so every merged read answered from the overlay alone, yet it also seeked the chunk database, fetched chunk values to read their headers and decoded the chunks its rows fell into, only to hide all of their rows. Measured on Linux (synthetic: 9 weeks, 7 sealed and kept, 670 sensors, about 690,000 rows and 76 MB per week; `SealedHistoryBench --keep-period`, "Measured" below): a page of 100 of each sensor in each week took 970 ms, allocated 1.7 GB and decoded 9,296 chunks against 239 ms and 209 MB for the same weeks unsealed; a whole dense sensor in one week 37.8 against 21.2 ms and 37 against 12 MB. Now a sealed week whose overlay holds every row is read exactly like its row database.

**The invariant.** From the seal until the job deletes its first overlay row, every live chunk row (above its sensor's watermark) has an overlay row with the same key: the seal builds the chunks from one overlay snapshot and drops the chunks of every sensor a delete touched while it encoded (C); writes only add or replace overlay rows; a range delete changes the chunks before the overlay, so a chunk row is gone before its overlay row (both the prefix and the rewrite path); an unseal only adds overlay rows; a fold runs only after source-row removal. On an equal key the overlay row wins, so the merged stream equals the overlay. The only code that deletes overlay rows and leaves their chunk rows is the job's compare-then-delete (source-row removal, a fold's overlay deletes, `WeekSealingJob.RemoveOverlayRowsEqualToChunks`).

**Durable state that says so** (`SealedWeekStages.OverlayHoldsEveryRow`, read when the store opens): `hsm-sealed` is there, neither `hsm-source-rows-removing` nor `hsm-source-rows-removed` is, and the keep time is known and more than a day (`SealedWeekStages.OverlayOnlyKeepMargin`) ahead of the clock, taking the earlier of the keep file and the keep line of `hsm-sealed`. The clock is the database core's (`DatabaseCore.Clock`, the system clock in the server; the job's tests set their own). The stage markers alone would not do: removal deletes overlay rows chunk by chunk and writes `hsm-source-rows-removed` only at the end. So removal now announces itself: before its first overlay delete, `RemoveOverlayRowsEqualToChunks` makes `hsm-source-rows-removing` durable (written with a strict sync, or synced again strictly if an earlier attempt wrote it; a failed sync fails the step before anything is deleted), then ends overlay-only reads in the store (`StopOverlayOnlyReads`). The keep-time condition covers weeks a build from before the announcement started to remove: it wrote no marker and removed only once the keep time had passed; the one-day margin keeps such a week merged also after the clock is corrected backwards by up to a day (`ARemovalBegunByAnOlderBuild_WithoutTheMarker_ReadsMergedOnceTheKeepTimeHasPassed`). Any doubt answers "merged", which is right in every state.

A seal hands the new store `overlayHoldsEveryRow: true`. Before it writes `hsm-sealed` it makes the overlay independent of its write-ahead logs (review R1): rows written without sync before the seal's snapshot can sit in the current memtable or in one waiting to be flushed, whose logs a power loss can take back, while their chunk rows are synced. `LevelDBDatabaseAdapter.FlushMemTablesToTables` calls LevelDB's `CompactRange` over the single key `[0x00]`, below every 24-byte row key: LevelDB first flushes both memtables into table files and waits for that, then compacts the tables overlapping the range, which are none. Checked against the bundled native builds on Windows and Linux (`MemTableFlushTests`): after it, every row written without sync survives the truncation of every `*.log` file to zero bytes (2,000 rows that stay in the current memtable, and 9,500 rows that switch the 8 MB memtable right before the step), and every table written before it is unchanged; without the step, or with an empty synced write instead (which syncs only the current log), rows are lost in both cases (Windows: 0 of 2,000; 0 or 7,725 of 9,500). That the new table file and the MANIFEST reach the disk is LevelDB's own guarantee (stock LevelDB syncs both; no test without a power loss can show it). Cost on a synthetic week (76 MB of rows, Linux): 25 ms with an empty memtable, 31 ms after 20,000 unsynced late values (one new 1.7 MB table), no table rewritten; on Windows 14 to 20 ms.

**Readers across the switch.** A read creates its overlay iterator first, and only then asks whether reads are overlay-only. The job ends overlay-only reads before its first overlay delete, and LevelDB creates an iterator and applies a write under the database mutex, so: if the read still finds overlay-only reads on, its iterator's snapshot was taken before the first delete and holds every row, and the overlay alone is the answer; if it finds them ended, it opens the chunk iterator and merges, as before (the overlay iterator still comes first). A read that started overlay-only and runs while removal steps delete overlay rows keeps its overlay snapshot, which holds every row. Pinned by `AReadThatDecidesAfterTheRemovalBegan_ReadsMerged` (the read's test seam runs the whole removal between its overlay iterator and its decision; mutation-checked: deciding before creating the iterator reads 0 of 9,000 rows) and `AReadAcrossTheFirstRemovalSteps_SeesEveryRow`.

**What an overlay-only read touches:** the overlay iterator, nothing else. No chunk iterator, no watermark read, no header, no decode; a corrupt chunk or an unreadable watermark does not fail it (the overlay holds the rows, so the answer is right). `FillLatestValues` and `GetLastAndFirstValues` never meet a `SealedChunkReadException` then. Overlay-only reads are a per-instance flag set at most once to off; a reopen asks the files again.

The chunk database is opened with the store, as before, also when reads use the overlay alone: range deletes must change the chunks too, or the deleted rows would come back once reads merge, and an open idle chunk database costs about 0.2 MB and four file descriptors (measured below).

What stays as before: writes, both delete paths (chunks first, then the overlay), the watermark, the delete logs and gates of the job, and every read once source-row removal begins. A store opened overlay-only stays so after the keep time has passed, until the job announces the removal; a store opened after the keep time (less the margin) reads merged.

### Writes and range deletes

`PutSensorValue` / `PutSensorValueOrThrow` write the overlay, exactly as the row worker does. `RemoveSensorValues(from, to)` takes one of two paths for the chunks, then deletes the overlay rows in the range.

**A prefix delete marks (#1805).** `from` and `to` are keys of the same sensor and no live chunk row of it lies below `from`: the sensor's first chunk starts at or after `from`, or it reaches below the watermark and `from` is at most one tick above the watermark. That is what retention, clear history and sensor removal send: `DatabaseCore.ClearSensorValues(sensor, sensor.From, to)` with `From` the previous cutoff (`Cut(to)` after every clear) or, after a start, the oldest row (`DateTime.MinValue` before the history loaded), and sensor removal `DateTime.MinValue .. DateTime.MaxValue`. Under the chunk write lock, without decoding anything, one synced batch: deletes every chunk of the sensor whose rows all lie at or below `to` (the next chunk's key is at or below `to`, or `sensor | (To - 1)` is, or the verified header's last key is); if a chunk still straddles `to` (or is unreadable and not provably inside), raises the watermark to `to`, never lowering it; deletes a watermark no remaining chunk reaches below. A delete that changes nothing writes nothing. So at most one partly deleted chunk per sensor and week exists, and its deleted rows go physically when a later prefix delete passes its last row, a fold rewrites the sensor, or a range delete rewrites the chunk. Bytes written per pass: a few chunk-key deletes and at most one 29-byte watermark per sensor. With an unreadable watermark the sensor's live chunk rows are unknown, so a delete that would have to know them is refused: on the prefix path one whose cutoff falls inside a chunk (the watermark cannot be raised without knowing it); and retention and clear history usually do not take the prefix path then, since their `from` (the previous cutoff) lies above the straddling chunk's key and only the first-chunk rule can be checked, so they take the rewrite path, which cannot drop the hidden rows and refuses as well. A refused delete changes nothing, chunks or overlay. Every refusal is counted in `ChunkRewriteFailures`, the error is logged once per sensor and opened week (later refusals at Debug), and the week is reported under "Weeks needing attention" with the advice to unseal it. A delete that removes every chunk of the sensor (sensor removal, a clear past the week) still goes through and deletes the watermark with them. The way out is the unseal (section G): it restores every row of the sensor's chunks.

**Any other range rewrites** (a range in the middle of a sensor, across sensors, or below live rows):

1. Under the chunk write lock, plan from the chunks that have a row in the range: a chunk wholly inside is deleted; a chunk on a border is decoded, its rows outside the range and above the sensor's watermark re-encoded (an unreadable watermark refuses the delete) (verified by decoding and `RowFidelity` before anything is written) and stored under the key of its new first row; an unreadable chunk is deleted only when it provably lies wholly inside the range: its key is in the range, and either the next chunk's key or `sensor | (To - 1)` is too. The second bound is an invariant of a weekly database: every row has `From <= tick < To` (`To` is the next Monday 00:00), because `DatabaseCore.AddSensorValue` stores a value in the week of its own `Time` under that same tick and the sensor-move merge copies a row within its week under the same tick. So a retention or clear-history delete that reaches the end of the week removes an unreadable chunk; one that ends inside the week is refused, as it must be (the chunk might hold rows after the cutoff).
2. Apply all of it as **one synced LevelDB write batch** to the chunk database.
3. Delete the overlay rows in the range, as the row worker does.

Crash states: before step 2, nothing has changed. Step 2 is atomic: every touched chunk is old or every one is new, so rows outside the range are never lost and rows inside never return from a chunk. Between step 2 and the end of step 3, overlay rows inside the range may remain; each of them is the value the week held before the delete, and the next delete of the range (retention re-runs) removes them. The opposite order would be worse: overlay rows deleted first would let the chunk rows they hid (older values of edited keys) reappear. Step 2 is synced so that a power loss cannot drop it while keeping the overlay deletes of step 3, which live in another database.

If a border chunk cannot be decoded or re-encoded, the whole delete is refused: nothing is written, the overlay is untouched, the error is logged and counted (`SealedHistoryDiagnostics.ChunkRewriteFailures`).

Crash states of a prefix delete: the chunk deletes and the watermark are one synced batch, so they happen together or not at all; between it and the overlay delete, overlay rows at or below `to` may remain, each the value the week held before, and the next delete (retention re-runs) removes them. Watermark keys are compacted away with the rest of the chunk database.

The rewrite path is left for deletes that are rare and operator-initiated (clear history of a middle range, sensor-move merges never delete): it is not followed by a compaction of the chunk database; the next compaction of the week (step 0, or the one after source-row removal) reclaims the space.

There is no single-row delete on `ISensorValuesDatabase`; every history delete (retention, clear history, sensor removal) is a range, so a sealed week needs no tombstones.

### Concurrency

No lock on reads. A read creates the overlay iterator first and the chunk iterator second; each LevelDB iterator reads the snapshot of its creation. A range delete changes the chunks first and the overlay second. A reader therefore sees the old chunks with the old overlay, or the new chunks with an overlay the delete has partly or fully gone through: each row it returns is a row the week held before, or the delete removed it; it never sees a duplicate, a mix inside one chunk, or a chunk row an overlay row used to hide. Chunk writers (range deletes, and the job's folds) are serialized by a lock, because a rewrite is planned from the chunks it read. A prefix delete keeps the same guarantee: its chunk deletes and watermark change together, and a read sees the chunks and the watermark of one snapshot, so it sees either the old chunks with the old watermark or the new ones with the new; the rows it hides are rows the delete removes. Since #1804 every write and range delete also holds the week handle's gate shared for its whole duration, both phases of a range delete included, and the sealing job's commit steps take that gate exclusively (section "Sealing job", parts C and D); reads still take no lock. During the keep period a read uses its overlay iterator alone (see "Overlay-only reads" for why a read that decides after creating it is safe across the switch).

### Errors

A chunk that fails to decode, or does not fit its key (the header's first key differs from the stored key, chunks overlap), raises `SealedChunkReadException` (in `HSMDatabase.AccessManager`, so the server catches it by the access layer's type). Every failure is counted per week (`ChunkReadFailures`) and process-wide (`SealedHistoryDiagnostics.ChunkReadFailures`); a given chunk is logged as an error once per process (by database and chunk key) and at debug level after that, because a corrupt chunk stays corrupt and its readers retry. Both process-wide counters (`ChunkReadFailures`, `ChunkRewriteFailures`) are posted by the self-monitoring under `Database/Sealed history` since #1804.

How it reaches the caller, per method:

| Method | On an unreadable chunk |
|---|---|
| `GetValuesFrom`, `GetValuesTo`, `GetKeysValuesTo`, `GetAll` | Throw from the enumeration: a range read never ends early as if the data ended. |
| `Get`, `GetLatest`, `GetFirst` | Throw. The row worker answers "no value" for a failed read, but `DatabaseCore.GetLatestValue` / `GetFirstValue` read null as "this week has none" and would answer with another week's value; a stale newest or oldest value must not be taken for the sensor's own. Any other failure keeps the row worker's fallback. |
| `FillLatestValues`, `GetLastAndFirstValues` | Per sensor: the sensor whose chunk cannot be read gets nothing from this week (its entry stays as the caller had it), every other sensor is answered, and the dictionary the caller accumulated is always returned. Neither has a caller in the server today; `DatabaseCore` would fill such a sensor from another week. |
| `CalculateHistorySize` | Never throws (it is informational: the history info of a node, the delete preview). An unreadable chunk adds its stored bytes; the count falls back to a lower bound (overlay rows, the readable chunks' rows no overlay row hides, and for an unreadable chunk wholly in the range with a readable header its row count less the overlay rows in its span). |
| `RemoveSensorValues` | Rewrite path: refused when a border chunk cannot be rewritten (logged, counted in `ChunkRewriteFailures`); an unreadable chunk wholly inside the range is deleted with it (see above). Prefix path: an unreadable chunk at the cutoff is kept under the raised watermark, not refused. With an unreadable watermark: see "A prefix delete marks" (refused unless every chunk of the sensor goes; logged once per sensor, counted, the week reported). |
| Any read of a sensor whose watermark cannot be read | Throws when the read needs a chunk row of that sensor, like an unreadable chunk. The reads of the sensor before it in key order that land on its rows are not affected (pinned by the test named in "Delete watermarks"). |

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

A seal changes no row, so the week's row database keeps its marker and the background compaction does not visit a freshly sealed week until its source rows are removed. The seal therefore compacts the chunk database itself (`SealedWeekWriter.Compact`, since #1805), before the commit and outside the week's gate: the write-ahead log the chunk batches went through becomes one sorted table, and the directory is about the size of its chunks. Without it the whole week stayed in the log (measured: 4.8 MB of chunks, 4.8 MB of log; zstd chunks do not shrink further, so the size is about the same, but the log is replayed at every open and stays until LevelDB flushes it).

**Retention through a sealed week** (found on the first rollout, fixed by #1805). Until #1805 a retention delete whose cutoff fell inside a chunk re-encoded that chunk; the hourly retention pass moves every sensor's cutoff through the oldest kept week, so each pass re-encoded about one chunk per sensor, the whole week when a sensor has one chunk per week, and the rewrites piled up in the chunk database's log and level-0 tables (a test week of 0.73 MB of chunks reached 9.0 MB on disk; the rollout's trimmed week 16 MB for 2.5 MB of chunks), while the 3-hour quiet period kept step 0 away. Since #1805 such a delete is a prefix delete and marks a watermark instead ("Writes and range deletes" below): measured on a synthetic week of 20 sensors with one week-long chunk each, trimmed hourly for 166 hours, it wrote 96,280 bytes in all (one 29-byte watermark per sensor and pass) and decoded no chunk; the chunk directory stayed between its sealed 913,365 bytes and 1,083,973 bytes. The same week on the rewrite path (the prefix path switched off) re-encoded 3,320 chunks, wrote 76,606,290 bytes (about 461 KB per pass) and reached 21,216,555 bytes on disk.

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

These figures are for weeks whose source rows are gone. During the keep period a sealed week is now read from its overlay alone and costs what the plain week costs (next section).

A point read or a first page costs one chunk decode (about 0.2 ms) instead of microseconds; whole-week and dense-sensor reads get faster. What runs when a sensor's history loads (at its first use after a start) is `DatabaseCore.GetLatestValue` plus `GetFirstValue`. `GetLatestValue` walks the weeks newest first and stops at the first with a value, so a sealed week is reached only for a sensor without a value in any newer week; `GetFirstValue` walks the oldest week first, so it reaches a sealed week for every sensor whose oldest week is sealed. Each such sealed week costs about 0.1 to 0.2 ms per sensor instead of 20 to 40 µs (two chunk decodes). `GetLastAndFirstValues` has no caller in the server (its only caller is commented out in `TreeValuesCache`); it went from 49 to 35 ms on the production copy once a seek stopped reading the previous sensor's chunk header.

### Measured: the keep period, memory, a long run (2026-11-20, `SealedHistoryBench --keep-period`)

Synthetic: 9 weeks, the 7 oldest sealed by the job and in their keep period, 670 sensors (one value every 2 to 40 minutes, a few File sensors, one dense sensor at one value per second for a day), about 690,000 rows and 76 MB per week. Linux (`mcr.microsoft.com/dotnet/sdk:8.0` container, 4 CPUs, Server GC as the web server runs), the same week read plain and sealed; per read: median time, managed allocation, chunk decodes.

| Read | Plain | Sealed, before | Sealed, overlay-only |
|---|---|---|---|
| `GetLatest`, 2-minute sensor | 0.002 ms, 0.4 KB | 0.029 ms, 29 KB (the chunk fetched for its header) | 0.001 ms, 0.4 KB |
| Page of 100, newest first | 0.037 ms, 15 KB | 0.556 ms, 414 KB, 1 decode | 0.028 ms, 15 KB |
| Whole dense sensor in the week (86,400 rows) | 21.2 ms, 12 MB | 37.8 ms, 37 MB, 22 decodes | 15.6 ms, 12 MB |
| History load, 670 sensors over 9 weeks | 3.6 ms, 1.2 MB | 4.4 ms, 1.7 MB | 2.7 ms, 1.2 MB |
| `GetLatestValue` as of the second-oldest week, 670 sensors | 2.0 ms, 0.5 MB | 11.2 ms, 9.1 MB | 1.5 ms, 0.5 MB |
| Page of 100 both ways, every sensor and week | 239 ms, 209 MB, GC 7/0/0 | 970 ms, 1,707 MB, 9,296 decodes, GC 57/19/0 | 243 ms, 209 MB, GC 7/0/0 |

Process memory after the start, the history load and two rounds of every page read, 512 MiB container limit (the copy of the database made in another container, so its page cache is not charged here):

| | Plain | Sealed, before | Sealed, overlay-only |
|---|---|---|---|
| Anonymous RSS (cgroup rss) | 51.9 MB | 60.6 MB | 51.8 MB |
| Container figure (usage less inactive file pages) | 62.1 MB | 74.3 MB | 61.9 MB |
| Managed heap after a full GC / committed | 0.2 / 45.3 MB | 1.4 / 44.5 MB (54.0 after the first round) | 0.2 / 45.1 MB |
| File descriptors | 110 | 162 | 112 |

What one open LevelDB database costs in this build (`LevelDBDatabaseAdapter`: write buffer 8 MB, 200 KB blocks, Snappy requested, `MaxOpenFiles` 100,000, LevelDB's default 8 MB block cache per database): opened and idle, about 0.2 MB of native memory and 4 file descriptors (7 chunk databases: +1.3 MB, +28 descriptors); the memtable grows only with writes, the block cache only with reads. Table files are memory-mapped (up to LevelDB's limit of 1,000 per process) and their pages count as file-backed memory of the process, reclaimable under the container limit (seen at the end of the long run below). A block is put in the block cache only when it was compressed: mapped blocks stored raw are read in place. **The Linux build of the native LevelDB (SoftFX.LevelDB.Standard 1.2.1) has no Snappy**: 20 MB of zeros compact to 19.8 MB on Linux and 1.1 MB on Windows (`--keep-period <dir> compression`), so on Linux row tables are stored uncompressed (about 110 bytes per row here against 35 on Windows) and neither row nor chunk blocks ever enter the block cache. Chunk blocks hold zstd data and are stored raw on both platforms: a chunk database's block cache stays empty (4 KB on Windows after reading every page of every week merged, against 6.5 MB for each row database). So the chunk database's options (block cache, compression, write buffer) cost nothing measurable, and they are left as they are.

Every week is opened after a start: `DatabaseCore.GetFirstValue` enumerates the weeks oldest first (`Reverse()` opens all of them), so the history load of the first sensor opens every week; hourly retention opens every week from the newest down to the one with the cutoff. The sealing planner reads marker files only, except for a week whose source rows are gone, which it opens to count foldable rows; the compaction step reads the marker of a week not opened yet and opens only the week it compacts.

A long run (`--keep-period <dir> hours`, 300 simulated hours on the sealed set, 512 MiB): every hour 40,200 writes into the current week and 20 late values into sealed weeks, the history load of 50 sensors, a page of 100 both ways of 50 sensors in random weeks, a retention pass moving the cutoff of all 670 sensors one hour through the oldest sealed week, 8 sealing passes with nothing due and 6 compaction steps; every tenth hour a page read is abandoned without disposal. After full collections the managed heap stayed at 1.6 to 1.7 MB (before) and committed at 45 to 53 MB from hour 25 to hour 300; anonymous memory went from 84 to 93 MB while the current weeks grew by about 1.3 GB of new rows (the plain set: 76 to 78 MB); the dead finalizable objects found by each full collection (`GCMemoryInfo.FinalizationPendingCount`) were the LevelDB handles of the abandoned reads (four per sealed read: two iterators, the chunk snapshot and its read options) on top of a constant 6 (2 on the plain set) that did not grow with the reads; the lease count of the week rose by one per abandoned read, as documented in B (no other lease stayed). No leak. The mapped table files went from 71 to about 890 as the current weeks grew, on the plain set alike. The same run with overlay-only reads: managed heap 0.5 MB after full collections (as plain), committed 44 to 49 MB, anonymous memory 72 to 87 MB, one finalizable handle per abandoned read (as plain).

## Sealing job (#1804)

`HistorySealingService` (in the server, every 7 minutes, first pass 15 minutes after start) calls `IDatabaseCore.RunHistorySealingPass(utcNow, settings)`. A pass does at most one action on one week; `DatabaseCore` picks it with `HistorySealingPlanner` (pure: the week states and the clock are parameters) and runs it with `WeekSealingJob` (`src/database/HSMDatabase.LevelDB/Sealed/`). When `HistorySealing.Enabled` is false the service does not call the database, and the database would answer `Disabled` without looking at anything: nothing is created on disk.

The first pass comes 15 minutes after the start rounded up to the 7-minute grid (`BaseDelayedBackgroundService.FirstRunUtc`), so 15 to 22 minutes after the start. When the job is on, the start line names that time (`History sealing is on: ...; first pass at <UTC>, then every 7 minutes`, #1805): until then nothing else about the job is logged. The service logs the passes the database core does not: a pass with nothing to do (`NothingDue`, `OutsideWorkingHours`) at Debug, and a pass refused because another step held the compaction state (`Busy`) at Info with what held it and the time of the next pass. The core names the holder in the report's `Note` (`the history shard compaction step was running (<shard>)`, `the manual database compaction`, `a history sealing pass`, `the unseal of <week>`). Such a skip leaves a 14-minute gap between two passes; it is by design (see H).

### Stages and markers (A)

A week's stage is read from files in its own directories (`SealedWeekStages.Read`), never from a separate database. Every file is small, human-readable text, written as a temporary file flushed to disk and renamed over the target (see "The marker's durability" above).

| File | Where | Meaning |
|---|---|---|
| `hsm-sealing` | chunk directory | Written by `SealedWeekWriter.Open` before the chunk database exists; deleted after the swap. Its presence (without the later markers) is what proves a marker-less directory to be an interrupted seal. |
| `hsm-rows-left` | chunk directory | The sensors left as rows on purpose: id, reason, a fingerprint of their rows (count and SHA-256 of keys and values). Written before `hsm-sealed` and rewritten by folds. Missing or unreadable only costs a retry. |
| `hsm-sealed` | chunk directory | Step 2's signal, unchanged in meaning and still the only file a read acts on: the chunks are complete and the week is read merged. Its text names the format and encoder revision, describes the seal and repeats the keep time on a line `Source rows kept until: <UTC time>`. |
| `hsm-keep-source-rows-until` | chunk directory | First line: the UTC time after which the source rows may be removed. Written just after `hsm-sealed` (until then the keep line of `hsm-sealed` is read). An operator may edit it to keep the rows longer. |
| `hsm-source-rows-removing` | chunk directory | The job may have deleted overlay rows (source-row removal or a fold): the overlay no longer holds every row, and reads merge it with the chunks. Written durably (strict sync) before the first overlay delete of the week, never removed (#1805 follow-up, "Overlay-only reads"). |
| `hsm-source-rows-removed` | chunk directory | The source rows that equal their chunk rows are gone. |
| `hsm-unsealing` | chunk directory | An unseal is in progress. Deleting it cancels the unseal (G). |
| `hsm-unsealed` | row directory | An operator unsealed the week; written only after every chunk row is in the overlay. The job never seals the week again until the file is deleted. |

| Stage | Files | The job |
|---|---|---|
| Not sealed | no chunk directory | seals it when due |
| Incomplete seal | no `hsm-sealed`; `hsm-sealing`, and none of `hsm-keep-source-rows-until`, `hsm-source-rows-removing`, `hsm-source-rows-removed`, `hsm-unsealing` | deletes the directory and seals again |
| Sealed, source rows kept until T | `hsm-sealed`, T from the keep file or the marker | removes the source rows after T |
| Source rows removed | `hsm-sealed`, `hsm-source-rows-removed` | folds |
| Being unsealed | `hsm-sealed`, `hsm-unsealing` | finishes the unseal first |
| Unsealed, leftover directory | no `hsm-sealed`; `hsm-unsealing`, and `hsm-unsealed` in the row directory | deletes the directory (lowest priority) |
| **Needs attention** | any other chunk directory without `hsm-sealed` | nothing: never deleted, sealed or touched; reported |

**The one rule that lets the job delete a chunk directory** is `SealedWeekStatus.MayDeleteChunkDirectory`: only an incomplete seal or an unsealed leftover, as defined above. `SealedWeekWriter.Open` (before a seal) and the unseal's clean-up both ask it; every other marker-less directory may hold the only copy of the week. Its truth table over a chunk directory without `hsm-sealed` (S = `hsm-sealing`, K = keep file, R = `hsm-source-rows-removed`, U = `hsm-unsealing`, D = `hsm-unsealed` in the row directory; `hsm-rows-left` does not matter):

| Markers present (no `hsm-sealed`) | Stage | May delete |
|---|---|---|
| S | Incomplete seal | yes |
| S, `hsm-rows-left` | Incomplete seal | yes |
| S, D | Incomplete seal (the planner does not seal an unsealed week) | yes |
| S, K | Needs attention | no |
| S, R | Needs attention | no |
| S, U | Needs attention | no |
| K | Needs attention | no |
| K, R | Needs attention | no |
| U | Needs attention | no |
| U, D | Unsealed leftover | yes |
| U, R, D | Unsealed leftover | yes |
| none | Needs attention | no |
| none, D | Needs attention | no |

As a rule: S without K, R and U is an incomplete seal; otherwise U together with D is an unsealed leftover; everything else needs attention. These thirteen lines are the cases of the test `WithoutHsmSealed_OnlyAnInterruptedSealOrAFinishedUnsealMayBeDeleted`, which also checks that the seal's writer deletes exactly the first kind and refuses the rest untouched. Since the #1805 follow-up `hsm-source-rows-removing` (M) counts like R: S with M, and K with M, need attention (two more cases of the test). M is written only after the seal's tail is finished, so S and M never meet in practice.

**The tail of a seal** (review U1). After the swap the seal writes the keep file and deletes `hsm-sealing`, both made durable (`WeekSealingJob.CompleteSealTail`). Until that tail is done the directory would read as an interrupted seal the moment `hsm-sealed` were lost, so: every pass finishes the tail of any sealed week that still has `hsm-sealing` or lacks the keep file (the keep time from the marker's keep line) before it plans, and so do source-row removal, fold and unseal when they start (removal and fold refuse to begin unless the tail is finished and durable at that moment: a deletion of `hsm-sealing` an earlier attempt could not sync is synced again); and every step that deletes overlay rows (source-row removal, a fold's overlay deletes) first checks on disk that `hsm-sealing` is absent and the keep file present, and refuses (the week needs attention) otherwise. So by the time the first overlay row of a week is deleted, its directory can no longer be taken for an interrupted seal: with `hsm-sealed` lost it reads as needing attention. I chose this over a separate "removal started" marker because it removes the ambiguous state itself instead of adding a fourth marker to the rule. With `hsm-sealed` present the tail states (`hsm-sealing` with or without the keep file) are a sealed week keeping its source rows, and never deletable.

`hsm-sealing` written by the writer can never be read as an unsealed leftover: that needs `hsm-unsealing`, which only an unseal writes, and the unseal finishes the seal's tail (deleting `hsm-sealing`) before it writes it. A fold runs only on a week in stage "source rows removed": the planner asks for that stage, and only such weeks get a foldable count.

Step 2's rule (marker present = sealed week) stays true at every instant: `hsm-sealed` is written only after the chunks are complete and verified, and removed (by an unseal) only after every chunk row is in the overlay and `hsm-unsealed` is written, and before any chunk is deleted.

### Weeks that need attention

The job never acts on such a week; the pass reports it (`HistorySealingPassReport.WeeksNeedingAttention`, the self-monitoring sensor `Weeks needing attention` with Error status, and an error in the log once per process per week and reason). Causes and what to do:

| Cause | What it means | What to do |
|---|---|---|
| A chunk directory without `hsm-sealed` that is neither an incomplete seal nor an unsealed leftover | The marker was lost (an operator, a partial restore, file-system damage). After source-row removal the chunks are the only copy of the week, which is now served from its (nearly empty) rows. | Put `hsm-sealed` back (any text) and restart the server; or, if the rows are known to be complete, delete the directory. |
| `hsm-keep-source-rows-until` cannot be read (missing with no keep line in `hsm-sealed`, or a first line that is not a UTC time) | The source rows are kept indefinitely. | Write a UTC time on the first line, e.g. `2026-12-01T00:00:00.0000000Z`. |
| A chunk cannot be read during source-row removal | Its overlay rows stay; the other chunks' rows went; the removal is retried daily and the week stays in its stage. | Clear the history range of the sensor named in the chunk read error (step 2 removes an unreadable chunk that lies wholly inside a delete range); or leave it, nothing is lost. |
| A chunk cannot be read during an unseal | The unseal stops before anything irreversible: `hsm-sealed` stays, `hsm-unsealing` stays and the job retries daily; while it is there, source-row removal and folds of that week wait. | Cancel the unseal (delete `hsm-unsealing`), or clear the history range of that sensor and press Unseal again. |
| A sensor's delete watermark cannot be read (#1805; reported from the opened week, `SensorValuesWeek.DeleteRefusal`) | Retention and clear history of that sensor in the week are refused, so its rows stay; reads of its chunk rows fail. | Unseal the week: the unseal restores every decodable row of the sensor's chunks, rows the watermark hid included (counted in `RowsRestoredPastLostWatermark`, warned in the log), and the deletes then go through on the rows. |

### The week handle and the live swap (B)

The shard dictionary holds a `SensorValuesWeek` per week instead of the row worker or the sealed store directly. It serves the week through its current instance and can replace it:

- **Same native databases.** The replacement is built over the open row database of the instance it replaces (LevelDB allows one handle per directory), and for a seal over the chunk database the seal's writer still has open (it is handed over, not closed and opened again). The swap therefore closes nothing a running read uses; the old instance is told it no longer owns the row database.
- **Leases.** Every read takes a lease on the instance current when it starts (a range read when its enumeration starts) and returns it when it ends; a replaced instance is disposed when its last lease ends. After a seal the replaced row worker owns nothing, so disposing it frees nothing; after an unseal the replaced sealed store closes its chunk database only then. A read that is never disposed keeps its lease forever: nothing waits for leases (see G), and the history table of the web UI disposes its page read on reload and on dispose.
- **When.** The seal swaps in the same exclusive step that writes `hsm-sealed`. From then on every delete goes to the sealed store and rewrites chunks. A read that started on the row worker finishes on it and sees the whole week: during the keep period the row database still holds every row.
- **The seal's commit** holds the week's exclusive gate only for what must be atomic with the delete-log check: the check, the rare synced drop of the chunks of sensors a delete touched, the `hsm-sealed` write (with write-through or a directory sync), and the swap (a reference assignment). The compaction of the chunk database (checkpoint `seal/compacted`), the record of sensors left as rows and the sealed store are done before the gate; the keep file is written and `hsm-sealing` deleted after it. A seal stopped during or right after the compaction is an interrupted seal like any other (the directory still has `hsm-sealing` and no `hsm-sealed`) and is started again from scratch.
- **Hold times.** Every exclusive step logs how long it held the gate (Debug; Warn from 100 ms) and the pass report keeps the longest per kind. Measured on the synthetic week (Windows, SSD): seal commit 5.8–9.7 ms, a source-row removal step 7–8.5 ms, an unseal copy step up to 10 ms, the unseal commit 16 ms, a fold commit 2.7 ms.

Why the gate's hold time matters: a sensor with `AggregateValues` on rewrites its span row on every equal value (`TreeValuesCache` saves `LastDbValue`, keyed by the first tick of the run, with `AggregatedValuesCount` and `LastReceivingTime` updated), in whatever week the run began. A long run of equal values therefore keeps writing into an old, possibly sealed, week on the ingestion path, and those writes wait while the gate is held.

The cost of the gate on the write path (one shared `ReaderWriterLockSlim` entry per write, no recursion): `SealedHistoryBench --put-bench`, 400,000 puts into a current week, three rounds: 1 thread 462–482 k/s directly on the worker against 468–470 k/s through the handle; 8 threads 349–359 k/s against 348–360 k/s. Within the noise of the runs (−2.6 % to +1.4 %); no cheaper gate is needed.

### A delete that races a seal (C)

Every write and range delete holds the week's gate shared for its whole duration; the job takes it exclusively for its commit steps. Before it reads anything, a seal opens a **delete log** on the week (under the exclusive gate, so a delete already running has finished and is reflected in what the seal reads); from then on every range delete records its range before it touches anything. The seal encodes from one LevelDB snapshot outside the gate. Its commit, under the exclusive gate, deletes from the chunk database every chunk of each sensor a recorded range touches (in one synced batch), and only then writes the marker and swaps. A delete cannot run between that check and the swap, and every delete after the swap goes to the sealed store. The dropped sensors stay rows (counted as `DeletedDuringSeal`) and are sealed later by a fold.

A write during encoding is not recorded and aborts nothing: the row is in the overlay, which wins over the chunk row on every read, and the source-row removal keeps it because it differs from its chunk row (a new key has no chunk row at all). A later fold takes it into the chunks. So a week that keeps receiving late values is sealed on the first attempt.

### A delete that races a fold (D)

A fold runs the same way: delete log first, then snapshots of the overlay and the chunks, encoding outside the gate, and a commit per sensor under the exclusive gate that is skipped (`DeletedDuringFold`) if a recorded range touches the sensor. A step-2 range delete holds the gate shared across both of its phases (chunks, then overlay rows), so no commit and no snapshot of the job lands between them: the log is opened under the exclusive gate, which waits for a running delete to finish both phases. The reader guarantee of step 2 is unchanged: a fold replaces a sensor's chunks in one synced batch with chunks that hold exactly the merged rows, and the overlay rows it folded stay until a later step deletes those that equal their new chunk rows.

### Removing the source rows (E)

When the keep time has passed (and `hsm-sealed` is still on disk), the job first makes `hsm-source-rows-removing` durable and ends the store's overlay-only reads (#1805 follow-up; a fold's overlay deletes do the same, a no-op by then), then walks the chunk database chunk by chunk (stepping over watermark keys). Each step, under the exclusive gate, re-reads the chunk, decodes it, drops its rows at or below the sensor's watermark (an overlay row equal to such a row was written after the delete and is not a source row) and, for each remaining row, reads the overlay row with the same key and deletes it only if it equals the chunk row by `RowFidelity`. A row written after the seal (a late value, an edit, a rewritten aggregated span row) differs and stays. A chunk that cannot be read keeps all its overlay rows; the week then needs attention, stays in its stage and is retried after the backoff. When every chunk has been read, `hsm-source-rows-removed` is written. Every step is idempotent, so a crash anywhere leaves a correct week and the next pass finishes the job. The deletes clear the compaction marker, and step 0's background compaction compacts the week once it has been quiet for 3 hours. The job does not compact it itself: compaction is its own one-shard-per-pass step with its own failure handling, and compacting inside the sealing pass would hold the shared maintenance state for a much longer step.

### Folding (F)

A fold is considered only for a week whose source rows are gone. The job counts the week's foldable overlay rows: rows of sensors that are not File sensors and not listed in `hsm-rows-left` with an unchanged fingerprint. The count is kept in memory and redone when the instance serving the week changes, and otherwise at most once an hour (`FoldRecountInterval`) while the week's change counter keeps moving, so a week whose aggregated span rows are rewritten all the time does not cost an overlay scan every pass. A week is folded at once when it has at least `FoldMinRows` such rows, otherwise once the **number** of its foldable rows has stayed the same for `FoldQuietPeriod` (by the job's own clock: after a restart the period starts again). A row rewritten in place does not change that number, so a week with a few continuously rewritten span rows is folded about once per quiet period (a test runs three days of 7-minute passes with three rewrites before each: 1 to 4 folds, at most one count per hour), and a trickle of new late values is folded once it stops growing.

Per sensor with foldable rows: its overlay rows merged with its live chunk rows (the overlay wins; rows at or below the watermark are dropped for good, and the watermark is deleted in the commit's batch and put back if the commit is undone) are encoded in batches of at most one chunk, decoded and compared with the merged rows field by field, and committed (old chunks deleted, new chunks put, one synced batch, under the chunk write lock and the exclusive gate); the new chunks are then read back and compared byte for byte, and if they differ the old chunks are put back. Then the overlay rows of the sensor that equal the new chunk rows are deleted by the rule of E; a row rewritten after the fold read it differs and stays (it wins on reads). A sensor without chunks (left as rows earlier because a delete raced its seal, or written to the week only after the seal) is sealed by a fold in the same way. File values stay rows.

> **The one place rows are deleted without a waiting period.** A fold removes the overlay rows it folded as soon as the new chunks verify. That includes a sensor sealed for the first time by a fold: its rows get no keep period. Every other source row waits for the keep period.

### Unseal (G)

`IDatabaseCore.UnsealHistoryWeek(name, utcNow)`, behind the Unseal button on the Configuration page (`HomeController.Unseal`, admin only, POST with an antiforgery token; the controller only calls the core). It works from whatever stage an earlier unseal reached:

1. Write `hsm-unsealing`.
2. Copy: chunk by chunk, under the exclusive gate, every live chunk row (above its sensor's watermark) whose key the overlay does not hold is written into the overlay (MemoryPack of the decoded value, the same bytes a merged read returns), synced. An overlay row always wins. Rows come back with the codec's fidelity (`ReceivingTime` to the millisecond). An unreadable chunk stops the unseal (the week needs attention): its rows exist nowhere else. An unreadable delete watermark does not (review W2 of #1805): the unseal is the way out of the refused deletes it causes, so it keeps rows rather than drops them. It treats the watermark as absent for that sensor, copies every decodable row of its chunks, logs a warning naming the sensor and saying that rows the lost watermark hid may be back, and counts those rows in the pass report (`RowsRestoredPastLostWatermark`). Restoring a few rows retention had deleted is harmless; losing live ones would not be.
3. Under the exclusive gate: write `hsm-unsealed` in the row directory, delete `hsm-sealed`, swap the week to a row worker over the same row database. The unseal is now done.
4. Delete the chunk directory, if no read still uses the replaced sealed store. This is checked once, never waited for (not on the HTTP request, not under the maintenance state): if a read holds it, the week is an unsealed leftover and a later pass deletes the directory (the lowest-priority action, retried an hour later each time, never holding anything else back).

A crash before 3 leaves a sealed week whose overlay holds some or all chunk rows (reads unchanged) and `hsm-unsealing`, so the next pass finishes the unseal before anything else (`FinishUnseal`), or the operator presses Unseal again. A crash after 3 leaves an unsealed leftover. A sealed marker over a partly deleted chunk directory cannot happen: chunks are deleted only in 4, and only by the deletion rule.

**Cancelling an unseal** is deleting `hsm-unsealing` (with the server stopped, or at any time: the job reads the stage at every pass). While `hsm-sealed` is there the week returns to its previous stage. The rows the copy already wrote equal their chunk rows: in the keep period they are the source rows anyway; after source-row removal they count as foldable rows, and the next fold re-encodes those sensors and removes them (tested).

### Coexistence and shutdown (H)

A pass and an unseal enter `DatabaseCore`'s compaction state (`TryEnterCompact`), the same one the manual Compact and the background compaction step use: none of the five ever runs at the same time as another, on any week, and the Compact button answers "already running" during a pass. A pass that finds the state taken returns `Busy` at once, with the holder in its `Note`; it is not published to the self-monitoring (the attention lists belong to the step that holds the state), so `Last run` keeps the last pass that ran. The job's 7-minute grid meets the compaction step's 10-minute grid once every 70 minutes; a pass is skipped only when a compaction is actually running at that moment. `Dispose` refuses new passes and waits for a running one. A pass is also interruptible: it checks for shutdown between sensors (of a seal, a fold and the count of foldable rows), chunk steps and copy steps, and stops there (outcome `Interrupted`, nothing to undo; the next pass resumes). Nothing in the job waits or sleeps. The longest uninterruptible piece is the encoding and verification of one sensor (a whole week, its densest sensor included, seals in 1.2 to 2.2 s, see Measured).

### Memory and pacing (I)

A week is sealed sensor by sensor from one snapshot: a first pass over the sensor's keys counts its rows, so batches have the codec's own balanced sizes; then one batch of at most 4,096 rows is decoded at a time, and only the encoded chunks of the current sensor are held (about 7.5 bytes per row). Verification reads the chunks back and streams the source rows again. A fold holds the old and new chunks of one sensor. The planner works in UTC working hours (`WorkingHoursStartUtc`..`WorkingHoursEndUtc`, the whole day when equal, may wrap past midnight); an operator's unseal ignores them. One action per pass, every 7 minutes.

The order of a pass: finish an interrupted unseal; remove the source rows of a week whose keep time passed; seal a closed week (its end, the next Monday 00:00 UTC, plus `SealAfter` is not in the future); fold; delete an unsealed leftover directory. Within each kind the oldest week first. A week that needs attention gets no action.

### Failure handling (J)

- A week whose action fails (I/O error, an unreadable chunk during source-row removal or an unseal, a fold whose chunks read back wrong, a store that cannot open, a marker that cannot be made durable) is logged with its name, held back for a day (`HistorySealingPlanner.FailureBackoff`, in memory like the compaction's), and the next weeks go on. The causes an operator has to act on are also reported as weeks needing attention.
- A sensor the codec refuses (`Refused:<reason>`), whose written chunks do not decode back to its rows (`VerificationFailed`), with a row that cannot be deserialized (`UnreadableSourceRow`), with an unexpected key length (`UnexpectedKey`) or an unreadable chunk during a fold (`UnreadableChunk`) stays rows, is counted by reason in the pass and listed in `hsm-rows-left` with a fingerprint of its rows. It is tried again only when its rows change. A sensor whose work was dropped because of a delete (`DeletedDuringSeal`, `DeletedDuringFold`) is counted but not listed, so the next fold takes it.
- File sensors are skipped by the type of their first row and not counted.
- An unreadable source row leaves its sensor as rows instead of failing the whole week (see Deviations).

### Configuration

Section `HistorySealing` of `Config/appsettings.json`, read once at startup (`HistorySealingConfig`, validated at startup with an error that names the key):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | ADR-0020, decision 10: off until the rollout step decides otherwise. |
| `SealAfterDays` | `7` | Days after a week closed before it is sealed (1..3650). Our choice: late values are harmless after a seal, this only keeps the job off weeks collectors are still catching up on. |
| `KeepSourceRowsWeeks` | `4` | Weeks the source rows stay after a verified seal (1..520). ADR-0020 leaves the number to configuration; the minimum of one week carries the durability rule of step 2. The keep time is fixed into each week when it is sealed; a later change applies to weeks sealed afterwards. |
| `WorkingHoursStartUtc`, `WorkingHoursEndUtc` | `"00:00"`, `"00:00"` | Daily UTC window, `HH:mm`; equal values mean all day. |
| `FoldMinRows` | `10000` | A sealed week with this many rows to fold is folded at the next pass. |
| `FoldQuietHours` | `24` | Fewer rows are folded once their number has not changed for this long. |

### Self-monitoring

Under `Database/Sealed history` in the "HSM Server Monitoring" product (`HistorySealingSensors`), posted from the 5-minute self-monitoring sample; each pass once:

| Sensor | Value |
|---|---|
| `Last run` (string) | action and outcome; the comment is the full pass report; Error status on a failed pass. Posted for every pass while the job is on, including "nothing due" (`None NothingDue`) and "outside working hours", so a new value every 7 minutes shows the job is alive; a pass skipped as `Busy` posts nothing. |
| `Sealed weeks` (int) | complete sealed weeks on disk after the pass |
| `Weeks needing attention` (int) | weeks the job will not touch until an operator acts; the comment names each week and the reason (see "Weeks that need attention"); Error status above 0. Posted with every pass. |
| `Rows sealed` (int) | rows written into chunks by the last seal or fold |
| `Bytes before` / `Bytes after` (MB) | key and value bytes of those rows / key and chunk bytes written for them |
| `Sensors left as rows` (int) | with the reasons in the comment, for the last seal or fold |
| `Verification failures` (int) | sensors whose chunks did not decode back to their rows in the last seal or fold; Error status above 0 |
| `Chunk read failures`, `Chunk rewrite failures`, `Chunk directory sync failures` (int) | `SealedHistoryDiagnostics` counters since the process started, posted when they change once there is a pass or one is above 0; Error status above 0 |

### Measured (2026-11-19, `SealedHistoryBench --job-synthetic` / `--job-shard`, Release, one machine)

| | Synthetic: 3 weeks, 300 sensors, 683,300 sealed rows per week | Production shard copy: 1 week, 492 sensors, 531,475 sealed rows |
|---|---|---|
| Seal one week | 1.2–1.9 s; managed heap +24 MB, working set +40–60 MB | 2.2 s; heap +25 MB, working set +78 MB |
| Bytes before → after (logical, per week) | 73.1 MB → 4.8 MB | 68.3 MB → 3.9 MB |
| History on disk: rows compacted → sealed with source rows kept → removed and compacted | 75.2 → 89.1 → 18.5 MB (3 weeks; File rows stay) | 21.8 → 25.6 → 3.8 MB |
| Remove the source rows of one week | 0.9–1.1 s; heap +18 MB | 0.7 s; heap +20 MB |
| Fold about 1,000 late values and edits over 20 sensors | 0.10 s (35,146 rows re-encoded) | 0.07 s (12,467 rows re-encoded) |
| Unseal one week | 1.4–1.6 s; heap +18 MB | 1.5 s; heap +18 MB |

Before the compaction that follows source-row removal, the week is larger than before (deletes are tombstones): 107 MB for the three synthetic weeks. The production shard copy was measured before the review changes of the seal's commit; the synthetic run was repeated after them (the hold times in B are from it).

### Deviations and readings of ADR-0020

- **"Reopened as sealed".** Step 2 asked that source rows never be deleted on the strength of a marker written in the same run without the week having been reopened as sealed. The live swap opens the sealed store in the same step as the marker (it cannot reopen the chunk database later without closing it under running reads). What protects the source rows instead: the marker is made durable before the swap (write-through on Windows, a directory sync on Linux and macOS that fails the seal if it fails), the keep period of at least one week, and the check that `hsm-sealed` is on disk when the removal starts.
- **Unreadable source row.** It leaves its sensor as rows (counted, listed) instead of failing the whole week: the other sensors of the week are sealed.
- **Folded rows get no keep period** (see F).
- **An unseal is sticky** (`hsm-unsealed`): otherwise the job would seal the week again at its next pass.
- **Keep time fixed per week**: changing `KeepSourceRowsWeeks` does not move the keep time of weeks already sealed (edit their file instead).
- **Unsealed leftover is stricter than "`hsm-unsealed` and no `hsm-sealed`"**: it also requires `hsm-unsealing` in the chunk directory, which every unseal writes first.

## UI / Operator Visibility

- Configuration page, "Values Database" row: the **Unseal** button (admins) turns the selected week back into rows; the result or the error is shown next to the buttons.
- Operator actions on files (server stopped, or at any time for the job's markers): put back a lost `hsm-sealed`; fix `hsm-keep-source-rows-until`; delete `hsm-unsealing` to cancel an unseal; delete `hsm-unsealed` to let a week be sealed again. See "Weeks that need attention".
- Self-monitoring sensors under `Database/Sealed history` (above).
- The log: the start line with the time of the first pass; every pass that did something logs its report at Info (Error when it failed), every sensor left as rows a Warn line with its reason; a skipped (`Busy`) pass an Info line naming what held the database; a pass with nothing to do a Debug line.
- Operator page (wiki source, not published yet): `wiki-draft/Sealed-Sensor-History.md`.
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

The sealing job (#1804), in the same project (`--filter FullyQualifiedName~HistorySealing`):

- `HistorySealingPlannerTests`: the seal delay to the tick, incomplete seals, unsealed and sealed weeks, the keep time (and a missing one), the fold threshold and quiet period, the order of actions and weeks, the failure backoff of every action, working hours with and without a wrap past midnight.
- `HistorySealingJobTests`, through `DatabaseCore` with an injected clock, against a plain database that receives the same writes and deletes, every read of every week and the point reads of `DatabaseCore` compared with step 2's `FidelityComparer`: the whole life (seal both weeks oldest first with a refused sensor and a File sensor, keep period, source-row removal, late values and an edit, a fold after the quiet period, convergence, unseal, no reseal, a restart); the switch off (nothing created on disk); working hours; a verification failure (test seam `SealingChunksBeforeWrite`) and a codec refusal left as rows, counted, not retried until the rows change, then folded.
- #1805: `ASeal_LeavesItsChunkDirectoryCompact` (after each seal the chunk directory's write-ahead log holds at most 4 KiB and its LevelDB files at most twice the chunk bytes plus 64 KiB; fails without the compaction, when the log holds every chunk; a pass with nothing to do is still published), `APassDuringTheCompactionStep_IsBusy_AndNamesIt` (a pass during the compaction step is `Busy`, names the step and is not published); in `HSMServer.Core.Tests`, `TheServiceLog_SaysWhenTheFirstPassRuns_AndWhyAPassWasSkipped` (the first-run time on the grid, the start line, the Info and Debug pass lines).
- #1805, delete watermarks: `PrefixDeletes_MarkAWatermark_AndGiveTheSameReads` (with and without the keep period: cutoffs below the sensor's start, inside a chunk, the same twice, going back, exactly at a chunk's first and last row, inside the last chunk, past the end; late writes below and above the watermark, a rewrite, a middle-range delete in the partly deleted chunk, sensor removal, reopens; every read compared, each prefix delete checked to decode nothing and write at most the watermark) and prefix deletes in the seeded `RandomOperations_GiveTheSameReads`; `HourlyRetention_WritesAFewBytesPerSensor_AndTheChunkDirectoryDoesNotGrow` (166 hourly passes over 20 week-long chunks: no decode, at most 29 bytes per sensor and pass, the directory within twice its sealed size); `SealedWeekStoreTests` region "Prefix deletes": readers started before the delete, between its chunk batch and its overlay delete, and between the two iterators of a read; an interruption after the batch (reads correct, the next delete finishes it); an unreadable watermark (bad checksum, unknown version, short value) fails only its sensor, is counted, refuses a delete that would raise it, and sensor removal clears it; a crash image right after the batch (Linux only); `RetentionThroughASealedWeek_MarksWatermarks_AndEveryStageOfTheJobRespectsThem` (retention during and after the keep period, a deleted row written again with its exact bytes and kept by source-row removal, a fold that drops the watermark, a restart, an unseal) and `PrefixDeleteDuringFold_DoesNotComeBack`. Mutation-checked: the cursor ignoring the watermark fails the differential, hourly, randomized and job tests; the unseal copy ignoring it fails the retention job test and a randomized job test; source-row removal ignoring it fails the retention job test; the fold ignoring it fails the retention and fold-race tests; a watermark lowered by a cutoff that goes back fails the prefix test.
- #1805 follow-up, overlay-only reads (`KeepPeriodReadTests.cs`): the differential theories (`ScriptedOperations_...`, `PrefixDeletes_MarkAWatermark_...`, `RandomOperations_...`) run in three modes: source rows removed; kept with removal begun (`hsm-source-rows-removing`, merged reads); kept as a seal leaves them (overlay-only: no read creates a chunk iterator, and at the end the same week read merged gives the same answers, so the chunks kept up with every delete). `OverlayOnlyReads_TouchNothingOfTheChunks` (every chunk and watermark damaged before the week opens: every read answers like the plain week, no chunk iterator, decode, watermark read or failure; merged, the same read throws); `ChangesDuringTheKeepPeriod_KeepOverlayOnlyAndMergedReadsEqual`; `TheFilesSayWhetherTheOverlayHoldsEveryRow` (thirteen marker states, both keep sources, the one-day margin); through the job, `AReadAcrossTheFirstRemovalSteps_SeesEveryRow`, `AReadThatDecidesAfterTheRemovalBegan_ReadsMerged`, `ARestartAndAnUnsealInEachStateOfTheSwitch_KeepEveryRead` and `ARestartInEachStateOfTheSwitch_ThenTheRemovalCompletes` (keep period, the announcement made durable, the first removal step, removed), `AnAnnouncementThatCannotBeMadeDurable_DeletesNothing`, `ARemovalBegunByAnOlderBuild_WithoutTheMarker_ReadsMergedOnceTheKeepTimeHasPassed` (review R4c); `MemTableFlushTests` (review R1, above); the crash image at `remove/announced` asserts that the announced week reads merged with its keep time still ahead (R4b); the job tests give the store their own clock (`DatabaseCore.Clock`), so their read modes do not depend on today (R4a); the deletion rule's truth table gained the two cases with `hsm-source-rows-removing`. Mutation-checked: deciding before creating the overlay iterator fails the decide-after test (0 of 9,000 rows); no announcement fails the across-removal, decide-after, restart and durability tests (an older week read as 0 of 300 rows); stopping overlay-only reads without the durable marker fails the restart tests (9,785 rows read as 6,785 after a crash in the first removal step); `OverlayHoldsEveryRow` without the keep-time condition fails four cases of its theory; range deletes that skip the chunks while reads are overlay-only fail the three overlay-only differential theories when they read merged at the end (13,900 rows read as 18,525).
- A crash after each stage (test seam `SealingCheckpoint` throws, the test restarts the database): seal after opening, mid-way through the sensors, after encoding, after the chunk database's compaction, after the marker, after the swap; source-row removal mid-way and after its marker; fold after encoding, after the commit, mid-way through the overlay deletes, at the end; unseal after its marker, mid-way through the copy, after `hsm-unsealed`, after the swap, at the end. Each restarts correct and completes.
- The races, each through a checkpoint: a range delete during a seal's encoding (C) and during a fold (D) do not come back (mutation-checked: without the delete-log check both fail on the very first comparison); a write during encoding is kept and the sensor is still sealed; a read across the seal's swap finishes on the row worker with every row; a read across the unseal's swap keeps the chunk database open and the directory is deleted only after it ends.
- Three randomized runs (logged seeds) of late writes, partial range deletes and retention deletes between 30 passes, with an unseal and restarts.
- In `HSMServer.Core.Tests` (`HistorySealingSensorsTests`): the self-monitoring values of a pass, a failed pass, the counters, weeks needing attention, the service not calling the database when off, the configuration defaults and validation; `HistoryTableViewModelDisposalTests`: the history table disposes its page read on reload and on dispose.
- `HistorySealingSafetyTests` (part of the same class): the truth table of the deletion rule; a lost `hsm-sealed` in three stages (chunks untouched for ten days of passes, the week reported, the marker put back restores it); a missing or unparsable keep time (kept, reported) and the fallback to the marker's keep line; a cancelled unseal whose copied rows are folded away; an unreadable chunk under source-row removal (its rows stay), under a fold (the old chunks stay, other sensors fold) and under an unseal (stops before anything irreversible; clearing the range lets it finish); a range delete between unseal copy steps; a span row rewritten after a seal (kept by the removal), rewritten during a fold (stays and wins), and rewritten continuously for three days (no thrash, at most one count per hour); a directory sync failing after `hsm-sealed` (the seal fails, the week stays rows, the retry seals it); an abandoned read across an unseal (`HistorySealingJobTests`: the unseal returns at once, the older week's removal still runs, the leftover goes once the read is disposed).
- Crash images (`ACrashImageAtAnyStage_...`, review T4e): at eleven checkpoints of seal (`seal/compacted` added by #1805), removal (`remove/announced` added by the #1805 follow-up), fold and unseal the database folder is copied file by file while every database is open and the copy is opened as a new root. **They run on Linux only**: on Windows the LevelDB build opens every open database's `NNNNNN.log` and `MANIFEST-*` without read sharing, and copying them fails with `ERROR_SHARING_VIOLATION` (0x80070020), also when opened with `FileShare.ReadWrite | FileShare.Delete`; the theory is skipped there with that reason. On Linux (x86_64, 2026-11-20) all ten pass, and the strict directory `fsync` through libc works (LevelDB tests after the #1805 delete watermark: 219 passed, 0 skipped; on Windows 208 passed, 2 skipped; after the overlay-only reads of the #1805 follow-up: 257 passed on Linux, 245 passed and 2 skipped on Windows). To run them from Windows, in a container: export the sources with `git archive HEAD | tar -x -C <dir>`, run `docker run --rm -v <dir>:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test src/tests/HSMDatabase.LevelDB.Tests -c Release`, and stub `npm` in the container (for example an executable `/usr/local/bin/npm` that exits 0) because the test project builds the server project, whose build runs an npm step.
- U1: the tail states with `hsm-sealed` (a sealed week, never deletable, the tail finished by `CompleteSealTail`, and needing attention once `hsm-sealed` is lost); a crash between the swap and the tail (the next pass finishes it before any removal); a tail that cannot be finished (every directory sync of the week's chunk directory fails, through the static seam `DurableFile.FailSync`, reset in a `finally`; the same on every platform): every overlay delete is refused across passes, the week is reported, the directory ends with the keep file written and `hsm-sealing` deleted but not durably, and the removal goes through once the syncs work; a lost `hsm-sealed` during a half-done source-row removal (needs attention, nothing deleted, the marker put back restores the week). Mutation-checked: without the check at the start of a removal the unfinishable-tail test fails (the per-step guard alone does not catch this case, since `hsm-sealing` is gone from disk by then; it stays as a second line); with the tail completion disabled the interrupted-tail and tail-state tests fail.

The new protections were mutation-checked (each removed, the named test run, restored): the stage rule falling back to "any marker-less directory is an incomplete seal" fails the three lost-marker cases; the writer deleting any directory fails ten truth-table cases; a non-strict sync of `hsm-sealed` fails the sync test; recounting the overlay on every pass, or restarting the quiet period on every change, fails the continuous-rewrite test; deleting the leftover without checking for reads fails the abandoned-read test; not reporting a stage's attention fails the lost-marker and keep-time tests.

## Notes

- Departures from the measured prototype: residual orders are the plain first and second difference for every integer column (the prototype hard-wired delta-of-delta for time); the decimal mantissas use a full integer column (byte or bit level), not always the bit level; the double column picks the smallest of XOR bytes, decimal and Gorilla; `CloseTime` is stored relative to `OpenTime`; the key and `Time` are separate columns, so a `Time` that differs from the key round-trips; strings are a dictionary per column with indices in an integer column, or templates with numeric slots (mode 2, added 2026-11-20 from two measurement rounds on a production week, `docs/initiatives/sensor-history-storage.md`); powers of ten are literals, not `Math.Pow`.
- Encoding costs about 0.8 µs per row and decoding about 0.17 µs per row on a production deployment's week (see Measured).

## Known Issues / Limitations

- A sensor keeps at most one partly deleted chunk per sealed week (rows below its delete watermark) until a later prefix delete passes the chunk's last row, a fold rewrites the sensor, or a range delete rewrites the chunk: those bytes stay on disk meanwhile.
- Delete watermarks are a new key shape in the chunk database. A build from before #1805 (the first rollout's) reads one as a corrupt 16-byte chunk key: reads that cross it fail, and so do source-row removal and unseal on that week. It matters only for a downgrade to such a build; unseal first. A chunk database without watermarks (sealed by such a build) is read by the current one unchanged.
- Rows with leftover bytes in `Nullable` padding decode with zero padding; their MemoryPack bytes differ from the source blob though every value is equal (see Invariants).
- zstd output is stable for one `ZstdSharp.Port` version. An upgrade may change the stored bytes of new chunks without changing the format; the golden encode test then fails only on its last assertion, and the upgrade needs a new encoder revision.
