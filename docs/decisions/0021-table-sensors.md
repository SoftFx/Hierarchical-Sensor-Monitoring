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
- A time limit on a batch costs size. With five columns, header, key and index record included, a row takes 11.9 bytes in 10-second batches, 6.8 in 60-second batches and 5.5 when a batch covers the whole 185-second sample; the size was still falling there. The median table has 229 rows in 185 seconds, so most tables never fill 4,096 rows within minutes.
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
6. **The collector closes a batch at 4,096 rows or after 60 seconds, whichever comes first.** When it stops it closes every open batch and sends them at once. Batches it could not deliver are kept in a local file and sent after the next start. The time limit keeps the data fresh on the server and bounds what a crash of the application can lose; the small batches it produces are merged by the server (point 15).
7. **On ingest the server stores a batch as it arrived.** It checks the header against the sensor's schema and limits and verifies the checksum; it does not decode the columns. Its work per batch on ingest does not depend on the number of rows.
8. **Batches live in a LevelDB database of their own, under a key that only grows:** `arrival hour, sequence number`. A second small database is the index: `sensor, first time` → batch key, last time, row count. Batches never share a database with sensor values, and nothing writes into the batch database out of key order.
9. **Retention works on the hour in the batch key:** the arrival hour of a batch that is not merged yet, the merge hour of a merged one. The server deletes the batches whose hour is older than the limit, with their index records, and then compacts that key range. The first version has one retention period per product. Removing a sensor removes its index records at once; its bytes leave when their hours expire.
10. **Batches are sent in HTTPS requests, not over the collector session.** A new binary route of the Sensor API, on the same port 443 and under the same TLS as everything else, takes one or more batches per request under the product's access key. Nothing travels unencrypted. The session keeps carrying values and commands and stays text-only. This is a deliberate addition to ADR-0019: bulk data that may be late must not queue ahead of values and commands on the one session connection.
11. **A repeated batch is stored once.** A batch is identified by its sensor, its first time and a sequence number given by the collector, so a retry after a lost acknowledgement is safe.
12. **A read returns batches, and the client library returns a matrix.** A range request returns the stored batches that overlap the range; the library decodes them and trims to the range. For clients without the library the server decodes into CSV or JSON, limited in rows.
13. **The chart is a reduction on the server:** the requested range of one column, decoded and reduced to a minimum and a maximum per pixel column. It comes after ingest and read.
14. **The feed is watched through ordinary sensors** derived from batch headers: rows, bytes and delivery lag.
15. **The server merges the small batches of a closed hour, once.** *(Added 2026-11-24 on the owner's decision, after the time-limit measurement; the model is VictoriaLogs, which writes small parts first and merges them in the background.)* When an arrival hour has closed, a background job takes the batches of each sensor in that hour, decodes them and encodes their rows again, in time order, into batches of up to 4,096 rows in the same format. The merged batches go into a second database of the same kind, under a key that only grows (`merge hour, sequence number`). The job decodes what it wrote and compares it with the source rows, switches the index records to the merged batches, and only then deletes the hour's arrival batches as one range. A batch is merged once; there is no cascade of merges. A batch the job cannot decode stays where it is, is counted and is reported; it is never dropped.

## Consequences

- Five-column tick data takes about 6.8 bytes per row while it sits in 60-second batches and about 5.5 after the merge on the measured three-minute sample, header, key and index record included. With full hours to merge the size is expected near 4.9 (from a scaled run; to be confirmed in the rollout step). There is no uncompressed window.
- Every row reaches the disk four times: twice when its batch arrives (the LevelDB log, then a table file) and twice when it is merged. Both databases take keys in growing order only, so neither compacts during writes. Space freed by the merge and by retention returns when the server compacts the deleted range, which costs 0.1 s and 14 MB at seven days of data.
- The merge costs the server about 0.5 µs of CPU per row: about 133 seconds a day for 276 M rows on one core.
- The merge is a background job that must survive a crash at any point: an hour is either served from its arrival batches or from its merged batches, never from both and never from neither, and a repeated run of the same hour changes nothing.
- A batch that arrives late is stored with its arrival hour and merged with that hour, so it lives longer than the retention period by its delay, and the batches of one sensor can overlap in time. A reader merges them by time.
- Per-sensor retention is not available in the first version. Giving it later means deleting batches out of key order, which brings compaction back for the affected hours.
- HSM owns a second on-disk and on-wire format next to sealed chunks. Both must stay readable forever; the batch format additionally has to produce the same bytes in .NET and in C++.
- The server does not see the content of a batch on ingest. A batch whose columns are damaged in a way the checksum does not catch is found by the merge, within about an hour, or by a reader before that; either reports it as an error and never returns or writes a partial table.
- A batch is client-supplied bytes that the server decodes later for the merge, CSV, JSON and the chart. Limits on rows, columns and decoded size are part of the format and are checked before any column is decoded.
- A collector with table sensors uses two channels: the session for values and commands, HTTPS requests for batches. An environment that allows only the session connection cannot send tables.
- The public collector API and the shared DTOs grow, additively. A server without the feature answers the batch route with an error; the collector reports it through its diagnostic sensors and keeps the batches in its local file up to a size limit, after which the oldest are dropped and counted.
- A crash of the application loses the rows of the open batches: up to 60 seconds or 4,095 rows per sensor. A clean stop loses nothing.
- Disk becomes a planning question. On the test feed (276 M rows a day) a year is about 500–550 GB, which the roughly 550 GB free on production cannot take together with everything else; the retention period or the disk has to be decided before the feature is switched on there.

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
- **A long time limit on the collector and no merge on the server:** the simplest server; 5.5 bytes per row at three minutes. The data would reach the server late by that limit and a crash of the application would lose that much, and tables that never fill a batch in the limit stay in small batches for good.
- **A cheaper encoding on the collector before the merge (coders with zstd 3, or raw columns with zstd 3):** 6.4–7.6 bytes per row on the wire against 6.2 for the final encoding, which costs the collector only about 0.5 µs per row. One encoding at both stages keeps one format.
- **A cascade of merges, as VictoriaLogs does:** each level rewrites the data again; one merge per hour already brings a typical table to full batches.
- **Binary frames on the collector session:** one connection for everything, but a large upload would hold back values and commands, and both collectors and the server would need binary-frame support.

## References

- Epic #1842, steps #1843–#1849 and the merge step #1850
- Measurement and proposal: `docs/initiatives/table-sensors.md`
- ADR-0020 (sealed sensor history): the column coders and the versioning rules reused here
- ADR-0019 (collector session as the default transport): point 10 adds a second channel for batches
