# Initiative: Table sensors — storing matrices in HSM

> Owner: shared | Last reviewed: 2026-11-23 | Status: Draft for the owner's review | Canonical: no

## Problem

HSM stores one value at a time. Every value goes through a sensor, alert evaluation and the tree cache, is written as one row, and is compressed only weeks later when its week is sealed (ADR-0020, epic #1800). That fits monitoring: a production server takes about 1.7 million values a day.

It does not fit data that arrives as a dense table. The first case is market data — ticks with bid, ask and sizes — at about 20 GB a day in its present form, which is on the order of hundreds of millions of rows a day, a few hundred times the current load. The same shape appears wherever a client has a matrix rather than a value: rows of measurements with many columns, order books, traces, batches of results.

The owner's requirement (2026-11-20), in priority order:

1. efficient storage of matrices — financial data first, but any multi-column table;
2. sending them to HSM and getting them back;
3. their timely deletion;
4. some rendering, as a nice-to-have.

## Goals

- A client sends a table (a batch of rows with typed columns) as one message; HSM stores it compressed and returns it on request.
- Storage cost close to a column store's, from the moment the data arrives — no uncompressed window.
- The server's work per batch is independent of the number of rows in it.
- Retention by time per sensor, enforced without rewriting data.
- The stored data is readable by standard tools without an HSM library.
- A basic chart of a numeric column over a time range.

## Non-Goals

- Per-row alerts, statuses or comments on table rows. A table sensor can still be watched by ordinary sensors (rows per minute, lag, gaps).
- Editing or deleting individual rows. A range of time can be deleted.
- A query engine (joins, aggregation across sensors). HSM returns the data; analysis happens in the client's tools.
- Replacing ordinary sensors or the sealed history codec of ADR-0020.
- Sub-second delivery of a row to a reader. A row is visible when its batch is.

## Current Behavior

- A value is one of eleven value types; the collector sends values in small packages; the server deserializes each value, runs the sensor model (validation, alerts, last value, aggregation of equal values) and writes one LevelDB row per value: key `sensorId | ticks`, value = MemoryPack bytes, about 130 bytes before LevelDB's own compression.
- File sensors already carry opaque bytes as a value. They are meant for occasional files: a value is one row, is never sealed, and the UI offers it as a download.
- Epic #1800 adds, on the branch `feature/1800-sealed-history`: a chunk database per week (`SensorValuesSealedV1_<from>_<to>`, key `sensorId | firstTick`, value = one compressed chunk of up to 4,096 values of one sensor), reads merged from chunks and rows, range deletes that drop whole chunks and rewrite the ones on the border, and a job that turns closed weeks into chunks. Measured on a production week: 10.6 bytes per value as built, 7.7 with the string and compression changes in progress, against about 160 on disk today.

The chunk database is, in effect, a store of small matrices of one sensor. This initiative lets a client write to such a store directly.

## Proposed Direction

A new kind of sensor, a **table sensor** (proposed term, not yet in the glossary). Its unit of data is a **batch**: a matrix of rows ordered by time, with a fixed set of typed columns.

```text
client                                  server                                   reader
------                                  ------                                   ------
collect rows into a table
encode by column + compress   --->      check the batch header only:             range query (sensor, from, to)
one message per batch                   schema id, rows, first/last time,   <---
                                        size, checksum                           batches as stored, or merged
                                        store the batch as it is                 a chart: decode + downsample
                                        key: sensorId | firstTime
                                        retention: drop batches older than N
```

Decisions to make, with the current leaning:

| Question | Leaning | Why / alternative |
|---|---|---|
| Batch format | Parquet, one row group per batch, zstd, sorted by the time column | Readable by Python, DuckDB, ClickHouse; writers exist for .NET, C++, Rust. Alternative: the HSM chunk codec — likely smaller, but only the .NET library reads and writes it, and it knows only HSM's value types. To be measured on real ticks before deciding. |
| Schema | Declared when the sensor is registered: column names and types, the time column, optional key columns; a batch carries the schema's id | The server can validate a batch from its footer without decoding data. Adding a column is a new schema id; old batches keep theirs. |
| More than two dimensions | Key columns (for example instrument, level) or list-typed columns | A matrix of rows and columns covers the first cases; no dedicated N-dimensional type until one is needed. |
| Where batches live | To be measured: values in the weekly chunk LevelDB, or append-only files with a LevelDB index | LevelDB rewrites large values several times during compaction; at a few GB a day that may cost more than it is worth. |
| Small batches | The server may merge the small batches of a closed period into larger ones in the background | Keeps the client simple (send what you have) and the stored files well compressed. Optional in the first version. |
| Retention | Per sensor, by time; a batch is deleted when its last row is older than the limit | No rewrite of border batches: a batch lives until all of it has expired. |
| Reading | A range query returns the batches that overlap the range, as stored; options to trim to the range and to select columns | Returning stored bytes costs the server nothing. Trimming and projection decode on the server and are limited in size. |
| Rendering | Decode the requested range, downsample to the chart's width (min and max per bucket; candles when the schema marks price columns) | "Some rendering", bounded in rows per request. |
| Duplicates and late data | A batch is identified by sensor, first time and a client sequence number; a repeated batch replaces the stored one | Makes a retry after a lost acknowledgement safe. Overlapping batches are returned in time order; the reader merges. |
| Watching the feed | Ordinary sensors derived from batch headers: rows, bytes, lag, gaps between batches | Per-row alerts are a non-goal. |

What it reuses from #1800: the weekly layout and its retention, the chunk-style key, range reads in key order, size accounting, the Configuration page's database list. What is new: the sensor kind and its DTOs, the batch ingest path, the read API, the chart, the collector API.

## Step 0 results (2026-11-23)

Measured with new modes of `src/sandbox/SealedHistoryBench` on the local branch `experiment/table-sensors-step0` (not pushed, not meant for master). The input was a test sample of quote ticks: 440,555 rows, 185 seconds of one busy hour, 1,029 instrument streams, time at 1 ms precision. The sample is private; only counts, sizes and timings are recorded here.

Owner's clarifications of the same day, which fix the shape of the feature:

- The application hands the collector a ready matrix; the collector compresses it and sends it. A large delivery delay is acceptable.
- The description of the matrix (number of columns, names, types) belongs to the sensor and does not change. Another shape (another book depth, prices without volumes) is another sensor. Columns are dense; nulls are not planned.
- Adding a column to an existing sensor is not needed now. A schema id in the batch header keeps it possible later without a format change.

What the sample looks like: 80.6% of rows carry the same timestamp as the previous row and 29.4% are exact duplicates of it, so a format that requires strictly ascending keys (sealed chunk format v1) cannot hold ticks. Prices have 1 to 10 decimal places; every value round-trips as a double bit for bit.

Bytes per row, one table per instrument (the layout the owner chose), batches of 4,096 rows; every configuration round-trips with zero mismatches:

| Codec | time, bid, ask (real data) | + price and volume (price derived, volume synthetic) | Encode, µs per row |
|---|---|---|---|
| Raw columns, 8 bytes per cell | 24.0 | 40.0 | 0.006 |
| Raw columns + zstd 15 | 3.58 | 5.83 | 1.8–2.8 |
| Parquet, library defaults, zstd 3 | 5.50 | 9.59 | 0.14–0.25 |
| Parquet, tuned, prices as scaled integers, zstd 19 | 4.28 | 7.95 | 0.7–1.0 |
| HSM column coders + zstd 3 | — | 5.43 | 0.19 |
| **HSM column coders + zstd 15** | **2.60** | **5.22** | 0.20–0.33 |

For comparison, the same rows are 58 bytes each as log text and 118 as JSON lines. Decoding costs 0.03–0.05 µs per row for the HSM coders. Batch size hardly matters above about 4,096 rows (5.31 bytes at 1,024 rows, 5.21 at any larger size); zstd 19 gains nothing over 15. Parquet wins only in a layout this feature will not use: one table with all instruments interleaved, encoded as a single batch of the whole sample (4.4 bytes against 7.4).

Where the server keeps batches, on a scaled day (the sample repeated with shifted timestamps to 276 M rows; the mechanics are real, the compression is not the point):

| | Batches as LevelDB values | Append-only file per hour + index |
|---|---|---|
| Size on disk | 1.37 GB | 1.37 GB |
| Bytes written to disk | 15.2 GB (11.1× the data; 13.7× at seven days) | 1.37 GB (1.0×) |
| Delete the oldest 6 hours | space returns only after `Compact` (3.5 s; 35 s at seven days) | 0.04 s, space returns at once |
| Read one hour of one instrument | 0.5–6 ms | 2–7 ms |
| Read one minute of all 1,029 instruments, 4,096-row batches | 0.19 s, 22 MB read | 0.19 s, 22 MB read |
| The same with 65,536-row batches | 3.1 s, 346 MB read | 3.2 s, 346 MB read |

Client side, from package metadata and code reading: the last Parquet.Net release with a `netstandard2.0` target is 5.5.0 (later releases need net8.0) and it brings System.Text.Json 10 into net472 consumers; a C++ Parquet writer means Apache Arrow through vcpkg (OpenSSL, Thrift, Boost and more; no 32-bit x86), where the native collector depends only on curl today. The HSM integer and double coders are about 840 lines, need four trivial polyfills for net472, and ZstdSharp.Port supports net472; zstd itself is a single small vcpkg port.

Not measured: more than three minutes of data; real price and volume columns; Linux (the run was on Windows); cold-disk reads; the collector on .NET Framework; delivery delay.

## Proposal after step 0

Waiting for the owner's decision; nothing below is built.

| Question | Proposal |
|---|---|
| Batch format | Own format: a header (magic, version, schema id, row count, first and last time, lengths, CRC-32C) followed by the columns coded with the sealed-history integer and double coders and compressed with zstd 15. Parquet is 50–110% larger in this layout and costs a heavy dependency in both collectors. |
| Who compresses | The collector. The coders move into a `netstandard2.0` library shared by the server and the managed collector; the native collector gets a C++ port with byte-identical output locked by conformance scenarios. |
| Cutting by time | The collector closes a batch of one sensor at 4,096 rows or after a time limit, whichever comes first. The server appends batches to one file per hour and keeps an index record per batch. |
| Deletion | Retention deletes whole hour files; no compaction, no rewriting. Removing one sensor drops its index records, and the bytes leave with their hour file. |
| Ingest API | A new binary route (`application/octet-stream`, one or more batches per request). The server checks the header and the checksum only. Whether a collector on a session may send binary frames instead is a question for the ADR. |
| Read API | A range request returns the stored batches that overlap the range, as stored. A CSV variant decodes on the server and is limited in rows. |
| Rendering | The server decodes the requested range of one column and reduces it to minimum and maximum per pixel column. Reading an hour of one instrument takes milliseconds, so this is cheap; it comes after ingest and read. |

Open: per-sensor retention with shared hour files (one retention value per product is the simple first version); the session question above; the time limit of a batch.

## Capacity

Stored size is measured on the test sample; the row counts of the future product are not known.

| Quantity | Value | Basis |
|---|---|---|
| Stored size | 5.2 bytes per row with five columns, 2.6 with time, bid and ask | step 0; the volume column was synthetic |
| A billion rows | 5.2 GB | the same |
| Test feed, quotes only (about 97 M rows a day) | 0.5 GB a day, 15 GB a month, 185 GB a year | the share of quotes is estimated from three minutes |
| Test feed, every record of the ticks file (276 M rows a day) | 1.4 GB a day, 43 GB a month, 525 GB a year | row count measured on 2026-11-20, the heaviest day |
| A sustained 10,000 rows a second | 4.5 GB a day, 135 GB a month, 1.6 TB a year | arithmetic |
| Free disk on production | about 550 GB | checked 2026-11-19 |
| Server work per batch | header and checksum check, one append | no per-row state; write amplification 1.0 |

## Work Breakdown

| Step | Scope | Notes |
|---|---|---|
| 0 | Measure on one real day of ticks: row count, instruments, Parquet (zstd) against the HSM chunk codec against the present form; batch size against compression and read time; LevelDB values against files | Decides the format and the storage layout. Needs a sample from the owner. |
| 1 | ADR: the table sensor, the batch format, the schema rules, retention, what the server validates | After step 0. |
| 2 | Server: sensor kind and DTOs, ingest of a batch, storage, retention, the range read API | DTOs in `HSMSensorDataObjects` are compatibility-sensitive. |
| 3 | Managed collector: a table sensor API that buffers rows, encodes and sends batches | Public API of a library used by third parties: additive only. |
| 4 | Chart of a numeric column; feed-health sensors from batch headers | |
| 5 | Native collector | Needs a Parquet writer in C++ (Arrow) — a large dependency; decide after the managed path is in use. Conformance coverage for both collectors is required by the project rules once the behaviour is portable. |

## Risks

- **Scope of HSM.** This makes HSM a store of bulk data as well as a monitoring system. Disk, backup and upgrade planning change with the volume; sensor history is not part of the server's backups today.
- **Disk.** A year of ticks does not fit on the current production disk. Retention or more storage has to be decided before the feature is switched on.
- **Compatibility.** A new sensor type reaches the shared DTOs and the public collector API. Older collectors and servers must ignore or refuse it cleanly.
- **A second on-disk format.** Parquet next to the HSM chunk codec means two readers to keep working forever. Using one format for both is cleaner but ties ordinary sensors to a format that does not know their types.
- **Large values in LevelDB.** Unmeasured; see the storage question above.
- **Untrusted input.** A batch is client-supplied bytes that the server later decodes for charts and trimmed reads: limits on rows, columns, decoded size and nesting are part of the format rules, and the decoder must come from a maintained library, not be written here.
- **Native collector.** A C++ Parquet writer is a heavy dependency for a library distributed through vcpkg.

## Verification

- Step 0 produces numbers on real data and is recorded here.
- Round trip: what a client sends is what a reader gets, byte for byte, for every batch.
- Retention removes exactly the expired batches; a restart in the middle loses and resurrects nothing.
- Ingest and read under the target volume on a test deployment: server CPU and memory, disk growth, read latency of a one-hour range.
- A batch that fails validation is refused with a reason and is counted; it never becomes a partly stored table.
- Docs: `aicontext/features/` for the feature, the glossary term, the wiki page for operators, the collector's public documentation.
