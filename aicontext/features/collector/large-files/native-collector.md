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

## Invariants

| Rule | Native |
|---|---|
| When a watched file is taken | size and modification time unchanged for the settle time (60 s, at least 1 s) as the collector observed them on its own monotonic clock (nothing found at start is trusted by its age); on Windows also an open with read sharing only must succeed (no writer, no holder without delete sharing) |
| Scan | polling, top level only, every `min(settle/4, 10 s)`, at least 250 ms; links and reparse points skipped (said and counted); names that are not UTF-8 skipped (said and counted); our mask matcher (`*`, `?`, whole name, ASCII case folding on Windows only) |
| Order | handed-over files first, in the order handed over; then settled watched files by modification time, then name; a file two watches match is taken by the watch made first |
| Concurrency | one upload at a time per collector, on one thread of lowered priority |
| Open | Windows: `GENERIC_READ | DELETE`, `FILE_SHARE_READ`, `FILE_FLAG_OPEN_REPARSE_POINT` (writers and deleters denied for the whole upload); Linux: `O_RDONLY | O_NOFOLLOW` |
| Identity (`source`) | Windows: `GetFileInformationByHandleEx(FileIdInfo)` → `vol:<16 hex>/file:<32 hex>`, a zero id = none; Linux: `ino:<hex>` (the device is compared within a run only: it is not stable across reboots) |
| `modified` | the last write time in UTC, 7 fractional digits, `Z`; every comparison at 100 ns ticks |
| A piece | `pieceBytes` of the source (the last the remainder); one zstd level 3 frame stating its content size, after the 88 reserved header bytes in the same buffer; CRC-32 (zlib) of the frame; SHA-256 of the source bytes; the piece size is the hello's, then the begin answer's |
| `end.hash` | SHA-256 of the concatenated 32-byte piece digests |
| Speed limit | **bytes sent** (88 + compressed length per frame), 10,000,000 B/s by default: read, compress, wait for that frame's turn, send; the burst is one frame. A dump that compresses well is read from the disk faster than the limit; only the lowered priority protects the disk |
| `retryAfter` | at least 1 s, at most 5 minutes (both collectors) |
| Waits | every wait (a `retryAfter`, `checking`, the speed limit, a digest re-read) services the file connection, so its heartbeat and a replace close are answered |
| Final refusal | 413, 422, a 400 at `begin`, another 4xx, a local size over `maxFileBytes`, a close with 1008: the file is left, said once, counted once, not tried again before the next Start |
| Given up (persisted) | 3 discards (a 400 at a piece or at `end`), 10 checksum mismatches (3 in a row on one piece: reconnect and `begin`), 3 closes with 1002/1007/1009; recorded in the state file, reported again at every Start, offered again only when the file changes |
| Changed file | size, time or identity differ before `end` or before the delete (Linux: also the path's inode): `discard` on the connection, or `begin` with the facts it was begun with and then `discard`; taken again when settled; `400 unknown op` (a server before `discard`): the part expires there, said once |
| Delete | only after `stored` in this run (at `end`, or at `begin` for a file the server has), in delete mode, after the facts compare equal again; Windows through the open handle (`FileDispositionInfo`), Linux `unlink` with the descriptor still open over the comparison; the delivered entry is written first. Without an identity, a `stored` at `begin` deletes only with the collector's own record of sending that file. A hard link loses one name |
| Feature absent | an upgrade answered 404 or 400: said once; asked again with every new session and every 30 minutes |
| Stop / dispose | cut, never waited for; continue after Start from the server's `next` |

## The data folder and the state files

`<base>/HSMDataCollector/<identity>/large-files/`, `<base>` the application's folder (`hsm_collector_set_data_folder`)
or `FOLDERID_LocalAppData` on Windows, `$XDG_DATA_HOME` or `$HOME/.local/share` elsewhere, the temporary folder when
there is none (said once); `<identity>` the first 16 hex of SHA-256 of `server|port|key|computer|module|executable`.
The managed format (`version: 1`): `watch-<sha16(folder|mask|sensor)>.json` per watch, `sent-<sha16(sensor)>.json`
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
of a piece, 20 rounds; a writer holding the file (Windows); a file changed in the middle (Linux). Run under ASan and
TSan in a Linux build with HTTP and tables (the CI sanitizer presets build without HTTP).

## Known Issues / Limitations

- The end-to-end run against a real server waits for steps 1 and 2 in the integration branch.
- Conformance (`tests/conformance/collector/large_file_contract.hsmtest`) is written by #1888; the native driver's
  verbs follow it.
- Linux: nothing can deny writers; a writer that keeps writing is caught by the settle time and the comparisons
  before `end` and before the delete. A read blocked on a dead network mount holds Stop until it returns.
- The owner's amendment of 2026-11-25 (secure and not-secure files, ordinary sends above the threshold on the file
  connection, ADR-0022 points 20 and 21) is not in 0.16.0; `hsm_large_file_options_t` takes the mark as an appended
  field.

## What differs from the managed collector

- The outcome of a handed-over file is a polled handle, not a task; cancel is `hsm_large_file_cancel`.
- `source` on Linux is `ino:<hex>` (managed sends none there); the delete compares the open descriptor with the path.
- Native counts a file not sent in the one `Queue overflow` counter (no queue name), as its table rows.
- Stop joins the uploader without a time cap (a native thread must not outlive its collector); Windows cancels a
  blocked read, and the thread's priority is raised when a stop is asked.
