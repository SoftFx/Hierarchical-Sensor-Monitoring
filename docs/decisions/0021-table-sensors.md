# ADR-0021: Table sensors: collectors send simple blocks, the server packs them into batches in LevelDB

**Status:** Accepted
**Date:** 2026-11-23, rewritten 2026-11-24
**Supersedes:** —

---

## Context

HSM stores one value at a time: every value passes the sensor model and becomes one row. Some data arrives as a dense table instead. The first case is tick data with five columns, tens of thousands of rows a second from one source, hundreds of millions of rows a day, spread over about a thousand tables (one per instrument). Per-value ingestion cannot carry that; a production server takes about 1.7 million values a day.

The owner's requirement: store such tables efficiently, delete them on time, take them in and give them back, and draw them in some basic way. The application hands the collector ready rows. The data must be visible on the server within seconds, and the collector must stay simple and cheap for the application it runs in.

The measurements are in `docs/initiatives/table-sensors.md`, on a real sample of 440,555 rows of 1,029 tables in 185 seconds. Bytes per row below are for five columns unless stated.

- **A stored batch of one table.** With up to 4,096 rows per batch the sealed-history column coders with zstd 15 give 5.22, plain zstd 15 over raw columns 5.83, Parquet 7.95–9.59. With three columns: 2.60, 3.58 and 4.28–5.50. LZMA instead of zstd after the coders changes the size by about one percent and decodes 5–7 times slower.
- **Small batches are expensive.** A typical table has 14 rows in 10 seconds. One batch per table every 10 seconds costs 9.08 on the wire and 11.88 on disk, because every batch pays for its header, its key and its index record.
- **One block for all tables is not.** The rows of all tables of a 10-second window in one block, grouped by table, as raw columns under one zstd frame: 5.14 with zstd 15, 5.75 with zstd 3. The same block with the column coders inside: 5.26. Rows in arrival order with a table-id column: 7.57. Encoding the raw block with zstd 3 costs the collector 0.09 µs per row, against 0.70 for per-table batches with the coders and 9–15 with LZMA.
- **A block is slow to read for one table.** To get one table out of an hour of 10-second blocks every block is decompressed: 130–260 ms and 207–344 MB, against 0.1–8 ms for per-table batches.
- **The LevelDB key decides the write cost.** Under the key `sensor, first time` each stored byte is written 14.4 times at seven days of data, because the values of different sensors arrive interleaved and every new file overlaps the old ones. Under a key that only grows it is written 2.0 times, with no compaction at all.

Constraints:

- DTOs in `HSMSensorDataObjects`, the public collector API, the collector session protocol and storage formats are compatibility-sensitive.
- The collector targets net6.0 and net472, and the native collector depends only on curl today.
- The collector session (ADR-0017) carries text frames only and is the collectors' default transport (ADR-0019).
- Sensor history is not part of the server's backups.

## Decision

1. **A table sensor is a new kind of sensor whose data is rows.** Rows of one sensor are ordered by time. Table sensors have no per-row statuses, alerts or comments.
2. **The schema belongs to the sensor.** It is fixed when the sensor is registered: an ordered list of named, typed columns, the first of which is the time. The first version has three column types: time, 64-bit integer and double. Columns are dense: a row has a value in every column. A different shape is a different sensor. Blocks and batches carry the id of the schema, so that adding a column to an existing sensor can be introduced later without a new format.
3. **Equal times are allowed, and the order of rows is kept.** Ticks repeat timestamps; rows of one sensor come back in the order the application gave them.
4. **There are two packers, with different jobs.** The collector's packer moves data quickly and cheaply; the server's packer stores it compactly. They share nothing but the schema.
5. **The collector sends a block.** A block holds the rows of all table sensors of one collector for one tick. Inside, the rows are grouped by table: a directory (table, schema id, row count) and then the columns as raw fixed-width arrays, table after table, under one zstd frame at level 3, with a header in front (magic, format version, row count, table count, first and last time, length, CRC-32C). zstd comes from a library. The collector carries no HSM column coders.
6. **The tick is 10 seconds and belongs to the collector, not to a sensor.** At every tick the collector closes the block and sends it: one message for any number of table sensors. A block that would exceed the frame limit is split. When the collector stops it sends the open block at once. Blocks it could not deliver are kept in a local file and sent after the next start. Every block has a sequence number; a block that arrives twice is stored once.
7. **Blocks travel on a second connection of the collector session.** A collector with table sensors opens a second WebSocket of the session protocol (ADR-0017), used only for blocks: a block is a binary frame and the server acknowledges each one, as it acknowledges a request today. The connection for values and commands is unchanged and stays text-only, so a large block never waits ahead of a value or a command. Table sensors need the session transport; there is no HTTP route for sending blocks in the first version.
8. **On ingest the server checks a block and stores it as it arrived.** It verifies the header and the checksum, decompresses the block, checks the directory against the registered sensors, their schemas and the limits, and stores the block unchanged in a LevelDB database of its own under a key that only grows: `arrival hour, sequence number`. The rows of a table the server refuses are reported in the acknowledgement and are neither served nor merged; the rest of the block is accepted.
9. **The server packs the blocks of a closed hour into batches, once.** When an arrival hour has closed, a background job splits its blocks by table and encodes the rows of each table, in time order, into batches of up to 4,096 rows: the integer and double coders of sealed history (ADR-0020) and zstd 15, with a header (format version, encoder revision, schema id, row count, first and last time, checksum). It decodes what it wrote and compares it with the source rows, marks the hour as merged, and only then deletes the hour's blocks as one range. There is one merge, not a cascade. A block the job cannot read stays where it is, is counted and is reported; it is never dropped.
10. **Batches live in LevelDB, one database per product, under a key that only grows:** `merge hour, sequence number`. A small index maps `sensor, first time` to the batch key, the last time and the row count. Nothing writes into a block or batch database out of key order, and neither shares a database with sensor values.
11. **The batch format is internal to the server.** Nothing outside the server reads or writes it. It follows the versioning rules of ADR-0020, point 12, and golden batches pin its bytes.
12. **Retention is one period per product.** The server deletes the batches of a product whose hour is older than the period, as one range of its database, with their index records, and compacts that range. Removing a sensor removes its index records at once; its bytes leave when their hours expire.
13. **A read returns rows in the block format.** A range request (sensor, from, to) over HTTPS is answered from the batches of merged hours and the blocks of hours not merged yet, joined by time. The answer is a sequence of blocks of that one table; the client library turns them into a matrix. For clients without the library the server answers in CSV or JSON, limited in rows. Rows are visible to a reader as soon as their block is stored.
14. **The chart is a reduction on the server:** the requested range of one column, reduced to a minimum and a maximum per pixel column.
15. **The feed is watched through ordinary sensors** derived from block directories: rows, bytes and delivery lag.

## Consequences

- A row of five-column tick data takes about 5.75 bytes on the wire and on disk from the moment it arrives, and about 5.5 after the merge on the measured three-minute sample (header, key and index record included). With full hours to merge, the merged size is expected near 4.9 (from a scaled run; to be confirmed in the rollout step). The merge is there for the speed of reading one table, not for the size.
- Data reaches the server at most one tick after the application produced it, and is readable at once. Until its hour is merged, reading one table costs a decompression of every block of the range: 130–260 ms for an hour. After the merge it costs about a millisecond.
- The collector spends about 0.09 µs of CPU per row and needs zstd as its only new dependency. The native collector needs no port of the column coders, and no byte-identical output between .NET and C++ is required: two zstd libraries may compress the same block to different bytes, and conformance compares the decoded rows.
- The server's storage format can change with a server release alone. Its cost is the server's: about 0.5 µs of CPU per row for the merge, about 133 seconds a day for 276 M rows on one core.
- Every row reaches the disk four times: twice when its block arrives (the LevelDB log, then a table file) and twice when it is merged. All databases take keys in growing order only, so none compacts during writes. Space freed by the merge and by retention returns when the server compacts the deleted range, which cost 0.1 s and 14 MB at seven days of data.
- The merge is a background job that must survive a crash at any point: an hour is served either from its blocks or from its batches, never from both and never from neither, and a repeated run of the same hour changes nothing.
- The server decompresses client-supplied bytes on ingest. The limits on decompressed size, tables, rows and columns are part of the block format and are checked from the header before anything is allocated.
- A collector with table sensors holds two connections to the server. The limits on sessions per address count them both and have to be raised or made aware of the pair.
- A script without the collector library cannot send tables in the first version: it would have to build a block. It can read them as CSV or JSON.
- A crash of the application loses the rows of the open block: up to one tick. A clean stop loses nothing.
- A block that arrives late is stored and merged with its arrival hour, so its rows live longer than the retention period by the delay, and the batches of one sensor can overlap in time. A reader joins them by time.
- A table with few rows in an hour ends up in small batches, one per hour. They are the price of a single merge.
- Per-sensor retention is not available in the first version.
- Disk becomes a planning question. On the test feed (276 M rows a day) a year is about 500–550 GB, which the roughly 550 GB free on production cannot take together with everything else; the retention period or the disk has to be decided before the feature is switched on there.

## Alternatives Considered

- **One batch per table from the collector, packed with the column coders:** the first version of this ADR. 9.08 bytes per row on the wire and 11.88 on disk at a 10-second tick, a thousand small batches per tick, and the column coders needed in both collectors with byte-identical output in C++.
- **A block with the column coders inside:** 5.26 bytes per row, the same as the raw block, for five columns (20% smaller for three), but the coders would live in both collectors again.
- **A block with rows in arrival order and a table-id column:** 7.57 bytes per row.
- **zstd 15 in the collector:** 5.14 instead of 5.75 bytes per row, for fifteen times the CPU inside the application.
- **LZMA:** on the raw block 4.06 bytes per row, but 9–15 µs of CPU per row inside the application and reads of unmerged data ten times slower. For stored batches it gains about one percent and decodes 5–7 times slower.
- **A long tick or a long time limit instead of the merge:** the simplest server, but the data is late by that limit; the owner wants it within seconds.
- **No merge at all:** blocks are already compact, but reading one table for a day would decompress 8,640 blocks.
- **A cascade of merges, as VictoriaLogs does:** each level writes the data again; one merge per hour already gives a typical table full batches.
- **HTTPS requests for blocks:** works, one request per tick, but a continuous stream fits a persistent connection better, and it would be the one place where a collector on a session used HTTP.
- **Binary frames on the existing session connection:** one connection for everything, but a large block would hold back values and commands.
- **Parquet as the stored format:** readable by standard tools, but 50–110% larger for per-table batches and a heavy dependency.
- **LevelDB under the key `sensor, first time`:** no index needed, but 14.4 times the writes at seven days and 42 s with 9.2 GB rewritten to free six deleted hours. Under `hour, sensor, first time`: 5.6 times.
- **One LevelDB database per hour:** 1.9 times the writes and space back at once, but 226 databases, 904 open handles and 4 s to open after seven days.
- **Append-only hour files, with their own index or with addresses in LevelDB, and SeaweedFS:** rejected by the owner: one store, LevelDB.

## References

- Measurement and proposal: `docs/initiatives/table-sensors.md`
- Epic #1842, steps #1843–#1850
- ADR-0017 (collector session), ADR-0019 (session as the default transport): point 7 adds a second connection of the same protocol
- ADR-0020 (sealed sensor history): the column coders and the versioning rules reused by the stored batch
