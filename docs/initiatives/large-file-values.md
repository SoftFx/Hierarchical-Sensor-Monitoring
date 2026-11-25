# Initiative: Large file values — application dumps delivered through HSM

> Owner: shared | Last reviewed: 2026-11-25 | Status: Decided, see ADR-0022 (`docs/decisions/0022-large-file-values.md`); epic #1882; nothing is built | Canonical: no

## Problem

An application with an embedded collector sometimes crashes, or writes a dump of its state without crashing. The dump has to reach the developers. Today somebody copies a file of tens of gigabytes by hand from the host.

The owner's requirement (2026-11-25): the dump goes through HSM. The collector picks it up, compresses it and uploads it; on the server it becomes a new value of a File sensor; users are notified; a user downloads it, and so does an AI agent through the API. The File sensor is to be brought to a finished state for this, and the heavy-data connection built for table sensors (epic #1842) is the model for the upload.

A File sensor cannot carry such a file today, at any stage of its path (see Current Behavior).

## Goals

- A file of up to 64 GB placed in a folder the application named reaches the server without a person, also after the application crashed and restarted.
- An upload that is cut continues from where it stopped.
- The file is a value of a File sensor: it has a place in the tree, a retention period, an alert when it arrives.
- A user and an API client download it; nobody gets its contents without the product's password.
- The upload does not disturb the application it runs in, nor the values and table blocks of the same collector.
- The mechanism is general: any large file of a File sensor. A dump is one use of it.

## Non-Goals

- Creating dumps. The application, the operating system or a person writes the file.
- Sending logs with a dump (another pipeline collects logs) or symbol files (not in the first version).
- Understanding the contents of a file. The collector and the server treat it as bytes.
- An HTTP route for uploading a large file. A collector that uses the HTTP transport cannot send one in the first version.
- The HSM agent and the Linux agent as senders. They can switch the same collector function on later, through their configuration.
- Keeping the last N dumps, a volume quota, or a mark that protects a dump from retention.
- Encryption of the stored bytes on the server's disk.

## Current Behavior

**The File sensor carries a file as one JSON document and stores it as one LevelDB row.**

- Wire: `FileSensorValue : SensorValueBase<List<byte>>` with `Name` and `Extension` (`src/api/HSMSensorDataObjects/SensorValueRequests/FileSensorValue.cs`). A byte of the file is a number in a JSON array, up to four bytes of JSON.
- Managed collector: the whole file is read into one array; `FileSensorOptions.MaxFileSizeBytes` is 10 MiB by default with a ceiling of 128 MiB (`src/collector/HSMDataCollector/Options/SensorOptions.cs`, `Sensors/InstantSensors/FileSensorInstant.cs`).
- Native collector: the whole file is read into memory on the caller's thread; the cap is a constant 10 MiB (`src/native/collector/src/hsm_collector.cpp`, `MaxFileSensorBytes`). There is no streaming upload of any kind, no compression library, and the session is text only: a binary message is an error (`src/native/collector/src/hsm_ws_connection.hpp`). Its dependencies are curl and nlohmann-json.
- On a session a file is one element of a `list` frame: about 1 MiB at the default `MaxFrameBytes` of 4 MiB; a larger one is dropped and counted. Over HTTP the body limit is 50 MB for every route (`src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs`).
- Server: the value is model-bound whole, deflated in memory and written as one row of the ordinary weekly sensor-values database, key `sensor id | time` (`src/database/HSMDatabase/DatabaseWorkCore/DatabaseCore.cs`). The last value of every File sensor stays in memory, decompressed (`FileValuesStorage.CacheSize => 1`). Nothing limits the size of a value; a failed write is logged and not reported to the sender.
- Download in the web UI: `HomeController.GetFile` and three more actions return the content from memory, the last 20 values at most, without streaming or ranges (`src/server/HSMServer/Controllers/HomeController.cs`, region "File"). Their product access checks belong to the web UI access-check work and are a precondition of step 3.
- API: an access key with `CanReadSensorData` gets the content as base64 inside JSON (`POST /api/sensors/history`), whole in memory. An API token (`/api/v1`, MCP) gets name, extension and size only: "file CONTENT is never serialized anywhere in the area" (`aicontext/features/server/management-api/feature.md`).
- Retention is by age only ("Keep sensor history"), key by key.
- Alerts: "new data has been received" exists for a File sensor; the message template has no variable for the file name, its size or a link. A Telegram message is limited to 1,000 characters and carries no attachment.

**The heavy-data connection of table sensors** (branch `feature/1842-table-sensors`, not in `master`): a second WebSocket of the collector session, subprotocol `hsm.tables.v1`, tied to the live session by `X-Hsm-Session`; binary frames of up to 4 MiB by default (64 MiB at most), each answered with a text acknowledgement, one in flight; a server-wide memory budget for frames over 64 KiB with one reservation per access key; close code `1013` when the budget is not available in time (`aicontext/features/api/wire-contract/feature.md`, "Table connection"; `aicontext/features/server/collector-session/feature.md`). The managed collector's client of it is being finished; the native collector has none (step 6, #1848, not started).

## The owner's decisions (2026-11-25)

| # | Question | Decision |
|---|---|---|
| 1 | What a dump is | A file that appears in a known folder: written by the application as it crashes (it then restarts by itself), written on request ("make a dump of the current state", into another folder), or put there by a person. |
| 2 | Who sends | The collector inside the application. The HSM agent may switch the same function on later. |
| 3 | Size | Up to 64 GB per file; the limit is a server setting. Compression of 3 to 6 times is expected and not measured yet. |
| 4 | Compression on the way | zstd, in the collector, on the fly, no temporary archive on the application's disk. |
| 5 | One file or several | One dump is one file. No logs, no symbol files. |
| 6 | Retention | By age only: the sensor's "Keep sensor history". |
| 7 | Disk short | The server refuses a new file before its upload starts; nothing is deleted early; the refusal is visible. |
| 8 | The source file | The collector deletes it after the server confirmed the whole file. |
| 9, 15 | Who downloads | Everyone who sees the product. The only form a file leaves the server in is a zip with the product's password. |
| 10 | API | A token downloads what its owner may download; no new token flag. An access key never downloads. |
| 11 | Connection | A WebSocket of its own on the session, open only while a file is uploaded. Not the table connection. |
| 12 | A half-uploaded file | Kept 7 days from its last piece, then deleted and reported. |
| 13 | Load on the application | A speed limit by default (10 MB/s), one upload at a time, one background thread. |
| 14 | Where the password is applied | On the server, when a file is downloaded. Set per product. |
| 16 | Scope of the mechanism | Any large file of a File sensor; a dump is a use of it. |
| 17 | Collectors | Native (C++) first, managed right after it in the same epic; the server part before both. |
| 18 | Notification | On by default for a sensor set up for dumps. |
| 19 | When a file is complete | Size and modification time unchanged for N seconds (60 by default). |
| 20 | UI | The sensor page. |
| 21 | Branch | An epic and an integration branch of its own, cut from `feature/1842-table-sensors`. |
| — | The store | Files on the server's disk, given out by Caddy ("сначала я планировал хранить это на диске и отдавать его напрямую через Caddy"). |
| 22 | Access when Caddy serves | HSM checks every download and writes the journal; Caddy moves the bytes. |

Not answered: how often dumps appear.

## Proposed Direction

### Terms

- **Large file value**: a value of a File sensor whose content is not part of the value. The value holds what describes the file (name, size, hash, state); the content lies in the large file store and travels in pieces.
- **Dump**: a file with a snapshot of a process, delivered as a large file value. To HSM it is bytes.
- **Piece**: a fixed-size part of the source file (the last one may be shorter), compressed by itself. A piece number names a place in the file.
- **File connection**: the WebSocket of a collector session that carries pieces.
- **Dump password**: the password of a product under which its large file values are given out.

### The collector

- The application names a folder, a name mask and a sensor path (`WatchLargeFiles(folder, mask, sensorPath, options)`), or hands over one file (`SendLargeFile(sensorPath, filePath)`). Both are additions to the public API; `SendFile` and its limits do not change.
- A file in a watched folder is taken when its size and modification time have not changed for N seconds and it opens for reading with no writer. Files found at start are taken by the same rule, oldest first.
- One upload at a time, on one background thread of lowered priority: read a piece, compress it with zstd level 3, send it, wait for the acknowledgement. The collector holds one piece in memory.
- A speed limit, 10 MB/s by default, set by the application.
- The collector keeps a small state file in its data folder: which files it has delivered. A file is deleted from the application's disk only after the server answered that the whole file is stored and its hash matched. A per-folder option keeps the file instead.
- What the collector cannot see: a file whose writer died for good looks complete and is sent as it is.
- Large files need the session transport, as table sensors do.

### The wire: the file connection

A further WebSocket on `GET {base}session` with its own subprotocol (working name `hsm.files.v1`), tied to the live session by `X-Hsm-Session`, taking no session slot, as the table connection does. It is opened for an upload and closed after it. The loop, the heartbeat, the access check for every frame, the acknowledgements and the close codes are those of the table connection; its memory accounting is reused with a budget of its own.

- **Begin** (text request): the sensor path, the file's name, size and modification time, the piece size. The server answers with the number of the next piece it wants (0 for a new file, N for one it holds a part of), or refuses: no room on the disk (to be tried again later), a file over the limit, a sensor that cannot take it. The refusal comes before a byte of the file is sent.
- **Piece** (binary frame): a header (request id, upload id, piece number, lengths, checksum) and the zstd frame of that piece. One in flight; the acknowledgement names the next piece.
- **End** (text request): the hash of the whole file. The server reads what it stored, checks the hash and answers `stored`; only then the value exists, the alert fires and the collector may delete the source. A mismatch discards the upload, and the collector sends the file again.
- A connection that drops in the middle costs one piece. After a restart of either side the collector begins again with the same file and continues from the server's number.
- The piece size is at most the frame limit; the default frame limit of 4 MiB needs about 47 KB/s of link speed to pass the 90-second deadline of a reserved frame.

Details to settle in the step: the header layout, whether the whole-file hash is a hash of the bytes or of the pieces' hashes (the second lets both sides resume without reading the file again), how the collector learns the result of a check that takes minutes.

### Where the bytes live on the server

**Direction of the owner (2026-11-25): files on the server's disk, given out by Caddy.** The comparison with LevelDB is being measured and its numbers are added here when the run ends; they are the record of why a second kind of store is justified here, when it was refused for table sensors (ADR-0021).

The two candidates:

- **A. LevelDB.** A database of its own for large files, one key per piece (`file id | piece number`), keys of a file only growing. One kind of store, as for table blocks.
- **B. Files (the direction).** One file per large value in a folder of the server; the value in the sensor's history holds the description.

Why files fit this data and did not fit table rows: a large file is written once, front to back, never changed, read as a whole or by byte range, and deleted as a whole. A web server gives a file out by itself, with ranges, without the application copying a byte. Table rows are many small records that are searched, merged and deleted by time, which is what LevelDB is for.

The layout:

- A folder set in the server's settings (it may be another volume than the databases), one subfolder per product.
- While a file is uploaded: `<file id>.part`, the pieces appended as they arrive, with a small record beside it (the description from `begin`, the number of pieces accepted).
- After `end`: the server reads the part once, checks the hash and writes `<file id>.zip` under the product's password, then removes the part. A file of a product without a password stays in its compressed form until a password is set.
- The value in the sensor's history points at the file by its id; the file's name on the disk tells nothing.
- At start the server compares the folder with the values: a part without a live upload record is kept its 7 days; a zip without a value, or a value without its zip, is reported and not silently fixed.

What the second store costs: two things to keep consistent (the value and the file), a folder that is not in the databases' folder, and a store that needs its own free-space check and its own retention pass.

### The store, measured (2026-11-25)

Both candidates were run the same way: a Linux container (Docker Desktop on WSL2, kernel 5.15), the data on a fresh ext4 volume for each scenario; `SoftFX.LevelDB.Standard` 1.2.2 opened as the table block store opens its database (no compression, 8 MiB write buffer); files of 8 GiB of incompressible bytes, as a compressed dump is; pieces of 1, 4 and 16 MiB. Most rows are single runs; the cold reads were repeated three times. The code is a throwaway console project on the local branch `experiment/dump-store-bench`.

| | LevelDB, one key per piece | One file |
|---|---|---|
| Bytes written to the disk per byte stored, each piece durable | 2.00 (log, then table) | 1.00 |
| Write of 8 GiB, each piece durable, pieces of 1 / 4 / 16 MiB | 146 / 73 / 55 s | 93 / 65 / 12 s |
| Write of 8 GiB, not synced per piece | 36–92 s | 17–20 s |
| Peak memory of the writer | 101–166 MB | 38–54 MB |
| What one file of 8 GiB is on the disk | 1,024 tables of 8 MiB | 1 file |
| Opening the store with three files in it | 39–165 ms | nothing to open |
| Reading 8 GiB back, cold | 109–975 MB/s | 1,162–3,346 MB/s |
| Resident memory of the reader | grows to 7.7–8.5 GB (mapped tables) | 29–46 MB |
| A range of 1 MiB, cold, median | 4–48 ms (reads the whole piece) | 0.6–2.3 ms |
| Deleting one file of three: the call | 0.2–5.2 s | 13–85 ms |
| Deleting one file of three: the space is back | only after a compaction of the range, 1.4–6.5 s | at once |
| After a kill of the writer | every acknowledged piece kept | every acknowledged piece kept |

- LevelDB did not rewrite data while it took the pieces: no compaction, only moves of tables. Its space overhead is at most 0.06%.
- From 2 to 8 GiB the times grew 3.8 to 4.7 times and the bytes written and the number of tables exactly 4 times. **Extrapolated to 64 GiB, not measured:** a durable write of about 580 s into LevelDB and 520 s into a file, about 128 GiB written to the disk by LevelDB, 8,192 tables for one file and about 82,000 for ten.
- The wrapper cannot set LevelDB's table size, and its reads always return a new array: a read through its ordinary API allocates the whole file again on the managed heap.
- Not measured: files of 64 GiB (the disk of the development machine), a power loss (only a kill of the process), a cache that is cold for certain, the production disk.

What the numbers say: LevelDB can hold the pieces and loses none in a crash, at twice the disk writes and with a reader whose memory grows by the size of what it reads. A file costs less at every row, and it is the only one of the two that a web server can give out without HSM touching the bytes.

### Giving a file out

- The only outward form is a zip with AES encryption under the product's password, holding the file under its original name. The classic zip password is not used: it can be broken when part of the content is known, and the header of a dump is known. Such a zip opens in 7-Zip and WinRAR, as a rule not in the archiver built into Windows.
- The password is set and seen by a Manager of the product and by an admin, in the product's settings. Its changes go to the journal. While a product has no password, its large files are accepted and kept and cannot be downloaded.
- **The zip is built once**, after the upload, and kept as the stored file. A download is then a plain file with byte ranges: a cut download continues, and no CPU of the server is spent on it. A change of the password re-encrypts the kept archives in the background; until an archive is re-encrypted it is given out under the password it has, and the page says which.
- **HSM decides, Caddy gives out** (owner, 2026-11-25). The download request comes to HSM: it checks the sign-in or the API token and that the caller may see the product, writes the journal record, and answers with an instruction to Caddy to serve the file. The bytes go from Caddy, not through HSM. For this the folder of large files is mounted into the Caddy container read-only, and the bundled Caddyfile gets one block. Without Caddy (`docker-compose.direct.yml`) HSM serves the same file itself, with ranges.
- A download that is cut must fail visibly for the client, never look like a whole file: the size is announced, and the hash of the archive is in a header and on the page.
- Every download is written to the journal: who (the user, or the token and its owner), when, which file. The record is made when the download is allowed; whether the bytes all arrived is in Caddy's access log.

### Access

- Web UI: everyone who may see the product downloads the zip. An unknown file and a file of a product the user may not see get the same `404`.
- The same rule holds for ordinary file values: the product access checks of the existing file actions are a precondition of the download step.
- API token: `GET /api/v1/...` streams the same zip to a token whose owner may see the product, with the size and the hash in headers. The MCP tool returns the description of a file and the link; the bytes never go through MCP. An AI agent gets the password from its operator, not from HSM.
- An access key (`Key` header) sends and never downloads a large file. `POST /api/sensors/history` returns the description of a large value, not its content.

### The sensor, retention, notification, UI

- A large value belongs to an ordinary File sensor. Its time is the modification time of the source file; the time it was accepted is kept beside it.
- Retention is the sensor's "Keep sensor history": a value past its period is removed with its bytes. A half-uploaded file is removed 7 days after its last piece.
- Before an upload the server compares the free space with a configured minimum plus the size announced; below it the upload is refused and the refusal is counted in a self-monitoring sensor with an alert.
- A sensor registered for dumps gets the alert "new data has been received" by default. New template variables carry the file name, the size and a link to the sensor page. The message is sent once, when the file is stored.
- The sensor page lists the large values: name, size, time, application and host, state (uploading with a percentage, stored, removed by retention), and the download button. No preview.
- Self-monitoring: uploads in progress, bytes stored, refusals, files removed.

## Open questions

- How exactly HSM hands a file to Caddy (an internal redirect answered by `reverse_proxy` with `handle_response`, or `forward_auth` in front of a `file_server`): to be tried against the bundled Caddy 2.11 and `caddy/test.sh` in the step.
- The zip library for AES on the server (the zip built into .NET has no encryption).
- The compression ratio and speed on a real dump: needs a sample from the owner, measured on his machine, numbers only.
- How often dumps appear, and the link speed between the application hosts and the server: both decide the defaults (speed limit, free-space minimum).
- The retention period a dump sensor gets by default.
- Whether a product without a password should rather refuse uploads than keep files nobody can download.

## Work Breakdown

Epic #1882. One PR per step, or per part of a large step, into the integration branch `feature/1882-large-file-values`, cut from `feature/1842-table-sensors`.

| Step | Issue | Scope | Notes |
|---|---|---|---|
| 0 | — | The measurement of the store | Done, above |
| 1 | #1883 | Server: the large file store (files in a folder) and the large value of a File sensor: write, read, delete, retention, the free-space check, the check at start | No dependency on table sensors |
| 2 | #1884 | Server: the file connection (begin, piece, end, continue, limits, budget) | Reuses the session loop of #1845 |
| 3 | #1885 | Server: the product's dump password, the zip, the download for the web UI, `/api/v1` and Caddy, the journal, the MCP tool | Relies on the product access checks of the web UI's file actions |
| 4 | #1886 | Server: the sensor page, the alert variables, the default alert, self-monitoring | |
| 5 | #1887 | Native collector: reading a file in pieces, the watched folder, the state file, the speed limit; conformance scenarios | Binary frames and zstd shared with #1848; first to production |
| 6 | #1888 | Managed collector: the same behaviour; the conformance scenarios pass in the .NET driver | net6.0 and net472 |
| 7 | #1889 | Rollout: a real dump end to end, a file of 64 GB, the settings of production, the wiki | |

## Risks

- A dump holds the memory of a process: keys, passwords, client data. With download open to everyone who sees the product, the password is the only protection of the content. A shared password cannot be taken back from one person, and the journal shows who downloaded, not who opened.
- The server knows the password and holds a file without it while the file is uploaded and checked. The password protects a downloaded copy, not the server.
- A second kind of store beside LevelDB: the value and its file can disagree after a crash or a manual change of the folder; the check at start reports it.
- The bundled Caddy gains access to the folder of large files and a rule of its own; an installation with its own reverse proxy has to repeat that rule or let HSM serve the files.
- A collector that deletes the source file makes the server's copy the only one. Sensor history is not part of the server's backups.
- A crash loop produces a dump per crash; the server refuses when the disk is short, and the files pile up on the application's disk.
- The native collector gains two dependencies (zstd, and binary WebSocket frames through curl) and its first streaming code.
- The epic stands on a branch that is not in `master`.
- Public collector API, DTOs and the session protocol are compatibility-sensitive: every change here is an addition.

## Verification

- Server tests over a real WebSocket: begin, pieces, end; a cut and a resume at every point; a wrong hash; a refusal for space; the limits; access lost in the middle.
- A download test with ranges and with a cut connection; the access matrix (role, token, key, unknown id).
- Conformance scenarios in both drivers.
- One real dump of the owner's application, end to end, with the measured compression ratio, times and the disk written.
- The download through the bundled Caddy and without it (`caddy/test.sh`, which runs only in WSL on the development machine).
- `aicontext/` updated with each step; the ADR after the owner agrees with this document.
