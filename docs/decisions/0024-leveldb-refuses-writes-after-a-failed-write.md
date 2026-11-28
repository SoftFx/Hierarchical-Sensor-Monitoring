# ADR-0024: A LevelDB database refuses writes after a failed write until its write-ahead log is rolled

**Status:** Accepted
**Date:** 2026-11-28
**Supersedes:** —

---

## Context

#1791: after the volume of `Databases` ran out of space and the space came back, the server answered `200` to writes
that the next start dropped. Proved record by record on a tmpfs with LevelDB 1.23: the write that hit `ENOSPC` left
656 of its 942 bytes in the write-ahead log, LevelDB's log writer counted the whole record anyway (it moves its block
offset on whether the append succeeded or not) and did not stop the database, the next writes were appended after the
torn record and succeeded, and the recovery of the next open dropped them with the rest of the torn record's block.
Stock LevelDB stops a database only after a failed `fsync` of the log or a failed background flush (a "background
error" every later write fails with until the database is opened again); a failed append is reported to that write
alone. This hits every LevelDB database of the server, synced writes included.

The fault cannot be cured by reopening the database in the running server: a close under a running read crashes the
process natively, and only the history weeks are guarded against that (#1958); the other databases are #1961.

## Decision

Every write of every LevelDB database of the server goes through a **write guard** of that database
(`LevelDbWriteGuard`):

- The guard serializes the writes of its database.
- After a native write fails, the database **refuses writes** (`LevelDbWritesRefusedException`, nothing written) until
  the log the failure may have torn is closed.
- It is closed by a **roll**: a manual compaction of a range that holds no key, which makes LevelDB start a new log and
  write the memtable into a table, after which LevelDB deletes the old log. The guard accepts writes again only when no
  log numbered at or below the torn one is left in the directory (LevelDB reports nothing from a manual compaction).
- The roll is tried right after the failure and then by a write at most once a second, and only when the volume has
  room for the memtables (best effort: each database checks alone): a roll whose flush fails gives LevelDB a background
  error, which only a reopen (a restart of the server) clears.
- The direct writes of the LevelDB wrapper are banned in the store projects (analyzer error), so a new store cannot
  skip the guard.
- Failed writes, refused writes and the databases refusing writes now are counted and posted by the self-monitoring
  (`Database/Write failures`, `Database/Refused writes`, `Database/Databases refusing writes`).

## Consequences

- No write is acknowledged that a restart would drop: a restart drops only the failed write's torn record.
- A database whose write failed on a full disk takes writes again by itself once the space is back (the next write's
  roll), without a restart. A database in which LevelDB itself holds a background error (its own flush failed on the
  full disk) still needs a restart; it is now visible as an Error sensor and logged.
- Concurrent writes to one database no longer share one log record (LevelDB's group commit): one native write at a time.
  LevelDB already queued them; the cost is the merging of concurrent writes (synced or not) into one log append and one
  `fsync`. The history weeks are written mostly from the update queue; many concurrent writers of one
  database are rare (table block puts).
- Callers keep their own contract for a failed write (a registration answers `500`, #1756; a table block is not
  acknowledged). A refused write is a `ServerDatabaseException` like any store failure.
- No storage format, key or file changes; a rollback to the previous server is safe.

## Alternatives Considered

- **Reopen the database after a failed write**: clears a background error too, but needs a gate and drain of every read
  and iterator of every database (#1961), which only the history weeks have.
- **Refuse writes until a restart, without a roll**: simpler, but every disk-full episode, however short, would need a
  restart.
- **Roll right away, without the free-space check**: on a full disk the roll's flush fails, which turns the episode into
  a background error and a restart, the very state #1791 saw in the history database.
- **Write everything synced**: a synced write that fails in its append tears the log the same way (LevelDB stops the
  database only when the `fsync` fails).

## References

- #1791 (the bug, with the tmpfs repro), #1756 (registration answers `500` on a store failure), #1958 / #1961 (close
  under a running operation), #1986 (node writes that swallow store failures, found on the way).
- `aicontext/features/storage/write-faults/feature.md`.
