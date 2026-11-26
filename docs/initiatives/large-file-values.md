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
- A user and an API client download it; nobody gets the contents of a secure file without the product's password.
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
- Native collector: the whole file is read into memory on the caller's thread; the cap is a constant 10 MiB (`src/native/collector/src/hsm_collector.cpp`, `MaxFileSensorBytes`). There is no streaming upload of any kind, and the session is text only: a binary message is an error (`src/native/collector/src/hsm_ws_connection.hpp`). On `master` its dependencies are curl and nlohmann-json and it has no compression library; zstd arrives with table sensors (below).
- On a session a file is one element of a `list` frame: about 1 MiB at the default `MaxFrameBytes` of 4 MiB; a larger one is dropped and counted. Over HTTP the body limit is 50 MiB for every route (`src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs`).
- Server: the value is model-bound whole, deflated in memory and written as one row of the ordinary weekly sensor-values database, key `sensor id | time` (`src/database/HSMDatabase/DatabaseWorkCore/DatabaseCore.cs`). The last value of every File sensor stays in memory, decompressed (`FileValuesStorage.CacheSize => 1`). Nothing limits the size of a value; a failed write is logged and not reported to the sender.
- Download in the web UI: the content is returned from memory, the last 20 values at most, without streaming or ranges (`src/server/HSMServer/Controllers/HomeController.cs`, region "File").
- API: an access key with `CanReadSensorData` gets the content as base64 inside JSON (`POST /api/sensors/history`), whole in memory. An API token (`/api/v1`, MCP) gets name, extension and size only: "file CONTENT is never serialized anywhere in the area" (`aicontext/features/server/management-api/feature.md`).
- Retention is by age only ("Keep sensor history"): a delete of the sensor's key range up to a time. The values are not enumerated, so nothing today could remove a file that belongs to a value.
- Alerts: "new data has been received" exists for a File sensor; the message template has no variable for the file name, its size or a link. A Telegram message is limited to 1,000 characters and carries no attachment.

**The heavy-data connection of table sensors** (branch `feature/1842-table-sensors`, not in `master`): a second WebSocket of the collector session, subprotocol `hsm.tables.v1`, tied to the live session by `X-Hsm-Session`; binary frames of up to 4 MiB by default (64 MiB at most), each answered with a text acknowledgement, one in flight; a server-wide memory budget for frames over 64 KiB with one reservation per access key; close code `1013` when the budget is not available in time (`aicontext/features/api/wire-contract/feature.md`, "Table connection"; `aicontext/features/server/collector-session/feature.md`). State of the collectors on 2026-11-25, moving daily: the managed collector has zstd in that branch (#1846, part 1) and its client of the connection is in review (PR #1894); the native collector has its block writer with zstd in that branch (#1848, part 1, PR #1890) and its binary WebSocket frames are in review (PR #1892). This integration branch was cut before PR #1890 and does not hold it yet.

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
| 9, 15 | Who downloads | Everyone who sees the product. The only form a file leaves the server in is a zip with the product's password. (Amended by 23 and 26: the zip of a secure file is under the password; a file that is not secure is a zip without one.) |
| 10 | API | A token downloads what its owner may download; no new token flag. An access key never downloads. |
| 11 | Connection | A WebSocket of its own on the session, open only while a file is uploaded. Not the table connection. |
| 12 | A half-uploaded file | Kept 7 days from its last piece, then deleted and reported. |
| 13 | Load on the application | A speed limit by default (10 MB/s), one upload at a time, one background thread. |
| 14 | Where the password is applied | On the server, not in the collector: the server makes the zip. Set per product. |
| 16 | Scope of the mechanism | Any large file of a File sensor; a dump is a use of it. |
| 17 | Collectors | Both, in parallel, in the same epic; the server part before both. (First answer: native first, managed right after it. Changed the same day, when the owner named the second consumer: the Aggregator runs on the native C++ collector, TTS on the managed .NET one, and the managed collector does not wait for #1848.) |
| 18 | Notification | On by default for a sensor set up for dumps. |
| 19 | When a file is complete | Size and modification time unchanged for N seconds (60 by default). |
| 20 | UI | The sensor page. |
| 21 | Branch | An epic and an integration branch of its own, cut from `feature/1842-table-sensors`. |
| — | The store | Files on the server's disk, given out by Caddy ("сначала я планировал хранить это на диске и отдавать его напрямую через Caddy"). |
| 22 | Access when Caddy serves | HSM checks every download and records it; Caddy moves the bytes. |
| 23 | A file of `SendFile` that does not fit a session frame | It travels by the new path, as a large file value, instead of being dropped, and without a password: secure files and the others are kept apart ("Да, везти новым путём … НО пароль не надо в данном случае, надо разделять просто секьюрный файлы и нет"). |
| 24 | How such a file is stored | As a file on the disk, one approach for every large file, only as an archive ("файлы храним как файл на диске! давай один подход", "в архиве только"). |
| 25 | Which files of `SendFile` take the new path | Only those above a certain size ("больше определённого размера только"): a quarter of the session frame limit. |
| 26 | Who decides whether a file is secure | The application, at the send ("Приложение, при отправке"). |

How often dumps appear, the owner's estimate: about once a month. The consumers he named: the Aggregator and TTS, which crashes and leaves dumps the same way.

The rules below that are not in this table are the design's, not the owner's: they were added to make the decisions hold in the cases an independent review of 2026-11-25 raised (a restart between the check and the zip, two files with one time, a full disk in the middle, bytes that lie about their size, a file that changes while it is sent).

## Proposed Direction

### Terms

- **Large file value**: a value of a File sensor whose content is not part of the value. The value holds what describes the file (name, size, hash, state, the modification time of the source file); the content lies in the large file store and travels in pieces.
- **Dump**: a file with a snapshot of a process, delivered as a large file value. To HSM it is bytes.
- **Piece**: a fixed-size part of the source file (the last one may be shorter), compressed by itself. A piece number names a place in the file.
- **File connection**: the WebSocket of a collector session that carries pieces.
- **Dump password**: the password of a product under which its secure large file values are given out.
- **Secure file**: a large file value whose zip is encrypted under the product's dump password. A file that is not secure is packed into the same zip without encryption and without a password. The application decides at the send, per file; a file is secure unless it says otherwise, and a file of an ordinary send above the threshold is never secure.

### The collector

- The application names a folder, a name mask and a sensor path (`WatchLargeFiles(folder, mask, sensorPath, options)`), or hands over one file (`SendLargeFile(sensorPath, filePath)`). Both are additions to the public API. A file handed over or taken from a watched folder is secure unless the application says it is not (managed: `LargeFileOptions.Secure`, true by default; native: an options field whose zero value means secure).
- **An ordinary send above the threshold** (`SendFile`, `SendFileAsync`, a string content on the managed collector; `hsm_sensor_add_file`, `hsm_sensor_add_file_from_path`, `FileSensor::SendFile` on the native one; the legacy wrapper's `SendFileAsync` through the native collector) travels on a session as a large file value, in pieces on the file connection, instead of an element of a `list` frame that the collector drops when it does not fit. The threshold is a quarter of the frame limit the server announces for the session (`maxFrameBytes` / 4: 1 MiB at the default of 4 MiB), the size that fits a frame whatever the bytes are; it has no setting. At or below it nothing changes: a `list` frame, an ordinary file value, its preview. The limits of an ordinary send stay (`FileSensorOptions.MaxFileSizeBytes`, 10 MiB by default and 128 MiB at most; the native 10 MiB): the content is held in memory until the upload ends. Nothing is read from a watched folder, nothing is deleted from a disk, nothing is kept across a restart (as with the value queue), and a final refusal is counted as a dropped value. Such a file is never secure. With the HTTP transport nothing changes (`POST /file`); against a server without file connections the value is dropped, said and counted, as today.
- A file in a watched folder is taken when its size and modification time have not changed for N seconds and, on Windows, it opens for reading with no writer. Linux has no such check: there only the settle time decides. Files found at start are taken by the same rule, oldest first.
- For the whole upload the collector keeps the file open and, where the system allows it, denies writing and deleting. Before `end`, and again before it deletes the file, it compares the size, the modification time and the identity of the file (file id or inode) with what it began with. A file that changed is not finished: the upload is discarded on the server and begun again when the file has settled.
- One upload at a time, on one background thread of lowered priority: read a piece, compress it with zstd level 3, send it, wait for the acknowledgement. The collector holds one piece in memory.
- A speed limit, 10 MB/s by default, set by the application. It is kept by waiting between pieces; a piece, once begun, is sent at full speed, so the server's deadline for a frame holds whatever the limit is.
- The collector keeps a small state file in its data folder: which files it has delivered, each by name, size, modification time and identity. A new file under an old name is a new file. The file that was sent, and only that one, is deleted from the application's disk after the server answered `stored`. A per-folder option keeps the file instead.
- A refusal for space is not final: the file stays and is tried again later. A final refusal (a file over the limit, a sensor or a product that is gone, a key that may no longer send) leaves the file where it is, is reported once and is not tried again before the collector's next start.
- What the collector cannot see: a file whose writer died for good looks complete and is sent as it is.
- Large files need the session transport, as table sensors do.

### The wire: the file connection

A further WebSocket on `GET {base}session` with its own subprotocol (working name `hsm.files.v1`), tied to the live session by `X-Hsm-Session`, taking no session slot, as the table connection does. It is opened for an upload and closed after it. The loop, the heartbeat, the access check for every frame, the acknowledgements and the close codes are those of the table connection. A server then holds at most three sockets per session, not two.

Memory: file frames have a budget of their own, so a piece never takes the reservation of a table block. The rule of the table connection, one large-frame reservation per access key, is **not** carried over: collectors that share a key would queue behind each other's files. What bounds the memory instead is the number of uploads in progress on the whole server, a setting; an upload beyond it is refused at `begin`, to be tried again later.

- **Begin** (text request): the sensor path, the file's name, size and modification time, the piece size, and whether the file is secure (`"secure"`; a `begin` without it means secure, so an older collector or a mistake fails closed). The mark is stored in the upload's record and never changed afterwards. A file of an ordinary send above the threshold uses the same protocol. The server answers with the number of the next piece it wants (0 for a new file, N for one it holds a part of), or refuses before a byte of the file is sent: no room (below), a file over the limit, too many uploads in progress, a sensor that cannot take it. The piece size is fixed here and kept in the upload's record; a `begin` that names the same file with another piece size starts the upload again from piece 0. A part has one live upload: a second `begin` for it while the first connection is alive is refused as busy.
- **Room.** At `begin` the server reserves the announced size in the store's own accounting and keeps the reservation until the upload is packed or abandoned; the free-space check counts every reservation, so uploads that begin together cannot pass on the same free bytes.
- **Piece** (binary frame): a header (request id, upload id, piece number, the length of the piece before and after compression, a checksum of the compressed bytes) and the zstd frame of that piece. One in flight; the acknowledgement names the next piece. The piece size announced in the hello is the frame limit less the header and less the most zstd can add to bytes that do not compress, so a piece always fits a frame.
- **What a piece may be.** The number of pieces is the size divided by the piece size, rounded up, and no more. Every piece must decompress to exactly the piece size, the last one to the remainder. The server decompresses into a buffer of that size with a bounded window, so a small frame cannot unfold into gigabytes and the bytes sent cannot exceed the size that was checked against the limit and the free space. A piece that breaks this is answered `400` and the upload is discarded.
- **An acknowledgement means the piece is on the disk**: the piece and the upload's record are flushed to the disk before the answer. A write that fails, a full disk in the middle included, is answered `503` with a time to try again; nothing is acknowledged and nothing accepted before it is lost.
- **End** (text request): the hash of the whole file. The server reads what it stored, checks the hash and marks the upload **verified** in its record on the disk; then it answers `stored`. Only then the value exists, the alert fires and the collector may delete the source. A mismatch discards the upload, and the collector sends the file again.
- A connection that drops in the middle costs one piece. After a restart of either side the collector begins again with the same file and continues from the server's number.
- With the default frame limit of 4 MiB a link needs about 47 KB/s to pass the 90-second deadline of a reserved frame; below that no piece gets through, and the collector reports it.

Details to settle in the step: the header layout, whether the whole-file hash is a hash of the bytes or of the pieces' hashes (the second lets both sides resume without reading the file again), how the collector learns the result of a check that takes minutes.

Settled in #1884 (2026-11-25; the wire is `aicontext/features/api/wire-contract/feature.md`, "File connection"): an 88-byte header with the SHA-256 of each piece's original bytes; the hash in `end` is the SHA-256 of the pieces' digests, while the value shows the SHA-256 of the original bytes, which the end check computes on the way (the coordinator's decision); `end` is answered with the outcome of a check done within 2 seconds, otherwise `202 checking` and the collector asks again, after a cut with `begin`. The issue's test of a frame budget and its `1013` is replaced by the bound on uploads in progress: a file connection never waits for memory.

### Where the bytes live on the server

**Direction of the owner (2026-11-25): files on the server's disk, given out by Caddy.** The comparison with LevelDB was measured (below); the numbers are the record of why a second kind of store is justified here, when it was refused for table sensors (ADR-0021).

The two candidates:

- **A. LevelDB.** A database of its own for large files, one key per piece (`file id | piece number`), keys of a file only growing. One kind of store, as for table blocks.
- **B. Files (the direction).** One file per large value in a folder of the server; the value in the sensor's history holds the description.

Why files fit this data and did not fit table rows: a large file is written once, front to back, never changed, read as a whole or by byte range, and deleted as a whole. A web server gives a file out by itself, with ranges, without the application copying a byte. Table rows are many small records that are searched, merged and deleted by time, which is what LevelDB is for.

The layout:

- A folder set in the server's settings (it may be another volume than the databases). Two subtrees: one for uploads in progress and checked files, one for the archives that are given out. Only the second is ever shown to Caddy.
- A file on the disk is named by its id, a GUID the server makes; nothing a client sent is part of a path.
- An upload has a record beside its part and one of three states, written to the disk:
  - **uploading**: pieces are appended to `<file id>.part`. The 7 days count for this state only.
  - **verified**: the hash matched and the collector was told `stored`. From here the server owns the only copy and never removes it for age. The value exists.
  - **packed**: the archive is built and checked, and the part is removed.
- The archive is written under a temporary name and renamed into place when it is complete and its content was read back. A build that fails (a full disk, a fault of the library) removes the temporary file, keeps the part, is tried again and raises an alert. Before a build the server checks that the part and the archive fit together.
- A secure file of a product without a password stays **verified** until a password is set, then it is packed. A file that is not secure is packed at once, whether or not the product has a password.
- Every large file is stored as one zip with Zip64, built once after the check, whatever its kind: encrypted under the dump password for a secure file, without encryption for a file that is not secure.
- The value in the sensor's history points at the file by its id.
- At start: a part longer than its record says is cut back to the record; an **uploading** part waits for its collector its 7 days; a **verified** part goes on to its build; an archive without a value, or a value without its file, is reported and not silently fixed.
- **Removal is the store's own pass.** The cleanup of sensor history deletes a range of keys and never looks at the values, so the store compares its files with the values that exist and removes the files of values that are gone: by retention, by clear-history, with a removed sensor or product. Each removal is logged and counted.
- A File sensor that holds large values cannot be moved to another product in the first version: its secure archives lie under the first product's password. With ordinary sends above the threshold this reaches File sensors that are not set up for large files. A move inside the product changes nothing, the sensor keeps its id.

What the second store costs: two things to keep consistent (the value and the file), a folder that is not in the databases' folder, and a store that needs its own free-space check and its own retention pass.

### The store, measured (2026-11-25)

Both candidates were run the same way: a Linux container (Docker Desktop on WSL2, kernel 5.15), the data on a fresh ext4 volume for each scenario; `SoftFX.LevelDB.Standard` 1.2.2 opened as the table block store opens its database (no compression, 8 MiB write buffer); files of 8 GiB of incompressible bytes, as a compressed dump is; pieces of 1, 4 and 16 MiB. Most rows are single runs; the cold reads were repeated three times. Sizes here are GiB; the limit of 64 GB is the owner's figure and a setting in bytes. The code is a throwaway console project on the local branch `experiment/dump-store-bench`.

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

- LevelDB did not rewrite data while it took the pieces of one file at a time: no compaction, only moves of tables. Uploads that interleave their keys were not run. Its space overhead is at most 0.06%.
- From 2 to 8 GiB the times grew 3.8 to 4.7 times and the bytes written and the number of tables exactly 4 times. **Extrapolated to 64 GiB at pieces of 4 MiB, not measured:** a durable write of about 580 s into LevelDB and 520 s into a file, about 128 GiB written to the disk by LevelDB, 8,192 tables for one file and about 82,000 for ten.
- The wrapper cannot set LevelDB's table size, and its reads always return a new array: a read through its ordinary API allocates the whole file again on the managed heap.
- "Durable" here is a flush on the virtual disk of Docker Desktop, which may not reach the physical disk: the write times with a flush per piece are a lower bound, and the kill of the writer could not tell a flushed piece from one still in the system's cache.
- The resident memory of the LevelDB reader is mapped file pages: it counts in the container's memory and the system can take it back, unlike memory of the heap.
- Not measured: files of 64 GiB (the disk of the development machine), a power loss (only a kill of the process), a cache that is cold for certain, the production disk.
- Not measured at all, and the larger part of the work on a file of 64 GB: the check after `end` (reading and decompressing the whole file), the build of the archive, a re-encryption. They are measured in step 3 before its design is fixed.

What the numbers say: LevelDB can hold the pieces and loses none in a crash, at twice the disk writes and with a reader whose mapped memory grows by the size of what it reads. A file costs less at every row, and it is the only one of the two that a web server can give out without HSM touching the bytes.

### Giving a file out

- The only outward form is a zip with Zip64, holding the original file, decompressed from its pieces and compressed again by the zip, under its original name. A secure file's zip is encrypted with AES-256 (the WinZip AES format, AE-2) under the product's password; the zip of a file that is not secure has no encryption and no password. The classic zip password is not used: it can be broken when part of the content is known, and the header of a dump is known. An encrypted zip opens in 7-Zip and WinRAR, as a rule not in the archiver built into Windows.
- **What the password does not hide:** the name of the file inside, its sizes and its time are readable without it. A name of a dump often carries the host, the application and a process id.
- **The password has to be strong.** The format derives its key with 1,000 rounds of PBKDF2, so a short password a person made up is found by trying, offline, by anyone who has the archive. HSM therefore offers a generated password of 20 random characters or more; a password typed by a person has to pass a minimum length.
- **Where the password is.** Set and seen by a Manager of the product and by an admin, on a page of the web UI, signed in. It is stored in the server's configuration database as it is, because the server needs it to make a secure archive, and so it is in that database's backups. It is never part of an answer of `/api/v1` or of an MCP tool, whatever the token. A change is recorded in the journal as "dump password changed" without the old and the new value: the journal is readable by everyone who sees the product.
- While a product has no password, its secure large files are accepted and kept and cannot be downloaded. Its files that are not secure are downloaded as usual.
- **The zip is built once**, after the upload, and kept as the stored file. A download is then a plain file with byte ranges: a cut download continues, and no CPU of the server is spent on it.
- **A change of the password** re-encrypts the kept secure archives in the background; an archive that is not secure has no password version and is not rewritten. Every secure archive records the version of the password it was made under, and the job goes by that version, so an archive that was being built during the change is not missed. A replaced password is kept until its last archive is rewritten. A rewritten archive is a new file with a new id: a download of the old one that is continued later gets `404`, never bytes of two archives joined. Until an archive is rewritten it is given out under the password it has, and the page says which.
- **HSM decides, Caddy gives out** (owner, 2026-11-25). The download request comes to HSM: it checks the sign-in or the API token and that the caller may see the product, records the download, and answers with an instruction to Caddy to serve the file. The bytes go from Caddy, not through HSM. Only the subtree of finished archives is mounted into the Caddy container, read-only; parts and checked files are never there. No public route of Caddy reaches that folder: a file is served only as the answer to HSM's instruction. The path is built by HSM from the id of the file, parsed as a GUID, under a fixed root. Without Caddy (`docker-compose.direct.yml`) HSM serves the same file itself, with ranges.
- A link to a download works only with the sign-in or the token of the caller. There are no signed links: a link that carried its own permission would be a credential in a chat or in the transcript of an AI agent.
- A download that is cut must fail visibly for the client, never look like a whole file: the size is announced, the answer carries an `ETag`, and the hash of the archive is in a header and on the page.
- **Every download is recorded in a download log**: who (the user, or the token and its owner), when, which file. It is a record of its own and not the journal, which holds changes of configuration and never refuses an action when its write fails. A download that cannot be recorded is refused. One record covers the requests of one caller for one file within a short time, so a download manager that asks in ranges does not write hundreds. The bundled Caddy keeps no access log: the download log records that a download was allowed, and nothing records that it completed.

### Access

- Web UI: everyone who may see the product downloads the zip, of either kind; a file that is not secure also when the product has no password. An unknown file and a file of a product the user may not see get the same `404`.
- The download step relies on the web UI access-check work.
- API token: `GET /api/v1/...` streams the same zip to a token whose owner may see the product, with the size and the hash in headers. The MCP tool returns the description of a file and the link; the bytes never go through MCP. An AI agent gets the password of a secure file from its operator, not from HSM. The description of a file says whether it is secure.
- An access key (`Key` header) sends and never downloads a large file. `POST /api/sensors/history` returns the description of a large value, not its content.

### The sensor, retention, notification, UI

- A large value belongs to an ordinary File sensor, which may hold ordinary values and large values side by side. **Its time is the time the server accepted it**, made unique within the sensor. The modification time of the source file is a field of the description. (A value's key is the sensor and the time: two files with one modification time would overwrite each other, a file older than the last value would take the path of late values, and a file older than the history period would be removed right after its upload.)
- Retention is the sensor's "Keep sensor history", counted from that time: a value past its period is removed, and the store's pass removes its file. A half-uploaded file is removed 7 days after its last piece. A file that is being downloaded when it is removed is read to its end by the download in progress; a download continued later gets `404`.
- Before an upload the server compares the free space with a configured minimum, the reservations of the uploads in progress and the size announced; below it the upload is refused and the refusal is counted in a self-monitoring sensor with an alert.
- A sensor registered for dumps gets the alert "new data has been received" by default. New template variables carry the file name, the size and a link to the sensor page. The message is sent once, when the file is verified. A file of an ordinary send above the threshold adds no default alert to its sensor; the sensor's own alerts see it as a new value when it is verified.
- The sensor page lists the large values: name, size, time, application and host, the download button, whether the file is secure, and the state: uploading with a percentage, being checked, being packed, waiting for the product's password (secure files only), packing failed, ready, removed by retention. No preview, for either kind.
- Self-monitoring: uploads in progress, bytes stored, refusals, files removed, builds that failed, files waiting for a password.

## Open questions

- ~~How exactly HSM hands a file to Caddy~~ — settled by #1885: `reverse_proxy` with `handle_response` on HSM's `X-Hsm-Archive` answer (`aicontext/architecture/docker.md`).
- ~~The zip library for AES-256 and Zip64 on the server, and what a build and a re-encryption cost~~ — settled by #1885: SharpZipLib 1.4.2; the measured costs are in `aicontext/features/server/large-file-download/feature.md`.
- The compression ratio and speed on a real dump: needs a sample from the owner, measured on his machine, numbers only.
- The link speed between the application hosts and the server: it decides the default speed limit. (Dumps appear about once a month: at that rate and a history period of a few months the store holds a handful of files, and a crash loop is the exception the free-space refusal is for.)
- The retention period a dump sensor gets by default.
- Whether a product without a password should rather refuse uploads of secure files than keep files nobody can download.

## Work Breakdown

Epic #1882. One PR per step, or per part of a large step, into the integration branch `feature/1882-large-file-values`, cut from `feature/1842-table-sensors`.

| Step | Issue | Scope | Notes |
|---|---|---|---|
| 0 | — | The measurement of the store | Done, above |
| 1 | #1883 | Server: the large file store (files in a folder) and the large value of a File sensor: write, read, delete, retention, the free-space check, the check at start | No dependency on table sensors |
| 2 | #1884 | Server: the file connection (begin, piece, end, continue, limits, budget) | Reuses the session loop of #1845 |
| 3 | #1885 | Server: the product's dump password, the zip, the download for the web UI, `/api/v1` and Caddy, the download log, the MCP tool | Relies on the web UI access-check work |
| 4 | #1886 | Server: the sensor page, the alert variables, the default alert, self-monitoring | |
| 5 | #1887 | Native collector: reading a file in pieces, the watched folder, the state file, the speed limit; conformance scenarios | Binary frames and zstd shared with #1848, which it waits for. Consumer: the Aggregator |
| 6 | #1888 | Managed collector: the same behaviour, in parallel with step 5; whichever lands first writes the conformance scenarios, marks their verbs `CONFORMANCE-UNSUPPORTED: <verb> (#1887 or #1888)` in the other driver and keeps the scenarios out of that driver's run (native: not registered in `CMakeLists.txt`; managed: under `tests/conformance/collector/native/`, because the managed driver runs every scenario of the folder); the second step removes both | net6.0 and net472. Does not wait for #1848. Consumer: TTS |
| 7 | #1889 | Rollout: a real dump end to end, a file of 64 GB, the settings of production, the wiki | The order of the release: `hsm-caddy:2.11.4-9` (the hand-over rule, #1885) is published by the master run of `caddy-image.yml`; the reference compose in `wiki-git/Installation.md` names it already (the compose-wiki-sync check keeps that copy equal to `docker-compose.yml`), so the wiki is pushed only after that run has published the image |

## Risks

- A dump holds the memory of a process: keys, passwords, client data. With download open to everyone who sees the product, the password is the only protection of a secure file's content. An application that marks a dump not secure makes it readable by everyone who sees the product. A shared password cannot be taken back from one person, and the download log shows who downloaded, not who opened. An archive that left the server can be attacked offline for as long as it exists; its strength is the strength of the password.
- The server knows the password and holds a secure file without it while the file is uploaded and checked. The password protects a downloaded copy, not the server.
- A second kind of store beside LevelDB: the value and its file can disagree after a crash or a manual change of the folder; the check at start reports it.
- The bundled Caddy gains access to the folder of large files and a rule of its own; an installation with its own reverse proxy has to repeat that rule or let HSM serve the files.
- A collector that deletes the source file makes the server's copy the only one. Sensor history is not part of the server's backups.
- A crash loop produces a dump per crash; the server refuses when the disk is short, and the files pile up on the application's disk.
- The native collector gets its first code that reads a file as a stream; zstd and binary WebSocket frames come to it with table sensors (#1848), and this epic depends on them.
- The epic stands on a branch that is not in `master`.
- Public collector API, DTOs and the session protocol are compatibility-sensitive: every change here is an addition.

## Verification

- Server tests over a real WebSocket: begin, pieces, end; a cut and a resume at every point; a wrong hash; a refusal for space; the limits; access lost in the middle; `begin` with `secure` false, true and absent.
- The archive of a secure file opens with the password in 7-Zip and not without it; the archive of a file that is not secure opens in 7-Zip without a password; both hold the bytes that were sent.
- An ordinary send of a few megabytes on a session arrives as a large value that is not secure; one at or below the threshold arrives as an ordinary value; against a server without file connections the larger one is dropped and counted.
- A download test with ranges and with a cut connection; the access matrix (role, token, key, unknown id).
- Conformance scenarios in both drivers, the changed meaning of `file_larger_than_a_frame_is_dropped` included.
- One real dump of the owner's application, end to end, with the measured compression ratio, times and the disk written.
- The download through the bundled Caddy and without it (`caddy/test.sh`, which runs only in WSL on the development machine).
- `aicontext/` updated with each step.
