# ADR-0020: Sensor history of closed weeks is sealed into chunks with a codec of our own, on top of LevelDB

**Status:** Accepted
**Date:** 2026-11-19
**Supersedes:** —

---

## Context

Sensor history is the largest part of an HSM database. Every value is one MemoryPack blob under the key `sensorId | ticks` in a weekly LevelDB database. On a measured production shard that is 122 bytes on disk per value.

The measurement in `docs/initiatives/sensor-history-storage.md` compared the alternatives on that shard:

- Compaction alone brings a closed week to 43 bytes per value.
- DuckDB, VictoriaMetrics with VictoriaLogs, ClickHouse, TimescaleDB and QuestDB each lose on at least one thing the server does all the time: single-value writes and rewrites of an existing key (16–924 rows/s against 405,188 for LevelDB), reads of one sensor (13–61 ms against 8 ms today), or range deletes for one sensor.
- A chunk per sensor encoded with a time-series codec, stored in LevelDB, reached 9.5 bytes per value lossless and 7.5 with `ReceivingTime` at millisecond precision, and read a week of one sensor in about 1 ms.
- Closed weeks in Parquet with standard encodings reached 11.1 bytes per value at the same precision and 16–22 ms per one-sensor read, and need the same sealing job, merged read and retention rewrite around them.

Constraints that shape the decision:

- Storage key formats and serialized storage formats are compatibility-sensitive (`CLAUDE.md` → Compatibility Rules).
- Sensor history is not part of the backups the server takes. A defect that damages it is permanent.
- The same key is rewritten in normal operation (aggregated runs, bar updates, late values), and retention and "clear history" delete a time range of one sensor.
- Everything above the storage layer reads a week of history through `ISensorValuesDatabase`.

## Decision

1. **Current data does not change.** The current and recent weeks stay one row per value in `SensorValuesV2_<from>_<to>`.
2. **A closed week is sealed.** The rows of each sensor are encoded into chunks and stored in a sibling LevelDB database `SensorValuesSealedV1_<from>_<to>` under the key `sensorId | firstTick`. A chunk holds a bounded number of rows of one sensor and starts with a header: format version, encoder revision, value type, row count, first and last tick, checksum.
3. **The codec is ours; the compressor is not.** Columns are encoded separately (delta-of-delta for ticks, decimal-scaled integers with a per-chunk choice of raw, first or second difference for doubles, with a fallback to XOR with the previous value). Strings are a dictionary per column, or a template per distinct shape with every number in them (an ASCII digit run, optionally with one `.` and a sign) stored in integer columns. The result is compressed with zstd from a library.
4. **Sealed data is exact except for `ReceivingTime`.** It is stored as the difference from `Time` rounded to a millisecond; `Time`, which is part of the key, and every other field are bit-exact. Values are not rounded.
5. **The row database of a sealed week stays as its overlay.** Anything written to that week after sealing — a late value, an edit, the result of a sensor-move merge — is an ordinary row there. A read merges chunks and overlay in key order; on an equal key the overlay row wins.
6. **A prefix delete marks; any other range delete rewrites.** Both remove the overlay rows in the range. A delete of a prefix of one sensor's history in the week (retention, clear history, sensor removal) removes the chunks that lie wholly at or below its cutoff and records a per-sensor delete watermark when a chunk straddles the cutoff; chunk rows at or below the watermark are deleted, and a later fold or prefix delete removes them physically. Any other range removes the chunks that lie wholly inside it and re-encodes the chunks on its boundary. *(Amended 2026-11-20, #1805: the first rollout showed hourly retention re-encoding the whole oldest kept week every hour.)*
7. **Some data is never sealed.** File sensor values stay rows. So does any sensor with a row the codec cannot represent exactly: the encoder refuses the input instead of writing an approximation, and the refusal is counted.
8. **Sealing verifies before it deletes, and deletes late.** The job decodes every chunk it wrote and compares it with the source rows field by field. The source rows are then kept for a configured number of weeks; when that period ends each row is compared with its chunk again and deleted only if they still match. Existing history is sealed the same way, oldest week first.
9. **The format is versioned and fails loudly.** A chunk with an unknown version or a bad checksum is an error, never an empty result. A change to the bytes written for the same input requires a new format version or a new encoder revision (point 12), and old versions keep decoding.
10. **Sealing is off by default** until the rollout step decides otherwise, and an unseal operation turns a sealed week back into rows.
11. **The seam is `ISensorValuesDatabase`.** A sealed week is a second implementation of it; nothing above `DatabaseCore` changes.
12. **Format version and encoder revision are separate.** The header carries both. The format version changes only when a reader of the current version could not decode the new chunks correctly (a new column encoding, a changed layout, a changed meaning); a reader refuses an unknown format version and decodes every older one forever. The encoder revision changes whenever the encoder's output bytes change but stay decodable by the same format version (a better choice among existing encodings, other parameters, a compressor upgrade); it records which encoder wrote a chunk, and readers ignore it. Golden chunks of every released encoder revision stay in the repository as decode tests forever; only the current revision's set pins the exact bytes the encoder writes.

Independently of all of the above, closed weekly shards are compacted automatically.

## Consequences

- History of closed weeks shrinks from 122 to about 7.5 bytes per value on the measured data, and a one-sensor read of such a week gets faster, not slower.
- Strings that are numbers printed as text are stored as numbers: on a production week where String sensors held two thirds of the sealed bytes, the template encoding took the whole week from 10.6 to 7.7 bytes per value (with zstd level 15). The decoder must bound what such a column expands to, because template rows are built, not shared.
- HSM owns a storage format. It needs golden-file tests that pin the bytes, a test that fails when a value type gains a field the codec does not cover, and a format-version bump for every change a reader of the current version could not decode.
- An encoder improvement ships without a format bump: it bumps the encoder revision and adds a golden set beside the old ones, and chunks written by an older encoder can be found by their header for re-encoding.
- Adding a persisted field to a value type now means changing the codec in the same change; until then sensors that use the field stay unsealed.
- `ReceivingTime` of sealed values loses its sub-millisecond part. Code that matches it exactly must not rely on sealed data; the one known case, the File value lookup, is covered by not sealing File sensors.
- An older server does not see `SensorValuesSealedV1_*`: after a rollback the history of sealed weeks is hidden, not deleted, and the overlay rows remain readable. Unsealing before a rollback restores it, with the rounded `ReceivingTime`.
- An older server also deletes without knowing about chunks: retention and "clear history" run by it remove overlay rows only, so the chunks would bring that history back after the next upgrade. A rollback therefore unseals first; rolling back with sealed weeks in place is supported for reading, not for a return to the newer version.
- While source rows are kept, a sealed week takes more space than before, not less. The gain arrives when the keep period ends.
- Values still cross the storage interface as MemoryPack bytes, so a sealed read decodes a chunk and re-serializes each value. It is cheap next to the decode and is the price of leaving the cache untouched.
- A read of a sealed week decodes whole chunks; the chunk size bounds that cost and is fixed with the codec.
- Retention on sealed data costs a few key deletes and one small watermark write per sensor, nothing re-encoded; a sensor keeps at most one partly deleted chunk per week until a fold or a later prefix delete removes it. Other range deletes re-encode at most two chunks per sensor per range. The watermark is a new kind of key in the chunk database: a server built before #1805 reports it as a corrupt chunk, which matters only for a downgrade.

## Alternatives Considered

- **Closed weeks in Parquet with standard encodings:** lower risk in the encoder, but 11.1 against 7.5 bytes per value, 16–22 ms against 1 ms per one-sensor read, a reader dependency, a memory-hungry seal, and the same sealing job, merged read and retention rewrite. Raised by the owner as the low-risk option and not chosen.
- **ClickHouse, TimescaleDB, QuestDB:** an external server was acceptable to the owner, but each is larger at the same precision and needs a write buffer for single-value writes; QuestDB cannot delete rows.
- **VictoriaMetrics with VictoriaLogs:** a value becomes many series; with every field kept it is larger, rounds doubles, drops rows that share a millisecond, and has neither "last write wins" nor a range delete.
- **DuckDB as the history store:** single-row writes and rewrites about a thousand times slower, deletes that do not return space.
- **Compaction only:** kept as an independent step; it does not change the format and stops at 43 bytes per value.
- **Chunks in the same LevelDB database as the rows:** existing range scans compare keys by length first and would stop or misorder on a chunk key inside a sensor's row range; a sibling database keeps the two key spaces apart.
- **Rounding values in the first version:** 5.5 bytes per value at three significant digits, but irreversible and in need of a per-sensor setting. Deferred by the owner.
- **Deleting source rows right after verification:** less disk during the transition, no way back from a defect the verification missed. The owner chose to keep them for a period.

## References

- Epic #1800; steps #1801 (compaction), #1802 (codec), #1803 (sealed store), #1804 (sealing job), #1805 (rollout)
- Initiative and measurement: `docs/initiatives/sensor-history-storage.md`
