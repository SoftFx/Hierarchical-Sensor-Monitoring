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
| `Hash` | The 32-byte hash of the whole file the end check verified, as bytes (the file connection, #1884, defines what it is a hash of). |
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

The time is the server's acceptance time, chosen on the product's queue when the file is verified: now, or, when the cached newest value of the sensor or a row (point read of the key) already holds that tick, the next free tick (at most 1,000 ticks later). A value's key is the sensor and the time, so two files never share one; and a file older than the history period is not removed right after its upload. The source file's time is `SourceModified`. Test seam: `TreeValuesCache.LargeFileClock`.

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

GUIDs in the `N` format (32 hex digits). Nothing a client sent is part of a path. The `.rec` stays in `uploads/` after packing: it is the file's state. The folder is `IDatabaseSettings.LargeFilesFolder` (`PathToLargeFiles`), set from `LargeFiles.Folder`: not under `Databases` (it is not a database, is not part of `TotalDbSize` and is not backed up; sensor history is not backed up either).

### The record (`.rec`, version 1)

One JSON object (`LargeFileRecordFormat`), written whole by `DurableFile.WriteAtomic` with `strict`: a temporary `<fileId>.rec.tmp`, flushed to disk, renamed over the record (write-through on Windows, a directory sync on Linux), a failed sync a fault. Members: `version` (1), `fileId`, `productId`, `sensorId` (`N` format), `name`, `extension`, `size`, `sourceModifiedUtc`, `sourceIdentity`, `pieceSize`, `pieceCount`, `partLength`, `hash` (lowercase hex), `state` (`Uploading`, `Verified`, `Packed`), `createdUtc`, `lastPieceUtc`, `valueTimeUtc`, `archiveId`, `previousArchiveId`, `passwordVersion`, `archiveLength`, `archiveHash` (times as UTC round-trip strings; members without a value are left out). Unknown members are ignored; another version is refused (the record is then reported and its files kept).

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
| `TooManyUploads` | live leases = `LargeFiles.MaxUploadsInProgress` (`RetryAfter` 30 s); a continue counts too |
| `NoRoom` | free space − `MinFreeDiskMb` − reservations < size (`RetryAfter` 10 min); counted in `Refused for space`, a Warn line |
| `Continue` | a part of the key exists with the same piece size: a new lease, `NextPiece` = the record's piece count |
| `Started` | otherwise: a new file id; the part and the index are created empty and flushed, then the record (so a record always has its files) |

A part of the key with **another piece size** is removed (logged, counted in `Parts removed`) and the begin starts a new file id: nothing of the old part can mix in. `SensorRefused` is the service's (below). The piece count is `ceil(size / pieceSize)`; a file of 0 bytes has no piece and may finish at once.

**Free space.** `free − LargeFiles.MinFreeDiskMb − reservations ≥ size`, where every record not packed reserves its announced size minus what its part holds already. Reservations are counted under the store's lock, so two begins cannot pass on the same free bytes. The free space is of the volume of the store's folder (on Linux the nearest existing folder, so a mounted volume counts; on Windows the drive). Test seam: `FreeBytes`.

**Leases.** A lease is live until `LargeFiles.LeaseTimeoutSeconds` (180) pass without an append, and while the file is being checked whatever the time. `Release` ends one at once; a stale lease is ignored everywhere. A restart frees every lease.

## The order of an append

`Append(lease, n, storedBytes, digest)` answers `Discarded` (no such upload), `LeaseLost` (not the live lease, or the file is being checked), `AlreadyHave` (n below the next piece), `TooMany` (n at or past the piece count), `OutOfOrder` (n above the next piece), or writes, in this order, and returns only then:

1. The part is cut to the record's length (a failed append before may have left bytes), the bytes are written at its end and the part is flushed to the disk (`Flush(true)`).
2. The 44-byte index entry is written at `n × 44` and the index is flushed.
3. The record is written (piece count, part length, last piece time).

A stored length of 0 or above the zstd bound of the caller's maximum piece size (`ZSTD_COMPRESSBOUND`, `LargeFileStore.MaxStoredPieceBytes`), or a digest that is not 32 bytes, is the caller's error (`ArgumentException`). A write that fails is `LargeFileStoreException`: nothing is acknowledged, the record and memory are unchanged, the piece is sent again. The store does not interpret the bytes (the file connection decompresses and checks them).

Appends of one file are serialised by the file's own lock; the store's lock is never held across a write.

### A cut at every point

| The process stops | After a restart |
|---|---|
| before or during the part write, before its flush | the part is longer than the record or not: cut back to the record; the piece was not acknowledged and is sent again |
| after the part flush, during or after the index write | the part and the index are longer than the record: both cut back; the piece is sent again |
| after the index flush, before the record | the same |
| after the record, before the answer | the record covers the piece: `Continue` answers the next one |

Pinned by `LargeFileStoreTests.A_cut_at_any_point_of_an_append_...` (fault hooks at the named points, then a reopen) and by `LargeFileStoreKillTests`: a child process (`src/tests/HSMDatabase.LargeFiles.CrashHost`) appends pieces of 1 MiB through the real store and prints each acknowledged piece; the test kills it six times at random moments (most kills land inside a piece: bytes past the record were found), opens the store and checks every acknowledged piece byte for byte and the part cut back to its record, then lets it finish. **A kill, not a power loss:** the operating system still writes what the process had handed it.

## Finish

`StartFinish(lease)` answers `Incomplete`, `LeaseLost`, `Discarded` or `Checking`, or marks the file as being checked and returns a reader of its pieces (`ILargeFilePartReader`: in order, bytes and digest, boundaries from the index). `EndFinish` clears the mark whatever the result. The service (`FinishAsync(lease, wholeHash, checker)`):

1. The sensor must still be a File sensor of the record's product and not blocked, else the upload is discarded: `SensorGone`.
2. The checker reads the pieces (`ILargeFilePartChecker`; step 1 ships `StoredBytesHashChecker`, SHA-256 over the stored bytes, for tests; step 2 supplies the production one). No match: the upload is discarded, `Mismatch`. A cancelled or faulted check changes nothing on the disk.
3. On the product's queue (`LargeFileValueRequest`, never cancelled once queued): the sensor is checked again, the value time chosen, **the record written as Verified with the hash and the value time** (`MarkVerified`, durable), then the value added as any value is added (its checks, its alerts, its row: `TryAddValueWithDeliveryStamp` and `PersistAddedValue`). A sensor whose own checks do not take it (a singleton gate) gets the row written directly, with a Warn line: a verified file must have its value. The row is read back (the value write reports nothing).
4. `Verified` is answered only then, with the file id and the value time, and `FileVerified` is raised. A row that could not be written is a fault (`LargeFileStoreException`, `Write failures`): the record is Verified, so a begin answers `AlreadyStored` and the pass writes the row again from the record.

A second finish while the check runs answers `Checking` (an addition to the outcomes of the plan, see "What later steps must do").

## Discard and removal

`Discard(lease, reason)` removes the part, the index and the record, releasing the lease and the reservation; logged (Warn) and counted (`Parts removed`). `Remove(fileId, reason)` removes every file of a record (part, index, archive, the archive a re-encryption replaced) and then the record; refused while the file is being checked. An uploading record counts in `Parts removed`, a verified or packed one in `Files removed`; each removal is a Warn line naming the file, the sensor, the product and the reason.

## The pass

`LargeFileMaintenanceService` runs it once at start (after the tree is built: the cache builds it when it is constructed, before hosted services run) and then every hour, below normal priority. It works from the records and point reads of rows, never by scanning history (except the start check of rows, below).

1. **The store's own part** (`RunStorePass`, no value looked at):
   - an **uploading** record whose last piece is older than `LargeFiles.AbandonedUploadDays` (7), with no live lease and not being checked, is removed (`Parts removed`, Warn). A verified or packed file is never removed for age.
   - a `.part` / `.idx` without a record is kept until its newest file is that old, then removed (`Parts removed`, Warn); a part whose record exists but cannot be read is never taken for one.
   - the part left by a pack and the archive a re-encryption replaced (`previousArchiveId`) are removed; so is the `.zip.tmp` of a build not running in this process (Info).
   - empty product folders are removed.
2. **Records against sensors and rows:**
   - a record (any state) whose sensor no longer exists is removed: a removed sensor, a removed product (its sensors go with it), a merge occupant;
   - for a verified or packed record, on its product's queue (`LargeFileCheckRequest`, so a retention pass, a clear or a finish is never half-seen), a point read of the row at its value time:
     - the row is the large value of this file: kept;
     - the row is something else: **reported** (Warn, `Rows that disagree`), the file kept;
     - no row and the value time is below the sensor's history floor (the later of the KeepHistory cutoff and `HistoryClearedTo`, `TreeValuesCache.HistoryFloor`): removed (`Files removed`);
     - no row inside the kept range: **rolled forward**, the row written again from the record (the owner's decision, 2026-11-25), a Warn line and `Rows written again`. The motivating case is the crash between the verified record and its row;
     - a row that cannot be read: not decided, logged.
   - a record whose sensor is now of another product (a move that raced a begin) is reported and kept.

**A clear removes its files at once.** `ClearSensorHistory` (the UI clear and the retention pass, which runs through it) removes the files of the large values in the range it erased, logged and counted. The pass would find them too, but its floor `HistoryClearedTo` is kept in memory only: after a restart a cleared value inside the KeepHistory range would look like a missing row and be rolled forward. What is left: a crash between the delete of the rows and the removal of the files, then a restart; the pass then writes such a row again (a Warn line).

## The check at open and at start

At the store's first use (every server reads the records when the pass first runs at start):

| Found | Done |
|---|---|
| a `.rec.tmp` | deleted (Info): a record write that stopped before its rename; the record before it stands |
| a record that cannot be read, or names another file or product | reported (Error, `Start disagreements`), its files kept and not used |
| an uploading part or index longer than its record | cut back to the record (Info, `Start disagreements`): the bytes were never acknowledged |
| an uploading part or index shorter than its record | cut back to the last whole piece by the index (entries whose offsets chain and whose bytes are in the part), the record rewritten (Warn, `Start disagreements`); the collector sends the rest again |
| an uploading record without its part or index | the upload is removed (Warn, `Start disagreements`, `Parts removed`): it is not a value and the collector still has the source |
| a verified record whose part or index is missing or short | reported (Error, `Start disagreements`), kept: it cannot be packed or given out |
| a packed record without its archive | reported (Error, `Start disagreements`), kept |
| an archive no record names | reported (Error, `Start disagreements`), kept, never used |

At start the service also reads the rows of every **File sensor** and reports each large value (recognised by its first byte; only those rows are deserialized) whose file has no record (Error, `Start disagreements`); the value is kept. Nothing of this is repaired silently.

## Move and merge

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

There is no piece-size setting: the piece size is the caller's (the file connection derives it from its frame limit), within 65,536 .. its maximum. No switch: nothing happens until a file is uploaded. Docker: the `./LargeFiles:/app/LargeFiles` volume in both compose files (`aicontext/architecture/docker.md`).

## Self-monitoring

Under `Database/Large files` (`LargeFileSensors`), posted from the 5-minute self-monitoring sample, **only once the store's folder exists**. Counters since the process started, posted when they change: `Start disagreements`, `Parts removed`, `Files removed`, `Refused for space`, `Rows written again`, `Rows that disagree`, `Write failures`. Every one but the two removal counters is Error in the sample in which it grew and Ok at the next sample without growth. Step 4 (#1886) adds uploads in progress, bytes stored, builds and files waiting for a password, and the alerts.

## Log lines (logger `LargeFiles`)

- Open: `Large file store <folder> opened in <ms> ms: <n> files (<u> uploading, <v> verified, <p> packed), <d> disagreements, <o> parts without a record` (Info; Warn with disagreements), written when there is anything at all.
- A start, a verify, a pack: Info, with the file id, the sensor and the sizes.
- A refusal for space: Warn with the free bytes, the reservations and the minimum.
- Every removal: Warn with the reason; every roll-forward and every row that disagrees: Warn; every disagreement at open: Info, Warn or Error as in the table above.
- A write that fails: Error with the exception (`Write failures`).
- A pass that did anything: `Large file pass: ...` (Info).

## What later steps must do

**Step 2 (#1884), the file connection.** Code against `ILargeFileUploads` (`BeginAsync`, `AppendAsync`, `DiscardAsync`, `Release`, `FinishAsync`) and the types in `HSMDatabase.AccessManager.LargeFiles`. Pass `SensorPath` as the normalized path a registration stored (byte for byte, as for table blocks) and `ProductId` as the root product of the access key; check the key's permission before. Keep one piece = one zstd frame (the part concatenates them). Decompress and check the exact piece size yourself; the store bounds the stored length by the zstd bound of `MaxPieceSize`. Supply the production `ILargeFilePartChecker`. Map: `LargeFileStoreException` → a fault (5xx-like, send again); a refusal → its answer; `ArgumentException` → a protocol error of the client (a name missing or longer than 1,024 characters, a source identity longer than 256, a digest or hash that is not 32 bytes, a stored piece outside its bounds). `FinishAsync` may answer `Checking` (a second finish during the check). `AppendAsync` blocks its caller for the durable write. Call `Release` in a `finally` when the connection ends.

**Step 3 (#1885), the archives and the download.** The store's archive calls are built and tested: `BeginArchive` (Verified or Packed), `CommitArchive` (fsync of the temporary file, durable rename into `archives/`, the record Packed with `ArchiveId`, `PreviousArchiveId`, `PasswordVersion`, `ArchiveLength`, `ArchiveHash`, then the part or the replaced archive deleted, a delete that fails left to the pass), `AbandonArchive`, `OpenArchive` (`FileShare.Read | FileShare.Delete`), `OpenPart`, `OpenPieces`, `GetFiles(state)`, `TryGetFile`, `ArchivePath`, the event `FileVerified` (not durable: a packer also lists `GetFiles(Verified)` at start). A record keeps the reservation of its announced size until it is packed.

**Step 4 (#1886).** The rest of the self-monitoring and the alert on refusals for space; the sensor page's list of large values with the state from the record.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMCommon/SensorValues/SensorValues.cs` | `LargeFileValue` |
| `src/database/HSMDatabase.AccessManager/LargeFiles/ILargeFileStore.cs` | The store's interface, record, outcomes, limits, statistics |
| `src/database/HSMDatabase.LevelDB/LargeFiles/LargeFileStore.cs` | The store |
| `src/database/HSMDatabase.LevelDB/LargeFiles/LargeFileRecordFormat.cs` | The `.rec` format |
| `src/server/HSMServer.Core/LargeFiles/LargeFileUploadService.cs` | `ILargeFileUploads`, the begin request, the options, the pass |
| `src/server/HSMServer.Core/LargeFiles/StoredBytesHashChecker.cs` | The test checker of step 1 |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.LargeFiles.cs` | The value on the product's queue, the time, the history floor, the pass's decisions, the clear |
| `src/server/HSMServer/ServerConfiguration/Sections/LargeFilesConfig.cs` | The configuration section |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/LargeFileMaintenanceService.cs` | The pass at start and hourly |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/Database/LargeFileSensors.cs` | Self-monitoring |
| `src/tests/HSMDatabase.LargeFiles.CrashHost/` | The child process of the kill test |

## Tests

- `src/tests/HSMDatabase.LevelDB.Tests/LargeFileTests/LargeFileStoreTests.cs`: nothing created before the first begin, also by refused begins; append, finish, read back, the layout and a restart; a file of 0 bytes; out of order, twice, past the last piece; the bounds of a stored piece and a digest; two uploads of one file (a continue after a restart with the same file id, `Busy` with a live lease, a lease that expires or is released, another source identity, name, size or time is another upload, another piece size starts a new file id); `Checking` during a finish and no lease expiry meanwhile; a discard; the uploads in progress, the largest file and the piece sizes; the free-space refusal with two begins on the same free bytes, the reservation through a restart and a discard; a cut at each named point of an append, then a continue; a part longer and one shorter than its record; an uploading and a verified record without their part; a damaged record; a leftover temporary record; the 7 days of an uploading part and none for a verified file; a part without a record kept and then removed; the removal of a verified file; a pack, a re-encryption under an open reader, an abandoned build, the leftovers of a crashed build, an archive without a record and a packed record without its archive; the verified event with a failing handler.
- `src/tests/HSMDatabase.LevelDB.Tests/LargeFileTests/LargeFileStoreKillTests.cs`: the kill of a child process in the middle of a piece.
- `src/tests/HSMServer.Core.Tests/LargeFiles/LargeFileValueTests.cs`: the pinned bytes of an ordinary file value, the round trip and the tag of a large one, `with`, never deflated or inflated.
- `src/tests/HSMServer.Core.Tests/LargeFiles/LargeFileUploadServiceTests.cs` (real `DatabaseCore` and cache): the sensor a begin may name (missing, another type, blocked, an unknown product, a path not normalized) and the caller's contract; nothing created by the service or a refused begin; a verified upload becomes the value without content, in the history read too, and `AlreadyStored`; a wrong hash; `Incomplete` and `Checking`; a sensor removed during the upload; two values in one tick and a tick held by a row; removal with the sensor (a verified file and a part) and with the product; a clear; retention; the pass removing below the floor and rolling forward inside the kept range; a row that disagrees; a roll-forward after a restart; a large value without a record at start; the move and merge refusal and a move inside the product; the UI's new value and change of the last value.
- `src/tests/HSMServer.Core.Tests/BackgroundServices/LargeFileOperationsTests.cs`: the configuration's defaults, an error naming the key for each value out of range and an empty folder; the self-monitoring node absent until the folder exists, counters and failures as errors.
- `src/tests/HSMServer.Core.Tests/Controllers/HomeControllerLargeFileTests.cs`: 404 for the file and its stream, the info describes the large value.
- `src/tests/HSMDatabase.SealedHistory.Tests/RefusalTests.cs`: a large value is never sealed.
