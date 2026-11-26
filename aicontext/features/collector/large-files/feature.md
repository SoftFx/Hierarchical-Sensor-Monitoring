# Feature: Large Files (managed collector)

> Owner: collector | Last reviewed: 2026-11-26 | Canonical: yes
> Scope: Collector — watched folders and handed-over files sent as large file values of a File sensor, in pieces, on the
> file connection of the collector session. Decision: ADR-0022 (`docs/decisions/0022-large-file-values.md`). Wire: the
> file connection in [`../../api/wire-contract/feature.md`](../../api/wire-contract/feature.md). Epic #1882, step #1888
> (managed); the native collector is #1887 and mirrors the portable rules below. Amended on 2026-11-25 (ADR-0022 points
> 20, 21): secure and not-secure files, and ordinary file values above a quarter of the session frame routed here.

---

## Overview

An application gives its collector a **watched folder** (`WatchLargeFiles(sensorPath, folder, mask, options)`) or hands
over **one file** (`SendLargeFileAsync(sensorPath, filePath, options, token)`). The collector takes a file when it has
settled, uploads it in **pieces** (zstd level 3, one piece in memory) on the **file connection** (`hsm.files.v1`), ends
with the hash the wire asks for, and deletes the source only after the server answered `stored`. One upload at a time, on
one background thread of lowered priority, at most `MaxBytesPerSecond` (10 MB/s by default) sent. An upload that is cut
(a lost connection, Stop, a restart of either side) continues from the server's piece number.

Large files need the session transport: on HTTP, with a custom `DataSender` or on the session of another collector, a
watch is `Unavailable` and a hand-over completes `Unavailable` at once; said once through the collector's errors. No
throw for a condition of the environment. `SendFile` / `SendFileAsync` and `FileSensorOptions.MaxFileSizeBytes` are
unchanged.

Code: `LargeFiles/` (`LargeFileService` with its upload in `LargeFileService.Upload.cs`, `LargeFileWatch`,
`LargeFileStateStore`, `SourceFile`, `PieceFrame`, `FileNameMask`, `LargeFileTimings`), `Client/Session/FileConnection.cs`,
`Client/Session/SessionDataSender.LargeFiles.cs`, the public types in `Core/ILargeFileCollector.cs`,
`Options/LargeFileOptions.cs`, `PublicAPI/LargeFiles/`.

## Public API (4.2.0, additions only)

| Member | Notes |
|---|---|
| `ILargeFileCollector.WatchLargeFiles(sensorPath, folder, mask, options = null)` → `ILargeFileWatch` | Capability interface beside `IDataCollector`; `LargeFileExtensions` on `IDataCollector` |
| `ILargeFileCollector.SendLargeFileAsync(sensorPath, filePath, options = null, token = default)` → `Task<LargeFileResult>` | Never faults; the same file pending again returns the same task |
| `LargeFileOptions` | `SettleTime` 60 s (≥ 1 s), `DeleteAfterUpload` true, `MaxBytesPerSecond` 10,000,000 (bytes sent; 0 = none), `NewFileAlert` true, `Secure` true, `SensorOptions` (a `FileSensorOptions`) |
| `ILargeFileWatch` | `SensorPath`, `Folder`, `Mask`, `State`, `StateReason`, `FilesStored`, `FilesNotSent`, `Dispose()` (never throws) |
| `LargeFileWatchState` | `Active` 0, `Unavailable` 1, `Refused` 2 (the server refused the registration; until the next Start), `Disposed` 3 |
| `LargeFileOutcome` | `Stored` 0, `Refused` 1, `GivenUp` 2, `Missing` 3, `Unavailable` 4, `Cancelled` 5 |
| `LargeFileResult` | `Outcome`, `Reason`, `StoredTime`, `SourceDeleted` |
| `CollectorOptions.DataFolder` | See "The data folder" |
| `DefaultAlertsOptions.LargeFiles = 4` (`HSMSensorDataObjects`) | Sent in the registration of a large file sensor unless `NewFileAlert = false`; the server gives the sensor the default alert "new data has been received" (step 4, #1886). The collector attaches no alert of its own |

Argument mistakes throw `ArgumentException` (empty path, folder or mask; a mask with a directory separator, `[`, `]` or
`;`; a path the system cannot take; a sensor path
with a backslash or a tab; options out of range; a second live watch of the same sensor path, folder and mask). On a
disposed collector nothing is checked: a watch in state `Disposed`, a hand-over `Cancelled`.

**`FileSensorOptions` of a large file sensor** — used: `Description`, `SensorLocation`, `IsComputerSensor`,
`KeepHistory`, `SelfDestroy`, `TTLs`/`TTL`, `TtlAlerts`/`TtlAlert`, `Alerts`, `DefaultAlertsOptions` (ORed with
`LargeFiles`), `EnableForGrafana`, `IsSingletonSensor`, `IsForceUpdate`, `SensorUnit`, `Statistics`. Ignored (said once):
`MaxFileSizeBytes`, `DefaultFileName`, `Extension`, `IsPrioritySensor`, `AggregateData`, `PreviousPaths`. The path is
placed as `CreateFileSensor` places it (under the computer and module by default).

### Lifecycle

| Member | Stopped (never started) | Starting / Running | Stopping | Disposed |
|---|---|---|---|---|
| `WatchLargeFiles` | `Active`, polls after Start | `Active`, polls now | `Active`, after the next Start | `Disposed` |
| `SendLargeFileAsync` | queued, uploaded after Start | queued, uploaded now | queued, after the next Start | `Cancelled` at once |
| `ILargeFileWatch.Dispose` | removes the watch | removes it; its upload is cut (the part stays for a continue) | as Running | no-op |

Stop and Dispose do not wait for an upload: they cut it (the socket is aborted, the file handle closed) and wait for the
thread at most a second. The file connection is closed (1000) before the session. Final refusals are forgotten at Start.

Duplicates: two calls for one sensor path with different options — the first registration stands, the second is said
once. A path also created by `CreateFileSensor` — allowed; the last registration sent sets the sensor (give both the same
options). Two watches whose masks overlap on one folder — a file is taken once, by the watch created first. An equal live
watch — `ArgumentException`; after its dispose — allowed (same state file).

## Taking a file

- **Polling**, not `FileSystemWatcher` (it loses events on overflow and on network shares): every
  `min(SettleTime / 4, 10 s)`, at least 250 ms; the top level of the folder only. Symbolic links and reparse points are
  skipped; a missing or unreadable folder is said once and the watch stays `Active`.
- **The mask** is the collectors' own matcher (`FileNameMask`), not the system's (net472 matches `*.dmp` against
  `a.dmp_old` through 8.3 names): `*` any run, `?` one character, the whole name; ASCII case folding on Windows, exact
  elsewhere; `[`, `]`, `;` and separators are refused at the call (the subset both collectors match the same way). The
  native collector implements the same rule (rule 10 of `CLAUDE.md`), pinned by a conformance case.
- **Links**: a symbolic link or another reparse point is never followed (its target may lie outside the folder, and a
  delete through it would remove that): skipped, said and counted once per path and run.
- **Held open**: a file another program holds without sharing it (on Windows the open fails as a sharing violation) is
  said and counted once after 10 tries in a row; it is still taken when it is released.
- **Settle**: a file is taken when its size and modification time, as the scans observe them, have not changed for
  `SettleTime`. Files found at start wait one settle time like any other (no trust in an old modification time).
- **Order**: handed-over files first, in order; then the settled files of the watches, oldest modification time first,
  then the name.
- **Open** (Windows): `CreateFileW` with read (and, when it will be deleted, DELETE) access, sharing read only, with
  `FILE_FLAG_OPEN_REPARSE_POINT`; a sharing violation means a writer is still there: the file waits another settle time.
  The open denies writers and deleters for the whole upload. The facts — size, modification time, identity
  (`GetFileInformationByHandleEx(FileIdInfo)`: `source` = `vol:<16 hex>/file:<32 hex>`; a zero id = no identity) — are read
  from the handle. Elsewhere: a plain read open, the facts by path (neither framework reads the modification time from an
  open handle), no identity; the settle time alone decides.

## The upload

On the uploader's thread (`ThreadPriority.BelowNormal`; no effect on Linux), reading, SHA-256, CRC-32 and zstd level 3
(`ZstdSharp`, as table blocks). Every status as the wire contract says:

| Request | Answer | What the collector does |
|---|---|---|
| (local) | size over the hello's `maxFileBytes` | final refusal, no begin |
| registration (`commands`, its own request, before the first begin of each session) | per-path error / 4xx | the watch `Refused`, hand-overs `Refused`, until the next Start |
| `begin` | `200 next/pieces/pieceBytes` | pieces from `next` (`next == pieces`: `end`); a `pieceBytes` larger than the hello allows: begin again with the hello's |
| | `200 stored` | stored (a lost answer); the source is deleted per the rules below |
| | `202 checking` + `retryAfter` | wait, `begin` again (every `retryAfter` is capped at 5 minutes) |
| | `503` / `409 busy` + `retryAfter` | the file waits `retryAfter`; another file may go meanwhile |
| | `413`, `422`, `400`, other 4xx | final refusal |
| piece | `200 {next}` | the digest is kept at its piece number; go on from `next` (a repeat may jump ahead) |
| | `409 out of order {next}` | go on from `next` |
| | `409 checksum mismatch` | the same piece again; 3 in a row: a new connection and `begin`; 10 in all: given up |
| | `503` + `retryAfter` | wait, the same piece again |
| | `404` | `begin` again |
| | `400 … discarded` | begin again from 0; the third discard: given up |
| `end` (SHA-256 of the piece digests) | `202 checking` + `retryAfter` | wait, `end` again |
| | `200 stored` | stored |
| | `409 pieces are missing {next}` | `begin` again (the answer may come from the check, after which the connection holds no upload) |
| | `400 … discarded` | as at a piece |
| | `422` / other 4xx | final refusal |
| | `404` / `503` + `retryAfter` | `begin` again (then `end`) |
| `discard` | `200` / `404` | done; `409 checking`, `503`: wait and again; `400 unknown op` (an older server): said once, the part expires on the server |
| any | no answer in `RequestTimeout` after the frame was written, a cut, a close | a new connection after the session's backoff, `begin` again (continues from the server's `next`) |

Closes of the server: `1000` (replaced, its session ended) and `1001` → a new connection; `1008` (the key can no longer
send) → a final refusal until the next Start: the file in hand refused and counted, said once, nothing more uploaded
(watches wait, hand-overs stay queued); `1013` → a lost connection; `1002`, `1007`, `1009` (a frame of ours was wrong) count against the file:
the third gives it up. A refused upgrade: `404`/`400` (no large files on the server) is said once and asked again with
every new session and every 30 minutes; `409` waits for the next session; others, the session's backoff.

- **Speed limit**: each piece frame's bytes on the wire (88 + compressed length): read, compress, wait for that frame,
  send; the burst is one frame. Rebuilding digests by reading pieces again is not counted. A dump that compresses well is
  read from disk faster than the limit: only the lowered priority protects the disk. A piece frame whose write takes three heartbeats (90 s by default, the
  server's frame deadline) is said once with the link's rate.
- **Changed file**: size, modification time or identity different before `end` (or a short read) → `discard` on the
  connection that holds the upload (a `404` to it: `begin` with the original facts, then `discard`), the file taken again
  when it has settled (a new upload from 0). The in-progress record goes only when the server confirmed the discard (or
  a `begin` with the original facts answered `stored`); otherwise it stays, and a file that changed while the collector was
  down is begun under its original facts and discarded first. Once a run, the first upload sweeps the records whose file
  is gone or changed the same way. `400 unknown op` (an older server): said once, the part expires. Not counted as a loss.
- **Stored**: the delivered entry is written to the state first (and dropped again once the source is deleted: it only
  covers the moment between the two); then, with `DeleteAfterUpload`, the facts are compared
  again and the file is deleted through the open handle on Windows (`SetFileInformationByHandle(FileDispositionInfo)`:
  exactly the file that was sent). A file that changed after it was sent is not deleted (logged). **Without an identity**
  a `stored` answered at `begin` deletes the source only when this collector has its own record of having sent it
  (an in-progress or delivered entry); otherwise the file is kept and said. Off Windows the compare is by path and the
  delete by path: a file replaced under the same name between the two is the one deleted. **The state's word alone
  deletes nothing**: a file found through a delivered entry in delete mode (a crash between the state and the delete) is
  deleted only after a `begin` with its facts answered `stored` in this run; anything else uploads it. A hand-over the
  caller cancelled while it was stored is not deleted.
- **`StoredTime`** is the `time` of the `end` answer; null when `begin` answered `stored` (the wire has no time there).
- **Given up**: 3 discards, 10 checksum mismatches, 3 protocol closes of one file — persisted in the state, said again at
  every Start (the file still sits unsent), offered again only when it changes.

## Routed sends (ADR-0022 point 21)

On a session, a file value of `SendFile`, `SendFileAsync` or a string content whose bytes are more than the session
hello's `maxFrameBytes / 4` (1 MiB by default; no setting) is taken by the uploader instead of a `list` frame
(`SessionDataSender.SendFileAsync` → `LargeFileService.Route`), when the file connection is available. The send returns
as soon as it is taken, as a queued value does.

- From memory: no folder, no state file, no record, nothing deleted, nothing kept across a restart. `"secure": false`;
  `"source": "send:<guid N>"` (two sends of one name and size are two files); `"modified"` = the value's time; the
  value's `status` (its number) and `comment` in `begin`, left out when Ok / empty; the name is `Name.Extension`. No
  registration of its own and no flag 4: it waits until its sensor's ordinary registration was acknowledged on the session
  (at most 10 minutes, then dropped and counted: a bound of this collector's, so a registration that never comes does not
  hold a value for ever). `MaxFileSizeBytes` applies as to every ordinary send. Values waiting are bounded by
  `CollectorOptions.MaxQueueSize` (they left the file queue): past it the oldest not yet begun is dropped and counted.
  Each value is held once (its own bytes, not a copy). After a `1008` nothing is routed before the next Start: values
  take the ordinary path.
- Order: routed sends go before the files of the large-file API, in the order sent; one waiting preempts a large file in
  progress after its piece in flight, and that file then begins again and continues from the server's piece. The speed
  limit (the default 10 MB/s) applies to both.
- `stored` completes it. An answer with `retryAfter` is tried again while the process lives. A final refusal, a give-up,
  a registration refused, Stop and Dispose are a **dropped file value**: said with the path, name and size, and counted in
  `Queue overflow` under the **file queue** (not `Large files`). A routed upload cut by Stop discards its part while the
  connection is there; otherwise the part expires. A value whose `stored` answer was on its way when Stop came is still
  counted as dropped: Stop over-reports rather than under-reports.
- No file connection (the upgrade refused with `404`/`400`): the route is missing until the next session; what waits is
  given back to the file queue (`DataProcessor.RequeueFile`) and goes the ordinary way: a `list` frame when it fits, else
  dropped, said and counted as before. A value whose upload had begun is not given back (it may be stored already): it
  waits for the next session, whose `begin` says `stored` or where to go on from.
- A collector that only routes values never makes its data folder: the folder is made on the first use of a state.

## Secure

`LargeFileOptions.Secure` (true by default) is sent in every `begin` as `"secure"` and kept in the in-progress record
(another mark is another upload). A secure file is kept on the server as a zip under the product's dump password; one that
is not is the same zip without a password. Routed sends are never secure.

## The data folder

`CollectorOptions.DataFolder` (null: `%LOCALAPPDATA%` / `~/.local/share`, `HSMDataCollector`; the temporary folder, said
once, when the user has none) / **identity** / `large-files`. The identity is the first 16 hex digits of the SHA-256 of
`server|port|access key|computer|module|executable path`: two applications, or two products, never share a state.

| File | Holds | Lock |
|---|---|---|
| `watch-<key>.json` (key: folder, mask, sensor) | delivered files (path, name, size, modified, source, stored time) and failing files (discards, mismatches, protocol closes, given up) | its `.lock`, held for the life of the watch; held by another process: the watch says so once and waits, retrying at every scan |
| `sent-<key>.json` (key: sensor path) | the same, for handed-over files | for one upload; another process holding it 5 s: the file is put back |
| `upload-<key>.json` + `.digests` (key: sensor path, file path) | the upload in progress (id, original facts, piece size, pieces) and its piece digests, 32 bytes per piece by number | — |

Lock files are deleted when let go. Written whole to a temporary file of its own name (`.tmp-<guid>`), flushed, then renamed over the old one: a kill leaves the old or the new state. A missing state
is empty; a corrupt one is put aside as `.corrupt-<time>`, said once, and the collector starts it empty (a kept file may
be offered again; the server answers `stored` for one it still has). Entries of files no longer in the folder are dropped.
A digest list of another upload, size, time, identity or piece size is dropped; missing digests are filled by reading
those pieces again before `end`.

## Diagnostics

Every file not sent — refused, given up, missing (a hand-over), unreadable, a link, held open for 10 tries, handed over
and still queued at Dispose ("N handed-over large files were not sent") — is said once through the collector's errors
(the log and the `Collector errors` sensor) and counted in `Queue overflow` under the queue `Large files` (while that
sensor takes values) and in the watch's `FilesNotSent`. A file that changed is logged, not counted. Uploads log at Info:
the connection up and down, a cut, a store.

## Invariants

- One uploader thread at most; it exists only while a watch or a hand-over exists (it ends after 30 s idle). A
  generation number fences a thread left behind (a read blocked on a hung share): no state write and no delete after
  Stop or Dispose.
- One piece in memory (about 8 MiB at the default frame, allocated for an upload and dropped after it).
- The file connection is open only while there is a file to send.
- The source is deleted only after `stored`, and only the file that was sent.
- Nothing throws into the host from the uploader; its exceptions are reported and the file is tried again.

## Tests

`HSMDataCollector.Tests/LargeFiles/` (in memory: `FakeFileStore` with the server's semantics, a fake channel, a virtual
clock) and `Transport/LargeFileSessionTests.cs` (a real collector over a real WebSocket against the scripted server's file
side), linked into `HSMDataCollector.IntegrationTests`, so they run on net472 (net48 host) and net6.0 (net8.0 host):
the settle rule on a growing file, files at start oldest first, the mask, a cut at every point, the state after a kill, a
corrupt state, refusals of each kind, retries, checking, mismatches and their bounds, a changed file, the deny-write open
on Windows, the speed limit, Stop and Dispose in the middle, thread safety of the public calls, exception isolation,
the HTTP transport, a disposed collector.

Conformance: [`tests/conformance/collector/large_file_contract.hsmtest`](../../../../tests/conformance/collector/large_file_contract.hsmtest)
(the piece protocol, refusals, a continue, a changed file, the mask, settle, order). The native driver marks its verbs
`CONFORMANCE-UNSUPPORTED (#1887)` and does not register it until #1887.
