# ADR-0021: Table sensors store client-encoded batches in LevelDB under a key that only grows

**Status:** Accepted
**Date:** 2026-11-23
**Supersedes:** —

---

## Context

HSM stores one value at a time: every value passes the sensor model and becomes one row. Some data arrives as a dense table instead. The first case is tick data with five columns, tens of thousands of rows a second from one source, hundreds of millions of rows a day. Per-value ingestion cannot carry that; a production server takes about 1.7 million values a day.

The owner's requirement: store such tables efficiently, delete them on time, take them in and give them back, and draw them in some basic way. The application hands the collector a ready table; a long delivery delay is acceptable.

Step 0 measured the options on a real sample (`docs/initiatives/table-sensors.md`): 440,555 rows of 1,029 instruments, one table per instrument.

- Bytes per row with five columns, 4,096-row batches: the sealed-history column coders with zstd 15 give 5.22, plain zstd 15 over raw columns 5.83, Parquet 7.95–9.59. With three columns the figures are 2.60, 3.58 and 4.28–5.50.
- Batch size hardly matters above about 4,096 rows. With 65,536-row batches a read of one minute of all instruments costs 3.1 s instead of 0.2 s.
- Where batches live decides how often the disk writes them. In LevelDB under the key `sensor, first time` each byte is written 14.4 times at seven days of data, because batches of different sensors arrive interleaved and every new file overlaps the old ones. Under a key that only grows it is written 2.0 times, with no compaction at all.
- A Parquet writer costs a heavy dependency in both collectors: the last Parquet.Net with a `netstandard2.0` target is 5.5.0, and in C++ it means Apache Arrow.

Constraints:

- DTOs in `HSMSensorDataObjects`, the public collector API and storage formats are compatibility-sensitive.
- The collector targets net6.0 and net472, and the native collector depends only on curl today.
- The server accepts JSON only: 50 MB per HTTP request, text frames on the collector session.
- Sensor history is not part of the server's backups.

## Decision

1. **A table sensor is a new kind of sensor whose unit of data is a batch.** A batch is a matrix of rows of one sensor, ordered by time. Table sensors have no per-row statuses, alerts or comments.
2. **The schema belongs to the sensor.** It is fixed when the sensor is registered: an ordered list of named, typed columns, the first of which is the time. The first version has three column types: time, 64-bit integer and double. Columns are dense: a row has a value in every column. A different shape is a different sensor. Every batch carries the id of its schema, so that adding a column to an existing sensor can be introduced later without a new batch format.
3. **Rows of a batch are ordered by time, and equal times are allowed.** Ticks repeat timestamps; the order of rows inside a batch is preserved exactly.
4. **The batch format is HSM's own.** A batch is a header (magic, format version, encoder revision, schema id, row count, first and last time, payload length, CRC-32C) followed by the columns, each coded with the integer and double coders of sealed history (ADR-0020) and compressed with zstd from a library. Every value is bit-exact. Format version and encoder revision follow the rules of ADR-0020, point 12, and golden batches pin the bytes.
5. **The collector encodes and compresses.** The coders move into a library that the server and the managed collector share; the native collector gets a port whose output is byte-identical, locked by conformance scenarios.
6. **The collector closes a batch at 4,096 rows or after 60 seconds, whichever comes first.** When it stops it closes every open batch and sends them at once. Batches it could not deliver are kept in a local file and sent after the next start.
7. **The server stores a batch as it arrived.** On ingest it checks the header against the sensor's schema and limits and verifies the checksum; it does not decode the columns. Its work per batch does not depend on the number of rows.
8. **Batches live in a LevelDB database of their own, under a key that only grows:** `arrival hour, sequence number`. A second small database is the index: `sensor, first time` → batch key, last time, row count. Batches never share a database with sensor values, and nothing writes into the batch database out of key order.
9. **Retention works on arrival hours.** The server deletes the batches whose arrival hour is older than the limit, with their index records, and then compacts that key range. The first version has one retention period per product. Removing a sensor removes its index records at once; its bytes leave when their hours expire.
10. **Batches are sent over HTTP, not over the collector session.** A new binary route takes one or more batches per request under the product's access key. The session keeps carrying values and commands and stays text-only. This is a deliberate addition to ADR-0019: bulk data that may be late must not queue ahead of values and commands on the one session connection.
11. **A repeated batch is stored once.** A batch is identified by its sensor, its first time and a sequence number given by the collector, so a retry after a lost acknowledgement is safe.
12. **A read returns batches, and the client library returns a matrix.** A range request returns the stored batches that overlap the range; the library decodes them and trims to the range. For clients without the library the server decodes into CSV or JSON, limited in rows.
13. **The chart is a reduction on the server:** the requested range of one column, decoded and reduced to a minimum and a maximum per pixel column. It comes after ingest and read.
14. **The feed is watched through ordinary sensors** derived from batch headers: rows, bytes and delivery lag.

## Consequences

- Five-column tick data takes about 5.2 bytes per row from the moment it arrives: 5.2 GB per billion rows. There is no uncompressed window.
- Every batch byte reaches the disk twice (the LevelDB log, then a table file). Space freed by retention returns only when the server compacts the deleted range, which costs 0.1 s and 14 MB at seven days of data.
- A batch that arrives late is stored with its arrival hour, so it lives longer than the retention period by its delay.
- Per-sensor retention is not available in the first version. Giving it later means deleting batches out of key order, which brings compaction back for the affected hours.
- HSM owns a second on-disk and on-wire format next to sealed chunks. Both must stay readable forever; the batch format additionally has to produce the same bytes in .NET and in C++.
- The server does not see the content of a batch on ingest. A batch whose columns are damaged in a way the checksum does not catch is found at the first read; the reader must report it as an error and never return a partial table.
- A batch is client-supplied bytes that the server decodes later for CSV, JSON and the chart. Limits on rows, columns and decoded size are part of the format and are checked before any column is decoded.
- A collector with table sensors uses two channels: the session for values and commands, HTTP for batches. An environment that allows only the session connection cannot send tables.
- The public collector API and the shared DTOs grow, additively. A server without the feature answers the batch route with an error; the collector reports it through its diagnostic sensors and keeps the batches in its local file up to a size limit, after which the oldest are dropped and counted.
- A crash of the application loses the rows of the open batches: up to 60 seconds or 4,095 rows per sensor. A clean stop loses nothing.
- Disk becomes a planning question. On the test feed (276 M rows a day) a year is about 525 GB, more than the roughly 550 GB free on production leaves room for; the retention period or the disk has to be decided before the feature is switched on there.

## Alternatives Considered

- **Parquet as the batch format:** readable by standard tools, but 50–110% larger in the layout this feature uses, and a heavy dependency in both collectors. CSV and JSON export covers the readers without the library.
- **Plain zstd over raw columns:** 12–38% larger than the column coders, which already exist and are tested.
- **Compression on the server:** the server would decode and encode every row, and the network would carry uncompressed tables.
- **LevelDB under the key `sensor, first time`:** no index needed, but 14.4 times the writes at seven days and 42 s with 9.2 GB rewritten to free six deleted hours.
- **LevelDB under `hour, sensor, first time`:** 5.6 times the writes; keys are still scattered inside the hour.
- **One LevelDB database per hour:** 1.9 times the writes and space back at once, but 226 databases, 904 open handles and 4 s to open after seven days.
- **Append-only hour files with their own index:** 1.0 times the writes and space back at once, but a new storage engine to write and to test against crashes.
- **Batches in hour files with only their addresses in LevelDB:** rejected by the owner: two stores that must be kept consistent.
- **SeaweedFS:** a separate service next to the server; not measured. Rejected by the owner together with the file-based variants.
- **Binary frames on the collector session:** one connection for everything, but a large upload would hold back values and commands, and both collectors and the server would need binary-frame support.

## References

- Epic #1842, steps #1843–#1849
- Measurement and proposal: `docs/initiatives/table-sensors.md`
- ADR-0020 (sealed sensor history): the column coders and the versioning rules reused here
- ADR-0019 (collector session as the default transport): point 10 adds a second channel for batches
