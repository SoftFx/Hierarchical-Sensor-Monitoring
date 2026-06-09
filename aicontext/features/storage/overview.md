# Storage Features Overview

> Owner: storage | Last reviewed: 2026-05-28 | Canonical: yes

The storage area owns LevelDB/LMDB-backed persistence, key formats, value
serialization, snapshots, journals, interval data, retention, and restart
recovery behavior.

## Feature Folders To Add Here

- `leveldb/` - database implementation, native dependencies, prefix layout.
- `sensor-values/` - value persistence and history retrieval.
- `snapshots/` - tree/current-state snapshots.
- `journals/` - journal value storage and retrieval.
- `cache-recovery/` - restart/cache rebuild behavior.

Create folders from `../_TEMPLATE_feature.md` as work lands.
