# Initiative: Sensor history storage format

> Owner: storage | Last reviewed: 2026-11-19 | Canonical: yes
>
> Status: measurement finished, direction proposed, no decision recorded yet. One breakdown is still missing: rounding by kind of sensor (CPU, disk, network), see "Not measured".

## Problem

Sensor history is the largest part of an HSM database, and it is stored as one MemoryPack blob per value in LevelDB. The owner asked whether a column-oriented or time-series store (DuckDB first, then VictoriaMetrics, Prometheus-style encoding, ClickHouse and other external servers) would hold the same data in less space, and what such a move would cost on the operations the server actually performs.

This document records what was measured, what follows from it, and the direction proposed.

## Goals

- Know, from measurement on real data, how many bytes per stored value each candidate needs.
- Know what each candidate costs on HSM's access patterns: single-value writes, rewrite of an existing key, reads of one sensor, per-sensor range deletes.
- Choose a direction that reduces the size of sensor history without losing data the server relies on.

## Non-Goals

- Moving configuration data (products, sensors, policies, users, keys, dashboards, change sets, snapshots). It is small mutable key-value data and stays where it is.
- Changing the wire contract or the collectors.
- Storing File sensor values more compactly. They are blobs and are out of scope.

## Current Behavior

- Key: `sensorId (16 bytes) | ticks (8 bytes, big-endian)` — `src/database/HSMDatabase.AccessManager/DbKey.cs`.
- One LevelDB database per week (`SensorValuesV2_<from>_<to>`), Snappy compression, 200 KB blocks — `src/database/HSMDatabase.LevelDB/Database.cs`, `src/database/HSMDatabase/DatabaseWorkCore/ValuesDatabaseDictionaryT.cs`.
- Value: one MemoryPack-serialized `BaseValue` per row, 11 value types — `src/server/HSMCommon/SensorValues/`.
- Write: a synchronous `Put` per value (`DatabaseCore.AddSensorValue`). The same key is rewritten when a value is aggregated into the previous one, when a bar is updated, and when two values share a tick (last write wins).
- Read: almost always one sensor — a forward or backward range scan of up to 50,000 values, the latest and the first value at start-up, a point lookup. The database does no aggregation.
- Delete: retention (`KeepHistory`) and "clear history" are range deletes for one sensor; a sensor-move merge copies one sensor's rows under another id.
- `Compact()` exists but runs only when an administrator presses the button in the UI (`HomeController.Compact`).

## Method

**Data.** One closed weekly shard (2026-11-09 to 2026-11-16) copied from a consistent snapshot of a small production deployment: 531,480 rows, 492 sensors. A second dataset multiplies the sensors by 20 (9,840 sensors, 10,629,600 rows) by cloning each sensor with a time shift; cloned values are identical, which favours dictionary compression in every layout.

**Environment.** Intel Core Ultra 7 270K Plus, 31.5 GiB RAM, NVMe SSD, Windows 11; .NET 8.0.28; DuckDB 1.5.6; SoftFX.LevelDB.Standard 1.2.1; MemoryPack 1.21.4. In Docker: VictoriaMetrics 1.156.0, VictoriaLogs 1.54.0, Prometheus 3.16.0, ClickHouse 26.10.4, TimescaleDB 2.32.0 on PostgreSQL 17.11, QuestDB 10.2.0.

**Units.** "B/row" is bytes on disk per original HSM row. Timings are median milliseconds over 25 runs after warm-up, OS cache warm, and include rebuilding full values.

**Queries.** Three sensors were used: the densest (10,061 rows in the week), a median one (13 rows) and a sparse one (1 row).

| Id | Query |
|---|---|
| Q1 | The last 100 values of one sensor before a point in time, newest first |
| Q2 | The whole week of one sensor |
| Q3 | The latest value of every sensor (server start-up) |
| Q4 | Point lookup of one `(sensor, tick)` |
| Q5 | One sensor downsampled to one-hour buckets |

**Losslessness.** Every variant described as lossless was read back row by row and compared with the originals.

**What is not in the repository.** The benchmark harness and the raw result file are a throwaway prototype and are not part of this change.

## The data

| Type | Rows | Sensors | Average value bytes (MemoryPack) |
|---|---|---|---|
| Rate | 326,943 | 35 | 72.0 |
| DoubleBar | 127,975 | 87 | 196.5 |
| Double | 49,047 | 325 | 74.7 |
| Integer | 12,968 | 11 | 68.4 |
| IntegerBar | 10,074 | 5 | 174.8 |
| TimeSpan, Boolean, Version, Enum, String | 4,468 | 28 | 68–188 |
| File | 5 | 1 | 50,514 |

- Uncompressed, a row is about 105 bytes of value plus a 24-byte key.
- Rows per sensor: minimum 1, median 13, maximum 10,061.
- A value equals the previous value of the same sensor in 46% of consecutive pairs (Rate 55%, Double 39%, DoubleBar 27%, IntegerBar 20%, Integer 63%).
- The median time step is 60 s, but it is not regular: the delta-of-delta of the timestamp is exactly zero in 7.3% of rows and within 1 ms in 50.6%. 69% of rows have a non-zero sub-millisecond part.
- `IsTimeout` is never set; `Status` is not Ok in 711 rows; 130 rows are aggregated runs.

## Results

### Summary

| Variant | B/row | Q2, dense sensor (ms) | Single-value write (rows/s) | Rewrite of a key (rows/s) |
|---|---|---|---|---|
| **Embedded** | | | | |
| LevelDB as copied (today) | 122.4 | 8.1 | 405,188 | 198,033 |
| LevelDB after `Compact()` | 43.1 | — | — | — |
| DuckDB, arrival order | 40.0 | 18.3 | 372 | 349 |
| DuckDB, sorted by `(sensor, time)` | 32.1 | 5.5 | — | — |
| DuckDB, arrival order + primary key | 85.4 | 11.5 | — | 174 |
| Parquet + zstd, sorted | 13.7 | 13.2 | immutable | immutable |
| Parquet + decimal split + ms receiving time | 10.4 | 31.7 | immutable | immutable |
| Parquet, standard encodings only, ms receiving time | 11.1 | — | immutable | immutable |
| Same, doubles rounded to 3 significant digits (what-if) | 8.5 | 21.6 | immutable | immutable |
| Chunked LevelDB, byte-aligned codec, hour chunks | 14.4 | 1.00 | — | — |
| Chunked LevelDB, bit-level codec + zstd, week chunks | 9.5 | 0.92 | — | — |
| Same, receiving time at 1 ms | 7.5 | 0.75 | — | — |
| Same, doubles rounded to 3 significant digits (what-if) | 5.5 | 0.97 | — | — |
| **External servers** | | | | |
| VictoriaMetrics, value and status only | 5.1 | 13.1 | 445 | not last-write-wins |
| VictoriaMetrics + VictoriaLogs, every field | 19.6 | 60.9 | — | not last-write-wins |
| ClickHouse, tuned codecs | 14.9 | 32.2 | 16 sync / 811 async | 16 sync / 700 async |
| ClickHouse + decimal split + ms receiving time | 12.6 | — | — | — |
| TimescaleDB, uncompressed / compressed | 191.2 / 18.2 | 18.9 / 13.3 | 258 | 253 / 225 |
| TimescaleDB compressed + decimal split + ms receiving time | 11.3 | — | — | — |
| QuestDB, closed native partitions (21.7 after conversion to Parquet, not timed) | 230.5 | 18.8 | 924 | 924 |

Rows marked "ms receiving time" lose sub-millisecond precision of `ReceivingTime` only; everything else in them is exact. The chunked stores hold sealed data, so single-value writes do not apply to them: current data stays in plain LevelDB rows.

### LevelDB compaction

A closed weekly shard is 65.1 MB as it sits on disk and 22.9 MB after `Compact()` (205 ms): 2.8 times smaller with no change of format. Superseded versions of rewritten rows and deleted rows stay on disk until a compaction merges the files, and a week that is no longer written to is not compacted further on its own. The split between removed garbage and better compression of sorted data was not measured.

Deletes need compaction to free space: deleting three days of every sensor grew the directory from 22.9 to 24.4 MB, and compaction then brought it to 13.1 MB.

### DuckDB

| Test | LevelDB | DuckDB |
|---|---|---|
| Write one value at a time | 405,188 rows/s | 372 rows/s (single-row `INSERT`) |
| Write in batches of 10,000 (Appender) | — | 598,654 rows/s |
| Rewrite 10,000 existing keys | 198,033 rows/s | 174 rows/s (`INSERT OR REPLACE`, primary key); 349 rows/s (`UPDATE`, no index) |
| Q1, dense sensor | 0.12 ms | 2.3–10.1 ms |
| Q4, dense sensor | 0.002 ms | 0.9–1.6 ms |
| Q3, one sensor at a time (491 sensors) | 13.7 ms | 774–1,064 ms |
| Delete three days of all sensors, then reclaim | 22.9 → 13.1 MB | 17.1 → 24.1 MB (sorted layout) |

- The native file is barely smaller than compacted LevelDB (32–40 against 43 B/row).
- Without a sort by `(sensor, time)` a one-sensor read scans every row group: at ×20, Q2 takes 53.6 ms in arrival order against 5.7 ms sorted, and 365 ms against 8 ms under a 128 MB memory limit.
- A primary key doubles the file and does not help range reads.
- `DELETE` followed by `CHECKPOINT` never shrank the file.
- `memory_limit` covers the buffer manager only: with a 128 MB limit the process peaked at 213 MiB.

### Parquet

Parquet is the best of the standard formats by size (13.3–13.7 B/row), but it is a file format, not a database: files are immutable, so it can only hold closed periods. Read through DuckDB, every query has a floor of about 9–13 ms; the start-up loop over all sensors takes 4.6 s against 13.7 ms for LevelDB. Smaller row groups made reads slower. The two timestamp columns are 60% of the file.

With the same transforms as the chunk codec below (doubles split into decimal mantissa and exponent, receiving time as a millisecond delta, bar open and close as differences) the file is 10.4 B/row. Q2 then takes 31.7 ms: 17.5 ms to read the stored columns and the rest to rebuild bar open and close with window functions.

**Closed weeks in Parquet with standard encodings only.** The owner's alternative to a codec of our own: keep the current week in LevelDB and write each closed week as one Parquet file, with no transform that needs our own code to read back. The files were written by DuckDB with `PARQUET_VERSION V2` and zstd, sorted by `(sensor_no, time_ticks)`.

| File | B/row |
|---|---|
| Lossless | 13.3 |
| Receiving time as a millisecond delta, the rest exact | 11.1 |
| Also doubles as integers of `round(v × 1000)` (3 decimal places) | 9.3 |
| Also doubles rounded to 3 significant digits, kept as doubles | 8.5 |

- The writer chose dictionary encoding, not delta encoding, for the scaled integers, so storing doubles as integers did not bring the delta coder into play.
- Readers, week of the dense sensor: DuckDB with its metadata cache 15.6–21.6 ms; Parquet.Net (pure managed) 89–96 ms with the default row groups of 122,880 rows and 19.8 ms with row groups of 5,000 rows. Smaller row groups help the managed reader and hurt DuckDB.
- Start-up read of the latest value of every sensor: 11 ms as one pass with DuckDB, 2.1–34 s as a per-sensor loop depending on reader and row-group size.
- Sealing a week (LevelDB rows to a sorted file): 1.2 s and 530 MiB of memory; at ×20, 13.8 s and 7.3 GiB with DuckDB unconstrained.
- Retention is a rewrite of the file: 0.12–0.15 s at this size, 1.2–1.9 s and 3–4 GiB at ×20.
- Late values: a read that merges the file with a LevelDB overlay holding 1% of the sensor's rows takes 20.8 ms against 17.3 ms for the file alone; the overlay row wins on an equal key.

### VictoriaMetrics and VictoriaLogs

An HSM value is a record of many fields; a VictoriaMetrics sample is one number on one series. Two mappings were measured: value and status only (E1), and every numeric field as its own series with strings and comments in VictoriaLogs (E2).

| Store | B per sample | B per HSM row |
|---|---|---|
| E1 | 1.84 | 5.1 |
| E2 | 2.04 | 16.9 |
| VictoriaLogs (strings, comments) | 4.15 per entry | 2.7 |

Observed behaviour:

- Timestamps are kept in milliseconds: 63 rows of the shard collide and are lost.
- 148,837 rows come back with floating-point values rounded to 12 significant digits.
- A second sample with the same series and timestamp is stored as a duplicate; with deduplication enabled the largest value is kept, not the last.
- A time range of one series cannot be deleted (`delete_series` with a range answers HTTP 400); only a whole series can.
- Per-series retention exists only in the Enterprise edition (documentation claim, not tested).
- A written sample became visible to a read after about 12 s.
- Rebuilding a full value means reading several series and joining them by timestamp: Q2 takes 60.9 ms.
- File values have no place in this design.

### Prometheus TSDB

The same values were loaded into Prometheus blocks with `promtool`. Bits per sample, chunk data only (the index adds 6–61 bits per sample depending on how sparse the series are):

| Series | Real data | Same values on an exact time grid | Real timestamps, constant value |
|---|---|---|---|
| Rate | 42.2 | 30.4 | 15.4 |
| Double | 58.1 | 45.3 | 32.9 |
| DoubleBar mean | 49.0 | 37.8 | 21.2 |
| DoubleBar count | 21.8 | 10.7 | 21.2 |
| Integer | 14.5 | 9.6 | 11.8 |
| IntegerBar mean | 24.5 | 12.1 | 23.3 |

- Two control series on an exact 15 s grid: a value that never changes costs 3.7 bits per sample; a counter with 0.01 resolution that grows by a small varying amount costs 48 bits per sample. "About one bit per sample" is the case of an unchanged value on a regular grid.
- 63 samples that share a millisecond with another sample of the same series were dropped without a message.
- Sparse series are penalised by the two-hour block boundaries: a Double series averages 11 samples per chunk, so the fixed cost of a chunk dominates. Larger blocks were not tested.

### Chunked LevelDB with a time-series codec

The store keeps LevelDB and replaces one row per value with one chunk per sensor per period, encoded column by column. A read of one sensor stays a key seek plus a decode, which is why Q2 is 8 times faster than today.

**Value codecs, bits per value (week chunks):**

| Column | Byte-aligned XOR | Gorilla XOR (Prometheus) | Decimal-scaled integers + delta |
|---|---|---|---|
| Rate value | 28.9 | 28.9 | 28.9 (23.1 with zstd) |
| Double value | 35.2 | 31.4 | 22.1 |
| DoubleBar mean | 34.9 | 31.3 | 6.5 |
| DoubleBar min | 23.7 | 19.0 | 3.9 |
| DoubleBar std_dev | 34.2 | 30.3 | 4.4 |
| Integer value | 7.1 | — | 3.6 |
| Bar open / close | 40.0 | — | 1.5 |

- When a double changes, 47–53 of its 52 mantissa bits change, so XOR with the previous value saves little.
- Where the stored values are rounded decimals (bars, Double, EMA fields), scaling to an integer and coding the difference takes single-digit bits. The integer coder picks raw values, the first difference or the second difference (delta-of-delta) per chunk: the second difference wins for bar open times and EMA, the first for bar counts and means.
- Rate values are computed ratios such as `0.29124565993142887`. They do not scale to integers, and Rate is 61% of all rows.
- Predicting a field from another field of the same row adds little: a bar's `min` equals the previous `min` in 65% of rows and the `mean` of the same row in 41%. The useful cross-field relations are structural: a bar's open equals the previous close in 99.4% of rows, and the bar length is constant.
- zstd over already-encoded hourly chunks makes the store larger; over week chunks it helps.

**Timestamps.** Gorilla's bucketed delta-of-delta is built for second or millisecond steps: at 100 ns precision it costs 59.7 bits per Rate row against 21.1 for a variable-length delta-of-delta, and 8.6 bits at millisecond precision.

**Where the bits go, best lossless combination, bits per row:**

| Type | Total | Time | Receiving time | Values | Other |
|---|---|---|---|---|---|
| Rate | 68.3 | 20.0 | 25.2 | 23.1 | 0.02 |
| DoubleBar | 83.4 | 16.9 | 30.1 | 32.7 | 3.7 |
| Double | 82.2 | 16.0 | 30.8 | 24.5 | 10.9 |
| Integer | 36.3 | 7.2 | 27.3 | 1.7 | 0.2 |
| IntegerBar | 68.3 | 24.5 | 28.7 | 12.5 | 2.6 |

The two timestamps are more than half of a row. `ReceivingTime` at 100 ns precision costs more than `Time` and often more than the value.

**Receiving-time precision, bits per row over all types:**

| Step | 100 ns | 1 ms | 10 ms | 100 ms | 1 s |
|---|---|---|---|---|---|
| Bits | 27.0 | 11.9 | 8.4 | 5.0 | 2.5 |

**Built stores:**

| Variant | B/row after `Compact()` | Q2, dense sensor (ms) |
|---|---|---|
| Hour chunks, byte-aligned codec | 14.4 | 1.00 |
| Hour chunks, bit-level codec | 12.7 | 0.93 |
| Day chunks, bit-level codec + zstd | 9.8 | 0.78 |
| Week chunks, bit-level codec + zstd | 9.5 | 0.92 |
| Day chunks + zstd, receiving time at 1 ms | 7.9 | 0.81 |
| Week chunks + zstd, receiving time at 1 ms | 7.5 | 0.75 |

Every store round-tripped all 531,480 rows with no mismatch. With the receiving time at 1 ms only `ReceivingTime` differs, by at most half a millisecond; it never moves to the other side of `Time`.

### Rounding of values (what-if)

Bits per value at week chunks, with zstd in brackets where it helps:

| Column | Exact | 3 significant digits | 4 significant digits | 6 significant digits | 1 decimal place | 2 decimal places |
|---|---|---|---|---|---|---|
| Rate value | 28.9 (24.4) | 4.4 (2.8) | 5.4 (3.6) | 8.9 (7.0) | 1.6 (0.5) | 2.5 (1.4) |
| Double value | 22.1 | 5.1 | 7.8 | 10.7 | 4.5 | 6.3 |
| Double ema | 10.0 | 3.0 | 4.5 | 7.7 | 7.5 | 10.0 |
| DoubleBar mean | 6.5 | 5.1 | 5.9 | 6.5 | 3.7 | 5.8 |
| DoubleBar min / max | 3.9 / 5.7 | 3.3 / 5.1 | 3.6 / 5.5 | unchanged | 2.6 / 3.9 | unchanged |

- Bar fields already carry two decimals in this data, so rounding them gains almost nothing. The gain is in Rate, in plain Double values and, with significant digits, in Double EMA.
- A fixed number of decimal places compresses better than significant digits but rounds small values to zero (relative error up to 1.0 for Rate and Double at one or two places).
- Whole store with every double rounded: 5.5 B/row at 3 significant digits (relative error up to 0.5%), 5.7 B/row at 4 (up to 0.05%), 5.6 B/row at 3 decimal places.
- Three decimal places are not safe for Rate: no non-zero value became zero, but 45% of the non-zero Rate values moved by more than 1% and 22% by more than 10%. With three significant digits no value in any column moved by more than 1%.
- After rounding, a Rate row is 33.5 bits, of which the value is about 3 and the two timestamps are the rest.

### ClickHouse

`ReplacingMergeTree` with a version column, `ORDER BY (sensor_no, time_ticks)`, weekly partitions.

- Size: 27.3 B/row with default compression; 14.9 with codecs chosen per column (Delta with ZSTD for ticks, plain ZSTD for doubles — Gorilla and FPC were both larger — Delta or T64 with ZSTD for small integers); 12.6 with the decimal split and the millisecond receiving time. At ×20: 12.9 B/row.
- Reads: Q1 5.4 ms, Q2 32.2 ms, Q4 4.7 ms, Q3 as one query 58.7 ms (1,642 ms at ×20).
- Batched ingest: 98,602 rows/s. One row per `INSERT`: 16 rows/s synchronously (about 64 ms each), 811 rows/s with asynchronous inserts that do not wait for the flush.
- Rewrite: a newer version of a key is a new row; reads need `FINAL` until a merge removes the old one, and `FINAL` returns the highest version, not the last insert.
- A lightweight `DELETE` of a time range works, and `OPTIMIZE FINAL` then frees the space (7.9 → 4.6 MB for three days of all sensors).
- A row six days older than the newest is accepted and visible at once.
- Memory: about 1.2 GiB idle and 1.7 GiB during queries without a limit; it also ran the load and Q2 inside a 512 MiB container.

### TimescaleDB

Hypertable on the integer tick dimension, weekly chunks, unique index on `(sensor_no, time_ticks)`; compression with `segmentby = sensor_no`, `orderby = time_ticks`.

- Size: 191.2 B/row uncompressed (more than today's LevelDB), 18.2 compressed, 11.3 compressed with the decimal split and the millisecond receiving time.
- Reads: Q1 1.0–1.1 ms, Q2 18.9 ms uncompressed and 13.3 ms compressed, Q4 0.7–0.8 ms, Q3 58–207 ms (1.1–7.8 s at ×20).
- Batched ingest: 211,765 rows/s. One row per statement: 258 rows/s. Upsert of an existing key: 253 rows/s uncompressed, 225 rows/s on a compressed chunk; the last value is returned.
- Deletes on an uncompressed chunk free space only after `VACUUM FULL`. On a compressed chunk, deleting three days of all sensors failed with the default settings (`tuple decompression limit exceeded`); with the limit lifted it grew the chunk from 9.7 to 53.0 MB until `VACUUM FULL`.
- Memory: about 400 MiB at this size, 2.5 GiB after the ×20 work; it ran inside a 512 MiB container.
- Compression is under the Timescale License of the Community Edition, not in the Apache-2 edition (documentation claim).

### QuestDB

- Size: 230.5 B/row in closed native partitions, 21.7 after converting them to Parquet; the active partition is preallocated (553 MB for 79,000 rows).
- Ingest: 311,593 rows/s batched, 924 rows/s one row per request; upsert through `DEDUP UPSERT KEYS` at the same rate.
- Reads: Q1 1.2 ms, Q2 18.8 ms, Q4 0.7 ms, Q3 5.7 ms.
- Row `DELETE` is refused: retention for one sensor and "clear history" cannot be expressed.
- Memory: 0.6 GiB idle on the first run, about 2 GiB after repeated runs.

### Not measured

- Rounding broken down by what a sensor measures (CPU, memory, disk, network). The shard copy carries sensor ids but no paths; the classification needs the sensor list of the source deployment.
- The cost of sealing: re-encoding a period into a chunk, and reads that merge sealed chunks with later rows.
- Size before and after `Compact()` on a large production database.
- Native Windows builds of the external servers.

## Findings

1. A closed week shrinks 2.8 times by compaction alone. Nothing triggers that compaction today.
2. No measured product beats a codec on top of LevelDB on this data. With the same precision the order is: chunked LevelDB 7.5 B/row, Parquet 10.4, TimescaleDB 11.3, ClickHouse 12.6; lossless, 9.5 against 13.3–18.2.
3. One-sensor reads are the server's main query, and the key-value layout wins them by an order of magnitude: 0.75–0.92 ms for a week of the dense sensor against 13–32 ms for the columnar stores.
4. Every columnar or time-series candidate is weak on single-value writes and rewrites: 16–924 rows/s against 405,188 for LevelDB. Each would need a write buffer in front of it.
5. DuckDB does not fit as the primary store: slow single-row writes and rewrites, deletes that do not return space, and a file that is not much smaller.
6. VictoriaMetrics and Prometheus are small only for what they are built for: an unchanged number on a regular grid. On this data, with every field kept, VictoriaMetrics is larger than the alternatives, rounds doubles, loses rows that share a millisecond, and cannot express "last write wins" or a range delete.
7. Difference-based encoding gives no order of magnitude here, because timestamps are irregular at 100 ns precision and a changed double changes almost all of its mantissa. Decimal scaling is what compresses bars.
8. Closed weeks in Parquet are a workable alternative to a codec of our own, at a cost: 8.5 against 5.5 B/row with the same rounding (11.1 against 7.5 without it), and 16–22 ms against 1 ms for a week of one sensor. The sealing job, the merged read and retention by rewrite are needed either way; Parquet replaces only the block encoder, and adds a reader dependency and a memory-hungry seal.
9. After the values are encoded well, the timestamps dominate. Precision is the remaining lever: the receiving time at 1 ms takes the store from 9.5 to 7.5 B/row, and rounding Rate and Double values to four significant digits would take it to 5.7.

## Decisions (owner)

- 2026-11-19: `ReceivingTime` may be stored at millisecond precision. `Time` stays exact: it is part of the row key.
- 2026-11-19: an external database server is acceptable; the store does not have to be embedded.
- Not decided: rounding of stored values. It was measured as a what-if only.

## Proposed Direction

Keep LevelDB and add a codec of our own for closed periods. The owner's reading of the results on 2026-11-19 leans the same way, with closed weeks in Parquet as the alternative the owner raised for its lower implementation risk; the choice between the two is the subject of the ADR.

1. Compact closed weekly shards automatically. It is the cheapest gain and changes no format.
2. Seal closed periods into chunks on top of LevelDB: current data stays one row per value, so writes, rewrites and late values keep working; a background job re-encodes a closed period per sensor with the bit-level codec. Reads merge sealed chunks with any rows written into that period later.
3. Decide separately whether values may be rounded, and if so per sensor rather than globally.
4. Do not adopt DuckDB, VictoriaMetrics, ClickHouse, TimescaleDB or QuestDB as the history store.

## Work Breakdown

| Step | Scope | Notes |
|---|---|---|
| 1 | Confirm on a larger deployment | Size before and after `Compact()` on production data |
| 2 | Scheduled compaction of closed weeks | No format change |
| 3 | ADR: format for sealed periods | Own chunk codec or Parquet; block layout, version, checksum, chunk period, precision of `ReceivingTime` |
| 4 | Chunk codec and sealing job | Lossless round-trip tests over every value type |
| 5 | Merged reads, retention and clear-history on sealed data | Includes sensor-move merge |
| 6 | Rounding by kind of sensor | Needs the missing measurement and an owner decision |

## Risks

- A chunk format is a storage format: compatibility-sensitive and permanent. It needs a version byte, a checksum, and a lossless check over real data before it is trusted.
- Week-sized chunks are only suitable for sealed data: adding a value means rewriting the chunk. The cost of that path was not measured.
- Rounding `ReceivingTime` affects code that matches it exactly: the file download looks a File value up by the exact ticks of its `ReceivingTime` (`HomeController`). File values must keep exact timestamps or that lookup must change.
- The measurement used one shard from a small deployment where Rate is 61% of rows. A deployment with a different mix of types will see different ratios.
- The ×20 dataset is cloned, not real.
- The codec in the benchmark is a prototype. Its numbers show what is reachable, not what a production implementation will cost to build and maintain.

## Verification

- Losslessness of each variant was checked by reading every row back.
- Before any implementation: repeat the size measurement on a production database, and add storage tests that round-trip every value type through the chosen format.
- Update `aicontext/architecture/database.md` and `aicontext/features/storage/` when behavior changes.
