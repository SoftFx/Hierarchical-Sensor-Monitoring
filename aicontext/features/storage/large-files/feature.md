# Feature: Large file store

> Owner: storage | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: where and how the server keeps the content of large file values (ADR-0022 points 1, 7, 8, 11, 12, 16; #1883, step 1 of epic #1882): the large value of a File sensor, the store's files and records, the order of an append, leases, limits and the free-space reservation, the check at open and at start, the store's own pass, and what later steps build on. The file connection is #1884; the archives, the password and the download are #1885; the sensor page, the alert variables and the rest of the self-monitoring are #1886.

---

## Overview

A **large file value** is a value of a File sensor whose content is not part of the value. The row in the sensor's history holds the description of the file (name, size, hash, the modification time of the source file) and the id of its file; the content lies in the **large file store**, a folder of the server of its own (it may be another volume). Ordinary file values do not change.

Two parts:

- `LargeFileStore` (`src/database/HSMDatabase.LevelDB/LargeFiles/`, behind `ILargeFileStore` in `HSMDatabase.AccessManager/LargeFiles/`): the files, the records, the in-memory index, leases, reservations, the check at open, the pass of its own (abandoned and orphan parts, leftovers), the archive calls. It knows ids, never sensor paths. It is not LevelDB; it sits next to `DurableFile`, whose durable rename it uses. `IDatabaseCore.LargeFiles` holds the one instance; `DatabaseCore.Dispose` closes it.
- `LargeFileUploadService` (`src/server/HSMServer.Core/LargeFiles/`, behind `ILargeFileUploads`): what the file connection (#1884) calls. It resolves the sensor, keeps the store's rules, and on a verified finish writes the value through the product's update queue (`TreeValuesCache.LargeFiles.cs`). It also runs the pass that compares the records with the sensors and their rows (`RunPassAsync`), started by `LargeFileMaintenanceService`.

Nothing is created on a server that never takes a large file: the folder appears with the first begin that is not refused.

## The value

`LargeFileValue : FileValue` (`src/server/HSMCommon/SensorValues/SensorValues.cs`), registered as `[MemoryPackUnion(12, typeof(LargeFileValue))]` on `BaseValue`:

| Member | Meaning |
|---|---|
| `FileId` | The id of the file in the store, a GUID the server makes. Never an archive id: a pack or a re-encryption never rewrites history. |
| `Hash` | SHA-256 of the original (decompressed) content, 32 bytes: the checker's `ContentHash`. Not the hash the collector sends at the end, which is only an input of the check. |
| `SourceModified` | The modification time of the source file, UTC. |
| `Name`, `Extension` | The original name as the collector named it (shown, an archive entry name, never a path on the server) and its extension without the dot. |
| `Value` | Always empty. |
| `OriginalSize` | The size of the file. |
| `Time`, `ReceivingTime` | The time the server accepted the file, unique within the sensor (below). |

The state of the file (uploading, verified, packed) is **not in the row**: it lives in the store's record only.

- **Ordinary file rows keep their bytes.** Adding members to `FileValue` would have made every new file row unreadable by an older server (MemoryPack's member count); a derived record under a tag of its own leaves `FileValue` as it was. Pinned by `LargeFileValueTests.An_ordinary_file_value_serializes_to_the_same_bytes_as_before_the_large_value` (a fixed hex taken before the type existed). `with` keeps the runtime type.
- **Recognised without deserializing:** a stored row whose first byte is 12 (`LargeFileValue.IsLargeFileRow`) holds a large value.
- **Never deflated or inflated.** `SensorValueExtensions.CompressContent` / `DecompressContent` pass a large value through untouched (`Value` is empty and `OriginalSize` is not its length), so `FileValuesStorage`, the out-of-order write and the history converters never touch it. A history read and the in-memory last value carry the description only.
- **Never sealed.** It is a `FileValue`: the sealing job skips File sensors and the chunk encoder refuses it (`RefusalTests.Large_file_values_are_file_values_and_never_sealed`).
- **A rollback to a server without the type** loads the tree and leaves the rows untouched: such a server fails only on a tag-12 row. A sensor whose newest row is a large value loads without a value (`BaseSensorModel<T>.LoadHistoryUnderLock` catches), a history read of it throws, other sensors and ordinary file rows are unaffected. Back on a server with the type, everything reads again.

### The time of a large value

The time is the server's acceptance time, chosen on the product's queue when the file is verified, before the record is marked Verified: now, but at least one tick above the sensor's history floor (the KeepHistory cutoff or the last clear: a value below it is never stored, and the pass would take its file for one removed by retention). It is not pushed past the sensor's newest value: a value dated in the future by a skewed clock would carry every later file into the future; a large value older than the newest is stored as a late value (the read-back writes its row when the add did not). And when a row (point read of the key) already holds that tick, the next free tick (at most 1,000 ticks later). A value's key is the sensor and the time, so two files never share one; and a file older than the history period is not removed right after its upload. The source file's time is `SourceModified`. Test seam: `TreeValuesCache.LargeFileClock`.

### The web UI

- The file actions of the sensor page (`HomeController` region "File"): `GetFile` and `GetFileStream` answer **404** for a large value, never the empty bytes as a file; `GetFileInfo` and the list of recent files show its description. The download is step 3's.
- "New value" on a File sensor whose last value is large builds a plain `FileValue` with the name and extension (a copy of the large value would be a second row pointing at the same file); "change last value" keeps the large value as it is (the same row, the same file).

## On disk

```
<LargeFiles.Folder>/uploads/<productId:N>/<fileId:N>.part          the stored pieces as received, appended in order
<LargeFiles.Folder>/uploads/<productId:N>/<fileId:N>.idx           per piece 44 bytes: u32 stored length, i64 offset, 32-byte digest (little-endian)
<LargeFiles.Folder>/uploads/<productId:N>/<fileId:N>.rec           the record (below)
<LargeFiles.Folder>/uploads/<productId:N>/<archiveId:N>.zip.tmp    an archive being built (#1885), on the same volume as archives/
<LargeFiles.Folder>/archives/<productId:N>/<archiveId:N>.zip       finished archives only: the subtree Caddy will see (#1885)
```

GUIDs in the `N` format (32 hex digits). Nothing a client sent is part of a path. **The `archives/` directory itself is never deleted, renamed or recreated** (nor the store's folder replaced): step 3's compose bind-mounts it into Caddy read-only, and a replaced directory would leave the mount on a stale one. The store creates it when it is missing and removes only empty `archives/<productId>/` subfolders (pinned by `LargeFileStoreTests.The_archives_folder_itself_is_never_removed_or_replaced_only_its_empty_product_folders`). The `.rec` stays in `uploads/` after packing: it is the file's state. The folder is `IDatabaseSettings.LargeFilesFolder` (`PathToLargeFiles`), set from `LargeFiles.Folder`: not under `Databases` (it is not a database, is not part of `TotalDbSize` and is not backed up; sensor history is not backed up either).

### The record (`.rec`, version 1)

One JSON object (`LargeFileRecordFormat`), written whole by `DurableFile.WriteAtomic` with `strict`: a temporary `<fileId>.rec.tmp`, flushed to disk, renamed over the record (write-through on Windows, a directory sync on Linux), a failed sync a fault. Members: `version` (1), `fileId`, `productId`, `sensorId` (`N` format), `name`, `extension`, `size`, `sourceModifiedUtc`, `sourceIdentity`, `clientName` and `remoteAddress` (where the file came from: the session that began it; optional, a record without them reads with null), `secure` (the mark below; a record without it reads as secure: fail closed), `status` (written only when it is not Ok; a record without it reads as Ok) and `comment`, `pieceSize`, `pieceCount`, `partLength`, `hash` (lowercase hex), `state` (`Uploading`, `Verified`, `Packed`), `createdUtc`, `lastPieceUtc`, `valueTimeUtc`, `archiveId`, `previousArchiveIds` (every replaced archive not deleted yet), `passwordVersion`, `archiveLength`, `archiveHash` (times as UTC round-trip strings; members without a value are left out). Unknown members are ignored; another version is refused (the record is then reported and its files kept).

### In memory

At its first use the store reads every record into an index by file id and by **upload key** `(ProductId, SensorId, Name ordinal, Size, SourceModifiedUtc, SourceIdentity ordinal)`; a null source identity matches only a null one. Leases are in memory only; reservations come from the records, so they survive a restart.

## Begin

`Begin(key, extension, pieceSize, maxPieceSize)`, refusals in this order:

| Outcome | When |
|---|---|
| `TooLarge` | the size is negative or above `LargeFiles.MaxFileSizeBytes` (final) |
| `BadPieceSize` | the piece size is outside 65,536 .. the caller's `maxPieceSize` (final for the request) |
| `AlreadyStored` | a record of the key is Verified or Packed: nothing to send |
| `Checking` | the end check of the file runs (`RetryAfter` 10 s) |
| `Busy` | the part has a live lease (`RetryAfter` = what is left of it) |
| `TooManyUploads` | live leases of uploading records = `LargeFiles.MaxUploadsInProgress` (`RetryAfter` 30 s); a continue counts too |
| `TooManyParts` | a new upload only: the product has `LargeFiles.MaxUploadingPartsPerProduct` uploading records (`RetryAfter` 5 min) |
| `NoRoom` | a new upload only: free space − `MinFreeDiskMb` − reservations < its own reservation, 2 × size + 1 MiB (`RetryAfter` 10 min); counted in `Refused for space`, a Warn line |
| `Continue` | a part of the key exists with the same piece size: a new lease, `NextPiece` = the record's piece count |
| `Started` | otherwise: a new file id; the part and the index are created empty and flushed, then the record (so a record always has its files) |

The begin may name where the file came from (`ClientName`, `RemoteAddress`, optional members at the end of `LargeFileBeginRequest`): kept in the record of a new upload, a continue keeps the record's. **The secure mark** (`LargeFileBeginRequest.Secure`, true by default; `LargeFileRecord.Secure`): the application marks each file at the send as secure (a dump: its archive is encrypted under the product's dump password, #1885) or not (the same zip without a password). It is kept in the record of a new upload and never changed, and it is part of the upload key: a begin of the same file with the other mark is another upload (a new file id from piece 0; the old part expires as any abandoned part). Every answer that names a record reports its mark (`LargeFileBeginResult.Secure`). A file that is not secure is verified, listed (`GetFiles(Verified)`, `FileVerified`) and packed by the same calls; its `PasswordVersion` stays empty. **Status and comment** (`LargeFileBeginRequest.Status`, Ok by default, and `Comment`, sanitized as every value's comment, at most 1,024 characters): what the value gets (a routed ordinary send keeps its own), kept in the record of a new upload and put into the value when it is written, also on a roll-forward. The layout, the states and the value row do not change. Every answer carries `PieceSize`: the record's piece size (the request's on `Started`, the record's on `Continue`, `AlreadyStored`, `Checking` and `Busy`, 0 for a refusal). A part of the key with **another piece size** is removed (logged, counted in `Parts removed`) and the begin starts a new file id: nothing of the old part can mix in. `SensorRefused` is the service's (below). The piece count is `ceil(size / pieceSize)`; a file of 0 bytes has no piece and may finish at once.

**Free space.** `free − LargeFiles.MinFreeDiskMb − reservations ≥ the new upload's reservation`. From its begin until its first `CommitArchive` an upload reserves what its part still needs (announced size − bytes in the part) plus room for its archive (announced size + 1 MiB, `LargeFileStore.ArchiveSlack`); after the first commit nothing. A re-encryption reserves the current archive's length + 1 MiB from `BeginArchive` until its commit or abandon (in memory: a restart drops it with the temporary file). Reservations are counted under the store's lock, so two begins cannot pass on the same free bytes, and they come from the records, so they survive a restart. The free space is of the volume of the store's folder (on Linux the nearest existing folder, so a mounted volume counts; on Windows the drive). Test seam: `FreeBytes`.

**Leases.** A lease is live until `LargeFiles.LeaseTimeoutSeconds` (180) pass without an append on a monotonic clock (`Environment.TickCount64`, seam `MonotonicMs`: a step of the wall clock neither ends nor prolongs a lease), and while the file is being checked whatever the time. A lease never outlives its upload's Uploading state: `MarkVerified` and a discard end it, and a finish whose check began ends it whatever came of it (verified, mismatch, sensor gone, a fault or a cancellation of the check), so a part is never left busy; only live leases of uploading records count as uploads in progress. `Release` ends one at once whatever the state; a stale lease is ignored everywhere. A restart frees every lease.

**A part that received no piece goes with its lease**: on `Release`, on the expiry of its lease (removed by the next begin or pass), and at a restart (removed at open). It is logged at Info and counted in `Parts removed`; it is not a disagreement.

**Parts per product.** A product holds at most `LargeFiles.MaxUploadingPartsPerProduct` (16) uploading records: a new upload beyond them answers `TooManyParts`, a continue is not refused.

### The caller's contract

Broken bounds are the caller's error (`ArgumentException`), not a refusal: the name has 1 .. 1,024 characters (`LargeFileUploadService.MaxNameLength`), the source identity, the client name and the remote address at most 256 each (`MaxSourceIdentityLength`, `MaxOriginLength`), the comment at most 1,024 (`MaxCommentLength`), the status a defined `SensorStatus`, a piece digest and the whole hash 32 bytes, a stored piece 1 .. `LargeFileStore.MaxStoredPieceBytes(MaxPieceSize)` bytes (the zstd bound of the maximum piece size, `ZSTD_COMPRESSBOUND`). The piece size outside 65,536 .. `MaxPieceSize` is a refusal (`BadPieceSize`).

## The order of an append

`Append(lease, n, storedBytes, digest)` answers `Discarded` (no such upload), `LeaseLost` (not the live lease, or the file is being checked), `AlreadyHave` (n below the next piece), `TooMany` (n at or past the piece count), `OutOfOrder` (n above the next piece), or writes, in this order, and returns only then:

1. The part is cut to the record's length (a failed append before may have left bytes), the bytes are written at its end and the part is flushed to the disk (`Flush(true)`).
2. The 44-byte index entry is written at `n × 44` and the index is flushed.
3. The record is written (piece count, part length, last piece time).

A stored length of 0 or above the zstd bound of the caller's maximum piece size (`ZSTD_COMPRESSBOUND`, `LargeFileStore.MaxStoredPieceBytes`), or a digest that is not 32 bytes, is the caller's error (`ArgumentException`). A write that fails is `LargeFileStoreException`: nothing is acknowledged, the record and memory are unchanged, the piece is sent again. The store does not interpret the bytes (the file connection decompresses and checks them).

Appends of one file are serialised by the file's own lock. The store's lock is not held across the writes of an append, of a new upload (its entry is registered pending under the lock, its part, index and record are written outside it, then it is published or taken back), of a verify, a removal or an archive commit (the pass leaves a product's folders alone while it has a pending upload or a build). What does hold it: the first open of the store (reading every record, and the repairs of the check at open: cutting a part back, rewriting its record, deleting an empty part or a temporary record), once per process and in practice at start; and the removal of an empty product folder. A failed open is answered again for a minute without a new attempt, and counted once.

### A cut at every point

| The process stops | After a restart |
|---|---|
| before or during the part write, before its flush | the part is longer than the record or not: cut back to the record; the piece was not acknowledged and is sent again |
| after the part flush, during or after the index write | the part and the index are longer than the record: both cut back; the piece is sent again |
| after the index flush, before the record | the same |
| after the record, before the answer | the record covers the piece: `Continue` answers the next one |

Pinned by `LargeFileStoreTests.A_cut_at_any_point_of_an_append_...` (fault hooks at the named points, then a reopen) and by `LargeFileStoreKillTests`: a child process (`src/tests/HSMDatabase.LargeFiles.CrashHost`) appends pieces of 1 MiB through the real store and prints each acknowledged piece; the test kills it six times at random moments (most kills land inside a piece: bytes past the record were found), opens the store and checks every acknowledged piece byte for byte and the part cut back to its record, then lets it finish. **A kill, not a power loss:** the operating system still writes what the process had handed it.

## Finish

`StartFinish(lease)` answers `Incomplete`, `LeaseLost`, `Discarded` or `Checking`, or marks the file as being checked and returns a reader of its pieces (`ILargeFilePartReader`: in order, bytes and digest, boundaries from the index). `EndFinish` clears the mark and ends the lease, whatever the result. The service (`FinishAsync(lease, wholeHash, checker)`):

1. The sensor must still be a File sensor of the record's product and not blocked, else the upload is discarded: `SensorGone`. An upload removed meanwhile (its sensor removed, which takes its files at once) answers `LeaseLost` ("the upload is gone"); the next begin is refused for the sensor.
2. The checker reads the pieces (`ILargeFilePartChecker.CheckAsync` → `LargeFilePartCheck(Matches, ContentHash, FirstBadPiece, Defect)`; every defect of the content is `Matches = false`, only an I/O fault throws; step 2 supplies the production one; the tests use `StoredBytesHashChecker`, SHA-256 over the stored bytes as both the comparison and the content hash, which lives in `HSMServer.Core.Tests`). `IsChecking(fileId)` (store and `ILargeFileUploads`) tells a file "being checked" from the start of the finish to its end. No match: the upload is discarded, `Mismatch` with the `Defect` as its error. A cancelled or faulted check changes nothing on the disk (the record stays uploading with every piece; the lease ends, so the next begin continues it).
3. On the product's queue (`LargeFileValueRequest`, never cancelled once queued): the sensor is checked again, the value time chosen, **the record written as Verified with the checker's `ContentHash` and the value time** (`MarkVerified`, durable, ending the lease), then the value added as any value is added (its checks, its row: `TryAddValueWithDeliveryStamp` and `PersistAddedValue`), and its alerts delivered where an ordinary new value's are (`SensorUpdateViewAndNotify`): **at most once per file**. A sensor whose own checks do not take it (a singleton gate) gets the row written directly, with a Warn line: a verified file must have its value. The row is read back (the value write reports nothing, and the add may skip it: a late value below the floor, a value inside an aggregated span); when it is missing it is written directly and read back again. A roll-forward (below) is shown (`SensorUpdateView`) and never alerted; so a crash between the verified record and the notify means that file is never alerted: its value appears through the roll-forward without an alert.
4. `Verified` is answered only then, with the file id and the value time, and `FileVerified` is raised. A row that could not be written is a fault (`LargeFileStoreException`, `Write failures`): the record is Verified, so a begin answers `AlreadyStored` and the pass writes the row again from the record.

A second finish while the check runs answers `Checking` (an addition to the outcomes of the plan, see "What later steps must do"). `Incomplete` keeps the lease: no check ran, and the connection sends the missing pieces next.

## Discard and removal

`Discard(lease, reason)` removes the part, the index and the record, releasing the lease and the reservation; logged (Warn) and counted (`Parts removed`). `Remove(fileId, reason)` removes every file of a record (part, index, archive, the archives re-encryptions replaced) and then the record; refused while the file is being checked. An uploading record counts in `Parts removed`, a verified or packed one in `Files removed`; each removal is a Warn line naming the file, the sensor, the product and the reason.

## The pass

`LargeFileMaintenanceService` runs it once at start (after the tree is built: the cache builds it when it is constructed, before hosted services run) and then every hour, below normal priority. It works from the records and point reads of rows, never by scanning history (except the start check of rows, below).

1. **The store's own part** (`RunStorePass`, no value looked at):
   - a part that received no piece and whose lease expired is removed (Info);
   - an **uploading** record whose last piece is older than `LargeFiles.AbandonedUploadDays` (7), with no live lease and not being checked, is removed (`Parts removed`, Warn). A verified or packed file is never removed for age.
   - a `.part` / `.idx` without a record is kept until its newest file is that old, then removed (`Parts removed`, Warn); a part whose record exists but cannot be read is never taken for one.
   - the part left by a pack and the archives re-encryptions replaced (`previousArchiveIds`, the record rewritten without them) are removed; so is the `.zip.tmp` of a build not running in this process (Info, `Leftovers removed`);
   - an archive file no record names (a crash between its rename and its record) is removed once it is older than 24 hours (Warn, `Leftovers removed`); never in a product with a record that cannot be read, which may name it; never in a product that has no record at all (its `uploads/` lost, a folder put back, a store opened on another folder: kept and reported once); and never at all once the start check found values naming files the store has no record of (`KeepOrphanArchives`: such an archive may be the only copy of one).
   - empty product folders are removed.
2. **Records against sensors and rows:**
   - a record (any state) whose sensor is gone is removed: the backstop of the removal with the sensor (below). Gone is decided by evidence only: the sensor is not in the tree and a read of the environment database that succeeded did not find the key of its entity (`IDatabaseCore.SensorEntityExists`: an entity that cannot be deserialized still exists). A sensor the tree did not build at start, an unreadable entity, a removal that failed after the sensor left the tree, or a read that failed ("cannot tell"): the files are kept, reported (Warn, `Files of sensors not loaded`), never removed for that. One read per sensor and pass;
   - for a verified or packed record, on its product's queue (`LargeFileCheckRequest`, so a retention pass, a clear or a finish is never half-seen), a point read of the row at its value time:
     - the row is the large value of this file: kept;
     - the row is something else: **reported** (Warn, `Rows that disagree`), the file kept;
     - no row and the value time is below the sensor's history floor (the later of the KeepHistory cutoff and `HistoryClearedTo`, `TreeValuesCache.HistoryFloor`): removed (`Files removed`);
     - no row inside the kept range: **rolled forward**, the row written again from the record (the owner's decision, 2026-11-25), a Warn line and `Rows written again`. The motivating case is the crash between the verified record and its row;
     - a row that cannot be read: not decided, logged.
   - a record whose sensor is now of another product (a move that raced a begin) is reported and kept.

**A removed sensor takes its files at once.** `TreeValuesCache.RemoveSensor`, on the product's queue, right after the sensor's rows are deleted, removes every record of a File sensor (any state, uploads included): a sensor removed alone, by self-destroy, or with its product (the product removal removes each sensor through it). A file being checked is left to its finish, which then finds the sensor gone and discards it. A download never finds a file whose value is gone for longer than the removal takes; the pass is the backstop.

**A clear removes its files at once.** `ClearSensorHistory` (the UI clear and the retention pass, which runs through it) removes the files of the large values in the range it erased, logged and counted; only of those whose row a point read finds gone after the delete (the delete of rows reports nothing: a row still there keeps its file, and the pass looks again). The pass would find them too, but its floor `HistoryClearedTo` is kept in memory only: after a restart a cleared value inside the KeepHistory range would look like a missing row and be rolled forward. **Kept archives use space.** An archive no record names, in a product without records or while `KeepOrphanArchives` is set, is kept and reported and never deleted: its space stays used until an operator removes it.

**Known windows (kept as they are).** A crash between the delete of the rows of a clear and the removal of their files, then a restart: the pass finds a verified file without its row inside the kept range and writes the cleared value again (a Warn line); nothing is lost, the clear has to be repeated. And the deletes of LevelDB are not synced: a power loss after a clear can bring back rows whose files were removed; the start check reports each such value (`Start disagreements`), the value stays without its file.

## The check at open and at start

At the store's first use (every server reads the records when the pass first runs at start):

| Found | Done |
|---|---|
| a `.rec.tmp` | deleted (Info): a record write that stopped before its rename; the record before it stands |
| a record that cannot be read, or names another file or product | reported (Error, `Start disagreements`), its files kept and not used |
| an uploading record with no piece | removed (Info, `Parts removed`): its lease ended with the restart |
| an uploading part or index longer than its record | cut back to the record (Info, `Start disagreements`): the bytes were never acknowledged |
| an uploading part or index shorter than its record | cut back to the last whole piece by the index (entries whose offsets chain and whose bytes are in the part), the record rewritten (Warn, `Start disagreements`); the collector sends the rest again |
| an uploading record without its part or index | the upload is removed (Warn, `Start disagreements`, `Parts removed`): it is not a value and the collector still has the source |
| a verified record whose part or index is missing or short | reported (Error, `Start disagreements`), kept: it cannot be packed or given out |
| a packed record without its archive | reported (Error, `Start disagreements`), kept |
| an archive no record names | reported (Error, `Start disagreements`), never used; the pass removes it once it is a day old |

At start, on a server that held a large file at some time (`uploads/` or `archives/` exists, or the mark `hsm-large-files-seen` in the databases' folder, written durably before the first verified record, so a store folder lost or replaced by an empty one is still compared), the service also reads the rows of every **File sensor** and reports each large value (recognised by its first byte; only those rows are deserialized) whose file has no record (Error, `Start disagreements`); the value is kept, and from then on no archive no record names is removed (it may be that file). Nothing of this is repaired silently.

## Move and merge

**A known limitation of the first version:** the refusal holds for a File sensor with any large value, secure or not (the files lie under the product's folder and the records name the product). The refusal says so: the files leave with the sensor's history period or with clear-history. Re-homing the files of a moved sensor is a follow-up.

A File sensor with **any** record in the store (any state, a part counts) is not moved to another root product, and no merge (`onConflict: merge`, or a move a collector asks for with previous paths) is made where either side has records: `SensorMoveOutcome.NotSupported` with a message like the table sensor's. Checked in `MoveSensorAsync`, `PreviewMove`, `ApplyPreviousPaths` and again on the queue (`MoveSensorUnderLock`, `AbsorbSensor`), since a begin may come in between. A move inside the product is allowed: the sensor keeps its id and the records follow it. (ADR-0022: the archives lie under the first product's password.)

## Configuration

Section `LargeFiles` of `Config/appsettings.json` (`LargeFilesConfig` → `LargeFileOptions` → `LargeFileStoreLimits`), read once at startup, written back with its defaults, validated with an error that names the key (`LargeFiles.MaxUploadsInProgress = 0 is invalid: expected 1..1024.`):

| Key | Default | Meaning |
|---|---|---|
| `Folder` | `LargeFiles` | The store's folder, relative to the working directory like `Databases` (so next to it; `/app/LargeFiles` in the container) or absolute (another volume). Must not be empty. |
| `MaxFileSizeBytes` | 68,719,476,736 (64 GiB) | The largest file a begin accepts; 1 .. 2^50. |
| `MaxUploadsInProgress` | 4 | Live leases at once; 1 .. 1,024. |
| `MinFreeDiskMb` | 10,240 | Free space a begin leaves on the volume besides the reservations; 0 keeps only the reservations. |
| `AbandonedUploadDays` | 7 | An uploading part is removed this long after its last piece; 1 .. 3,650. |
| `LeaseTimeoutSeconds` | 180 | A lease without an append for this long is no longer live; 10 .. 86,400. |
| `MaxUploadingPartsPerProduct` | 16 | Uploading records a product may hold; a new upload beyond them answers `TooManyParts`; 1 .. 100,000. |

There is no piece-size setting: the piece size is the caller's (the file connection derives it from its frame limit), within 65,536 .. its maximum. No switch: nothing happens until a file is uploaded. Docker: the `./LargeFiles:/app/LargeFiles` volume in both compose files (`aicontext/architecture/docker.md`).

## Self-monitoring

Under `Large files` (`LargeFileSensors`; steps 3 and 4 add their sensors to the same class and node), posted from the 5-minute self-monitoring sample, **only once `uploads/` or `archives/` exists** (`HasContent`): the folder alone exists on every server whose compose file mounts it. Counters since the process started, posted when they change: `Start disagreements`, `Parts removed`, `Files removed`, `Leftovers removed`, `Refused for space`, `Rows written again`, `Rows that disagree`, `Files of sensors not loaded`, `Write failures`. Every one but the three removal counters is Error in the sample in which it grew and Ok at the next sample without growth. Step 4 (#1886) adds uploads in progress, bytes stored, builds and files waiting for a password, and the alerts.

## Log lines (logger `LargeFiles`)

- Open: `Large file store <folder> opened in <ms> ms: <n> files (<u> uploading, <v> verified, <p> packed), <d> disagreements, <o> parts without a record` (Info; Warn with disagreements), written when there is anything at all.
- A start, a verify, a pack: Info, with the file id, the sensor and the sizes.
- A refusal for space: Warn with the free bytes, the reservations and the minimum.
- Every removal: Warn with the reason; every roll-forward and every row that disagrees: Warn; every disagreement at open: Info, Warn or Error as in the table above.
- A write that fails: Error with the exception (`Write failures`).
- A pass that did anything: `Large file pass: ...` (Info).

## What later steps must do

**Step 2 (#1884), the file connection.** Code against `ILargeFileUploads` (`BeginAsync`, `AppendAsync`, `DiscardAsync`, `Release`, `FinishAsync`) and the types in `HSMDatabase.AccessManager.LargeFiles`. Pass `SensorPath` as the normalized path a registration stored (byte for byte, as for table blocks) and `ProductId` as the root product of the access key; check the key's permission before. Keep one piece = one zstd frame (the part concatenates them). Decompress and check the exact piece size yourself; the store bounds the stored length by the zstd bound of `MaxPieceSize`. Supply the production `ILargeFilePartChecker`. Map: `LargeFileStoreException` → a fault (5xx-like, send again); a refusal → its answer (`TooManyParts` like `TooManyUploads`: 503 with the time); `ArgumentException` → a protocol error of the client (section "The caller's contract"). The checker returns `LargeFilePartCheck`; its `ContentHash` (SHA-256 of the original bytes) is what the record and the value keep, its `Defect` the error of `Mismatch`. `FinishAsync` may answer `Checking` (a second finish during the check); once its check began it ends the lease, whatever the outcome. `AppendAsync` blocks its caller for the durable write. Call `Release` in a `finally` when the connection ends.

**Step 3 (#1885), the archives and the download.** The store's archive calls are built and tested: `BeginArchive` → `ArchiveBeginResult` (`Started` with an `ArchiveTarget` carrying `ExpectedArchiveId`, `NoRoom` for a re-encryption that does not fit, `NotFound`, `WrongState`), `CommitArchive` → `ArchiveCommitOutcome` (a compare-and-swap on `ExpectedArchiveId`: `Committed`; `Superseded` when another build committed first, `Removed` when the file was removed during the build, both deleting the temporary file and changing nothing; only an I/O fault throws; on commit: fsync of the temporary file, durable rename into `archives/`, the record Packed with `ArchiveId`, `PreviousArchiveIds`, `PasswordVersion`, `ArchiveLength`, `ArchiveHash`, then the part or the replaced archives deleted, one that cannot be deleted staying named for the pass), `AbandonArchive`, `OpenArchive` (`FileShare.Read | FileShare.Delete`), `OpenPart`, `OpenPieces`, `GetFiles(state)`, `TryGetFile`, `ArchivePath`, the event `FileVerified` (not durable: a packer also lists `GetFiles(Verified)` at start). A record keeps its reservation (the part still to come and its archive) until its first commit.

**Step 4 (#1886).** The rest of the self-monitoring and the alert on refusals for space; the sensor page's list of large values with the state from the record.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMCommon/SensorValues/SensorValues.cs` | `LargeFileValue` |
| `src/database/HSMDatabase.AccessManager/LargeFiles/ILargeFileStore.cs` | The store's interface, record, outcomes, limits, statistics |
| `src/database/HSMDatabase.LevelDB/LargeFiles/LargeFileStore.cs` | The store |
| `src/database/HSMDatabase.LevelDB/LargeFiles/LargeFileRecordFormat.cs` | The `.rec` format |
| `src/server/HSMServer.Core/LargeFiles/LargeFileUploadService.cs` | `ILargeFileUploads`, the begin request, the options, the pass |
| `src/tests/HSMServer.Core.Tests/LargeFiles/StoredBytesHashChecker.cs` | The test checker (not in production: the file connection supplies the real one) |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.LargeFiles.cs` | The value on the product's queue, the time, the history floor, the pass's decisions, the clear |
| `src/server/HSMServer/ServerConfiguration/Sections/LargeFilesConfig.cs` | The configuration section |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/LargeFileMaintenanceService.cs` | The pass at start and hourly |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/Database/LargeFileSensors.cs` | Self-monitoring |
| `src/tests/HSMDatabase.LargeFiles.CrashHost/` | The child process of the kill test |

## Tests

- `src/tests/HSMDatabase.LevelDB.Tests/LargeFileTests/LargeFileStoreTests.cs`: nothing created before the first begin, also by refused begins; append, finish, read back, the layout and a restart; a file of 0 bytes; out of order, twice, past the last piece; the bounds of a stored piece and a digest; two uploads of one file (a continue after a restart with the same file id, `Busy` with a live lease, a lease that expires or is released, another source identity, name, size or time is another upload, another piece size starts a new file id); `Checking` during a finish and no lease expiry meanwhile; a discard; the uploads in progress, the largest file and the piece sizes; the free-space refusal with two begins on the same free bytes, the reservation through a restart and a discard; a cut at each named point of an append, then a continue; a part longer and one shorter than its record; an uploading and a verified record without their part; a damaged record; a leftover temporary record; the 7 days of an uploading part and none for a verified file; a part without a record kept and then removed; the removal of a verified file; a pack, a re-encryption under an open reader, an abandoned build, the leftovers of a crashed build, an archive without a record and a packed record without its archive; the verified event with a failing handler. After the reviews of the design: the reservation of the part and the archive; a re-encryption's reservation and its `NoRoom`; a part with no piece removed on release, lease expiry and restart; a verified upload holds no lease; `TooManyParts`; a commit superseded by another build and one after a removal; a replaced archive a reader held, removed by the pass; an archive no record names, removed after a day.
- After the review of the branch: the reservation of a verified file with a compressed part, the files of a new upload and of an archive written outside the store's lock, archives of a product without records and after unknown files kept, `HasContent`, leases on the monotonic clock, a failed open answered and counted once in its backoff, a failed commit ending its build, where a file came from (and an older record without it), `IsChecking` (`LargeFileStoreTests`); a kill paused between the part flush and the record (`LargeFileStoreKillTests.A_kill_after_the_part_flush_...`); a sensor in the database but not in the tree keeps its files, one alert per verified file and none on a roll-forward, a clock stepped back below a clear, a store that cannot be read refuses a File sensor's move only, a fault of the verified record counted once (`LargeFileUploadServiceTests`); a clear whose delete failed keeps its files (`LargeFileSilentClearTests`).
- `src/tests/HSMDatabase.LevelDB.Tests/LargeFileTests/LargeFileStoreKillTests.cs`: the kill of a child process in the middle of a piece. It runs alone (a collection with parallelization off): on Windows the child inherits the inheritable handles of the test process, LevelDB's files among them, and held the `LOG` files of the table and sealing tests running beside it, which then could not delete their folders.
- `src/tests/HSMServer.Core.Tests/LargeFiles/LargeFileValueTests.cs`: the pinned bytes of an ordinary file value, the round trip and the tag of a large one, `with`, never deflated or inflated.
- `src/tests/HSMServer.Core.Tests/LargeFiles/LargeFileUploadServiceTests.cs` (real `DatabaseCore` and cache): the sensor a begin may name (missing, another type, blocked, an unknown product, a path not normalized) and the caller's contract; nothing created by the service or a refused begin; a verified upload becomes the value without content, in the history read too, and `AlreadyStored`; a wrong hash; `Incomplete` and `Checking`; a sensor removed during the upload; two values in one tick and a tick held by a row; removal at once with the sensor (a verified file and a part) and with the product, and by the pass for a sensor dropped without it; a sensor removed during the check; the checker's content hash kept, not the sent hash; a check that throws leaves the upload continuable; a clear; retention; the pass removing below the floor and rolling forward inside the kept range; a row that disagrees; a roll-forward after a restart; a large value without a record at start; the move and merge refusal and a move inside the product; the UI's new value and change of the last value.
- `src/tests/HSMServer.Core.Tests/BackgroundServices/LargeFileOperationsTests.cs`: the configuration's defaults, an error naming the key for each value out of range and an empty folder; the self-monitoring node absent until the folder exists, counters and failures as errors.
- `src/tests/HSMServer.Core.Tests/Controllers/HomeControllerLargeFileTests.cs`: 404 for the file and its stream, the info describes the large value.
- `src/tests/HSMDatabase.SealedHistory.Tests/RefusalTests.cs`: a large value is never sealed.
