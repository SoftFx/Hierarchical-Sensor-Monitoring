# Storage Features Overview

> Owner: storage | Last reviewed: 2026-11-28 | Canonical: yes

The storage area owns LevelDB/LMDB-backed persistence, key formats, value
serialization, snapshots, journals, interval data, retention, and restart
recovery behavior.

## Feature Folders

- [`sealed-history/`](sealed-history/feature.md) - chunk format of sealed sensor
  history (ADR-0020): byte layout, fidelity rules, versioning, encoder refusals.
- [`restore/`](restore/feature.md) - restore flow.
- [`table-blocks/`](table-blocks/feature.md) and [`table-batches/`](table-batches/feature.md) - the
  stores of table sensors (ADR-0021).
- [`large-files/`](large-files/feature.md) - the large file store and the large
  value of a File sensor (ADR-0022, #1883): files in a folder of their own, not
  a database.
- [`write-faults/`](write-faults/feature.md) - what every LevelDB database does
  after a write fails (ADR-0024, #1791): no write is acknowledged into a
  write-ahead log a failed write may have torn.

## Feature Folders To Add Here

- `leveldb/` - database implementation, native dependencies, prefix layout.
- `sensor-values/` - value persistence and history retrieval.
- `snapshots/` - tree/current-state snapshots.
- `journals/` - journal value storage and retrieval.
- `cache-recovery/` - restart/cache rebuild behavior.

Create folders from `../_TEMPLATE_feature.md` as work lands.

## Related

- `docs/initiatives/sensor-history-storage.md` - measurement of alternative stores
  and codecs for sensor history, and the proposed direction (not implemented).
