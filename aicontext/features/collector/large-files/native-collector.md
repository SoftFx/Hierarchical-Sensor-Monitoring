# Feature: Large Files (native collector)

> Owner: shared | Last reviewed: 2026-11-26 | Canonical: yes

Large file values in the native C++ collector (`src/native/collector`), since 0.16.0 (#1887, epic #1882,
ADR-0022). The behaviour is the managed collector's (#1888, `feature.md` in this folder once it lands): the same
rules are listed here as the native collector keeps them, with what differs at the end. Its first consumer is the
Aggregator, a native application that writes a dump when it crashes and restarts by itself.

## Overview

An application gives the collector a **watched folder** (a folder, a name mask, a File sensor's path) or **hands
over one file**. A file is uploaded in pieces on the **file connection** of the collector session (wire contract,
"File connection", `hsm.files.v1`): `begin` names the file, each piece is read, hashed (SHA-256), compressed (zstd
level 3) and sent as one binary frame with an 88-byte header, one in flight, and `end` sends the hash of the piece
digests; the server checks what it stored and answers `stored`, and only then does the value exist and is the source
deleted (unless kept). An upload that is cut continues from the server's piece number, after a drop and after a
restart of either side.

## API / Public Contracts

- **C ABI** (`hsm_collector.h`, `docs/native-collector-c-abi.md`, "Large files"): `hsm_collector_set_data_folder`,
  `hsm_collector_watch_large_files` (→ `hsm_large_file_watch_t`: `_get_stats`, `_state_reason`, `_release`),
  `hsm_collector_send_large_file` (→ `hsm_large_file_t`: `hsm_large_file_get_result`, `_reason`, `_cancel`,
  `_release`), `hsm_large_file_options_default`, `HSM_LARGE_FILE_NO_SPEED_LIMIT`. Zero in every option is its default.
- **Secure or not** (ADR-0022 point 21): `not_secure` (0 = secure, the default; C++ `LargeFileOptions::secure`). Every
  `begin` carries `"secure"`; the in-progress record keeps it, and it is part of the upload's key (another mark is
  another upload). A secure file is kept on the server in a zip under the product's dump password, the other in the
  same zip without one.
- **C++ wrapper** (`include/hsm_collector/large_files.hpp`): `LargeFileOptions`, move-only `LargeFileWatch` (its
  destruction stops watching) and `LargeFile` (its destruction leaves the file queued), `Collector::SetDataFolder`,
  `WatchLargeFiles`, `WatchLargeFilesForLife`, `SendLargeFile`. They throw `Error` only for a mistake of the call and
  after dispose.
- **No failure for the environment.** The HTTP transport, the in-memory default at Start, a libcurl without
  WebSocket and a build without `HSM_COLLECTOR_HTTP` and `HSM_COLLECTOR_TABLES` make a watch `UNAVAILABLE` and a
  handed-over file end `UNAVAILABLE`, with the reason, said once; the call returns OK.
- **The File sensor** is registered by its own `commands` request on the session before its first `begin`, and again
  at every new session: the registration `hsm_collector_create_file_sensor` sends, with the options' description,
  history period, TTL, self-destroy and location, and `DefaultAlertsOptions` = 4 (`LargeFiles`: the server adds the
  alert "new data has been received") unless `no_new_file_alert`. The flag is set only here; it is not part of the
  public `hsm_default_alerts_options_t`. A per-path error in the ack refuses the watch (`REFUSED` until the next
  Start) or the file (`REFUSED`).
- **Bounds of `begin`** (the server's, in UTF-16 code units): a full sensor path over 1,024 is refused at the call
  (`HSM_RESULT_INVALID_ARGUMENT`); a file whose name is over 1,024, or is not valid UTF-8 (a POSIX path), is a final
  refusal of that file (no `begin` can carry it: a hand-over ends `REFUSED`, a watched file is final for the run;
  said and counted). A `comment` (routed sends only) over 4,096 is cut at a code point and ends with an ellipsis
  (`…`), the 4,096 including it. Optional fields are left out, never sent as `null`.

## Invariants

| Rule | Native |
|---|---|
| When a watched file is taken | size and modification time unchanged for the settle time (60 s, at least 1 s) as the collector observed them on its own monotonic clock (nothing found at start is trusted by its age); on Windows also an open with read sharing only must succeed (no writer, no holder without delete sharing) |
| Scan | polling, top level only, every `min(settle/4, 10 s)`, at least 250 ms; links and reparse points skipped (said and counted); names that are not UTF-8 skipped (said and counted); our mask matcher (`*`, `?`, whole name, ASCII case folding on Windows only) |
| Order | handed-over files first, in the order handed over; then settled watched files by modification time, then name; a file two watches match is taken by the watch made first |
| Concurrency | one upload at a time per collector, on one thread of lowered priority |
| Open | Windows: `GENERIC_READ | DELETE`, `FILE_SHARE_READ`, `FILE_FLAG_OPEN_REPARSE_POINT` (writers and deleters denied for the whole upload); Linux: `O_RDONLY | O_NOFOLLOW` |
| Identity (`source`) | Windows: `GetFileInformationByHandleEx(FileIdInfo)` → `vol:<16 hex>/file:<32 hex>`, a zero id = none; Linux: `ino:<hex>` (the device is compared within a run only: it is not stable across reboots). The cost on Linux: an inode reused after a delete makes a new file with the same name, size, modification time and inode look like the delivered one, so in keep mode it is not sent |
| Digest list from an earlier run | trusted when the name, size, 100 ns modification time, identity and piece size are unchanged. The cost: a content changed under an unchanged modification time (a tool that restores it) is hashed from the old digests, the server's check passes for the old content, and the new file is deleted after `stored` |
| `modified` | the last write time in UTC, 7 fractional digits, `Z`; every comparison at 100 ns ticks |
| A piece | `pieceBytes` of the source (the last the remainder); one zstd level 3 frame stating its content size, after the 88 reserved header bytes in the same buffer; CRC-32 (zlib) of the frame; SHA-256 of the source bytes; the piece size is the hello's, then the begin answer's |
| `end.hash` | SHA-256 of the concatenated 32-byte piece digests |
| Speed limit | **bytes sent** (88 + compressed length per frame), 10,000,000 B/s by default: read, compress, wait for that frame's turn, send; the burst is one frame. A dump that compresses well is read from the disk faster than the limit; only the lowered priority protects the disk |
| `retryAfter` | at least 1 s, at most 5 minutes (both collectors); an answer that is not final and has none waits 30 s |
| Waits | every wait (a `retryAfter`, `checking`, the speed limit, a digest re-read) services the file connection, so its heartbeat and a replace close are answered |
| Final refusal | 413, 422, a 400 at `begin`, another 4xx at `begin` or `end`, a 4xx at a piece other than 400, 404 and 409, a local size over `maxFileBytes`, a `begin` that cannot be built (a name not UTF-8 or over 1,024), a close with 1008: the file is left, said once, counted once, not tried again before the next Start. A handed-over file that cannot be read is `MISSING`, not `REFUSED` |
| `end` answered 409 | always `begin` again (it names the piece to go on from), never pieces straight on: once the server's check has begun (a `202` came first) the connection no longer holds the upload |
| `503` at `end` | the server could not read the part: wait (`retryAfter`), `begin`, `end` again; past 3 such rounds in a row (per attempt; for a routed send counted across attempts) the file waits 30 minutes, said once and not counted as a loss |
| `404` on a piece or at `end` | a backoff, then `begin` again; past 3 such restarts of one attempt the file is given up |
| Given up (persisted) | 3 discards (a 400 at a piece or at `end`), 10 checksum mismatches (3 in a row on one piece: reconnect and `begin`), 3 closes with 1002/1007/1009, a fourth `404`; recorded in the state file, reported again at every Start, offered again only when the file changes |
| Changed file | size, time or identity differ before `end` or before the delete (Linux: also the path's inode): `discard` on the connection, or `begin` with the facts it was begun with and then `discard`; taken again when settled; `400 unknown op` (a server before `discard`): the part expires there, said once |
| Delete | only after `stored` in this run (at `end`, or at `begin` for a file the server has), in delete mode, after the facts compare equal again; Windows through the open handle (`FileDispositionInfo`), Linux `unlink` with the descriptor still open over the comparison; the delivered entry is written first. Without an identity, a `stored` at `begin` deletes only with the collector's own record of sending that file. A hard link loses one name |
| Feature absent | an upgrade answered 404 or 400: said once; asked again with every new session and every 30 minutes |
| A piece's write past its bound | every frame's write is bounded (`max(request timeout, 10 heartbeats)`); a write that passes it ("send timed out") loses the connection: reconnect after a backoff, `begin`, go on from the server's piece; not a checksum mismatch, not a protocol close. The ack deadline starts after the frame was written |
| Stop / dispose | cut, never waited for; continue after Start from the server's `next` |
| Cancel racing `stored` | a cancel before the uploader commits the outcome (right after `stored`) keeps the source and stays `CANCELLED`; after it (the delete decided or done) the outcome is `STORED` |
| Lifecycle on the uploader thread | a logger called there must not stop, dispose or destroy the collector: Stop returns `HSM_RESULT_INVALID_STATE`, Dispose returns and says so once; destroy there is not allowed (as for the lifecycle listener) |
| Lowered priority | a failure to lower it is said at debug level; the upload goes on at normal priority |

## Ordinary sends above the threshold (ADR-0022 point 20)

On a session, the content of `hsm_sensor_add_file`, `hsm_sensor_add_file_from_path` and `FileSensor::SendFile`
(so the legacy wrapper's `SendFileAsync`) is queued as every value is. **When the value queue is about to send it**,
it is a **routed send** if it is larger than a quarter of the frame limit of the session it is sent on (1 MiB at the
default 4 MiB; the native frame clamp of 8 MiB applies) and the file connection is available: it goes to the
uploader instead of a `list` frame. The 10 MiB cap of these calls stays; the calls return as before.

- The content comes from memory: no folder, no state file, no in-progress record or digest list on a disk, nothing
  deleted, nothing kept across a restart (as the value queue).
- `begin` carries `"secure": false`, `"source": "send:<guid>"` (a new one per send), `"modified"` = the value's
  time, and `"status"` / `"comment"` of the value when not Ok / not empty. No registration of its own and no flag
  4: it waits until the sensor's ordinary registration was acknowledged on the session, at most 60 s on the
  collector's clock; then, or at once when the server refused that registration (a 4xx, or a per-path error in the
  ack), it takes today's path.
- A value whose name, path or comment is not valid UTF-8, or whose name or path is over 1,024 characters, is not
  routed (today's path).
- Held in memory: at most 64 MiB of routed sends wait; one is always taken whatever its size; a send past the
  bound is a dropped value (said, counted), not sent the ordinary way.
- Order: routed sends go before files of the large-file API, in the order sent; a routed send that is ready
  preempts a large file after its piece in flight; the large file then begins again and continues from the
  server's piece. The speed limit applies to both.
- `stored` completes it. An answer with `retryAfter` is tried again while the process lives. A final refusal (a
  4xx without `retryAfter`, a size over the server's limit, 3 discards, 10 checksum mismatches, 3 protocol
  closes, a fourth `404`, 1008) is a **dropped value**: said once with the sensor, the name and the size, and
  counted in the overflow counter (`Queue overflow`), where a dropped file value is counted today. After a 1008 the
  routed sends still waiting take today's path at once.
- **Stop / Dispose**: a routed send for which no `begin` was answered goes back to the value queue before its stop
  drain (sent the ordinary way, or dropped and counted when it does not fit a frame), so a server without file
  connections loses nothing it gets today; one whose upload had begun is a dropped value, and its part expires on
  the server (Stop cancels the connection, so no `discard` goes out).
- **No route, no change**: the collector learns that the server has no file connections from a refused upgrade
  (404 or 400), remembered until the next session; then, and in a build without large files, with the HTTP
  transport, or during the stop drain, the value takes today's path (a `list` frame; a value that does not fit a
  frame is dropped, said and counted). A routed send waiting when the route turns out to be missing goes back to
  that path; a value that went back is never routed again.

## The data folder and the state files

`<base>/HSMDataCollector/<identity>/large-files/`, `<base>` the application's folder (`hsm_collector_set_data_folder`)
or `FOLDERID_LocalAppData` on Windows, `$XDG_DATA_HOME` or `$HOME/.local/share` elsewhere, the temporary folder when
there is none (said once); `<identity>` the first 16 hex of SHA-256 of `server|port|key|computer|module|executable`.
The format (`version: 1`; the schema, the lock names and the temporary names are each collector's own and need
not match the managed collector's: a data folder belongs to one collector): `watch-<sha16(folder|mask|sensor)>.json`
per watch, `sent-<sha16(sensor)>.json`
for handed-over files (each with `delivered` and `givenUp` entries named by name, size, modified and source), and per
upload in progress `upload-<sha16(path)>.json` (the facts it was begun with, the upload id, the piece size, the
counters) with `upload-<sha16(path)>.digests` (32 bytes per piece, by number, zero = absent). Each JSON file is guarded
by an exclusive lock on `<file>.lock` between processes (Windows: no sharing; Linux: `flock`) and replaced whole
through a unique temporary name. Missing: empty. Corrupt: renamed `.corrupt-<UTC>`, said, started empty (a kept file
may be offered again; the server answers `stored` for one it still has). A watch whose state file another process
holds uploads nothing and says so once. At Start the records of uploads whose file is gone or changed are discarded.

## Key Files

| File | Role |
|---|---|
| `src/large_files/hsm_large_files.{hpp,cpp}` | watches, handed-over files, the uploader thread and its state machine |
| `src/large_files/hsm_file_connection.{hpp,cpp}` | `begin`, pieces, `end`, `discard` over `AckedBinaryLink` (`hsm.files.v1`), every answer read into a kind |
| `src/large_files/hsm_large_file_platform.{hpp,cpp}` | listing, the deny-write open, facts and identity, reads at an offset, the delete of exactly the sent file, locked state files, thread priority |
| `src/large_files/hsm_large_file_state.{hpp,cpp}` | the state files and the digest list |
| `src/large_files/hsm_large_file_mask.hpp`, `hsm_sha256.{hpp,cpp}` | the mask matcher; SHA-256 from CNG or libcrypto |
| `src/hsm_acked_binary_link.{hpp,cpp}` | `Request`: a text request with its ack on the second/third connection (added for `begin`/`end`/`discard`) |
| `include/hsm_collector/large_files.hpp` | the C++ wrapper |
| `tests/hsm_large_file_tests.cpp`, `tests/hsm_file_connection_tests.cpp`, `tests/hsm_large_file_upload_tests.cpp` | the answers, SHA-256, the file connection against `tests/file_server_script.hpp`, the uploads end to end |

## Tests

`native_large_file_*` and `native_file_connection_*` (CMake: the build with HTTP and tables). The uploads run a real
collector on the session of the scripted test server and its file side (`tests/file_server_script.hpp`: the server's
checks of a piece, the part store, the hash check, scripted answers), on a manual clock that the uploader's waits
advance (recorded), and cover: the settle rule on a growing file; files found at start, oldest first; the speed limit
(one wait before every frame but the first, the previous frame's wire bytes at the limit); a drop, an unanswered
piece and a restart in the middle, continued from the server's next; every refusal; three discards given up and
remembered after a restart; checksum mismatches; a close with 1008; an empty file; keep mode not sent again; a corrupt
state file; the HTTP transport; argument mistakes; stop, dispose, cancel and destroy from another thread in the middle
of a piece, 20 rounds; a writer holding the file (Windows); a file changed in the middle (Linux); `end` answered 202
then 409 (begun again); a 404 bound; a piece refused with 403; a cancel racing `stored`; the bounds of `begin`;
a name that is not UTF-8 (Linux); stop, dispose and destroy from another thread during a `retryAfter` wait, a
202 check and a speed-limit wait; a watch released during an upload; stop and dispose from the logger on the
uploader thread; a discard after a cut (and `400 unknown op`); routed sends: the threshold of the live session, a
refused registration, a bad comment, a 1008, a stop that gives them back to the value queue, and the 64 MiB bound.
A piece whose write passes its send bound (the server stops reading; the bounded WebSocket send of #1910) is
sent again after a new connection and `begin`, counted neither as a checksum mismatch nor as a protocol close; the
ack deadline starts once the frame is written, so a write held longer than the ack timeout is still answered.
The fake server lets the upload go when an `end` begins its check, as the real one does. Run under ASan and TSan in
a Linux build with HTTP and tables (the CI sanitizer presets build without HTTP).

## Known Issues / Limitations

- The end-to-end run against a real server waits for steps 1 and 2 in the integration branch.
- Conformance: `tests/conformance/collector/native/large_file_contract.hsmtest` is #1888's fixture (29 cases,
  the registration with and without flag 4 included); the native driver implements every verb.
- Linux: nothing can deny writers; a writer that keeps writing is caught by the settle time and the comparisons
  before `end` and before the delete. A read blocked on a dead network mount holds Stop until it returns.

## What differs from the managed collector

- The outcome of a handed-over file is a polled handle, not a task; cancel is `hsm_large_file_cancel`.
- `source` on Linux is `ino:<hex>` (managed sends none there); the delete compares the open descriptor with the path.
- Native counts a file not sent in the one `Queue overflow` counter (no queue name), as its table rows.
- Stop joins the uploader without a time cap (a native thread must not outlive its collector); Windows cancels a
  blocked read, and the thread's priority is raised when a stop is asked.
- The state files' schema, lock names and temporary names are native's own (see "The data folder").
