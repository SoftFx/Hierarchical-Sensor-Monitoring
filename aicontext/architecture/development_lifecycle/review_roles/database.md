# Database And Storage Review Roles

## Storage Reviewer

Focus:

- LevelDB/LMDB key design, prefixes, serialization format, snapshots, journals, intervals, and environment databases;
- backwards compatibility with existing on-disk data;
- retention, compaction, cleanup, and bounded storage growth;
- atomicity and partial-write behavior around sensor values, history, and metadata.

Must read:

- changed files under `src/database/HSMDatabase*` and `src/server/HSMServer.Core` storage/cache users;
- formatters, `DbKey`, prefix constants, database interfaces, and migrations/conversion code if present;
- tests under `src/tests/HSMDatabase.LevelDB.Tests` and database tests in `HSMServer.Core.Tests`.

Output:

- corrupt-read/write, incompatible key, or legacy-data risks;
- query/scan shape risks such as unbounded range scans or missing prefix isolation;
- rollback and migration concerns;
- missing tests with existing data, malformed data, and boundary timestamps.

---

## Cache / Snapshot Reviewer

Focus:

- server tree state snapshots, values cache, update queues, table of changes, and cache invalidation;
- consistency between live sensor state and persisted history;
- recovery after restart, partial failure, or delayed queue processing.

Must read:

- changed cache, snapshot, update queue, and monitoring core files;
- tests under `TreeValuesCacheTests`, `UpdatesQueueTests`, `MonitoringCoreTests`, and related database tests.

Output:

- stale cache, lost update, duplicate update, or restart recovery bugs;
- ordering and idempotency risks;
- tests needed for restart, concurrent update, and queue drain scenarios.
