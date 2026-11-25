# ADR-0022: Large file values: pieces on a file connection, files on the server's disk, given out as a zip under the product's password

**Status:** Accepted
**Date:** 2026-11-25
**Supersedes:** —

---

## Context

An application with an embedded collector writes a dump when it crashes or when it is asked to. A dump is a file of up to tens of gigabytes that has to reach the developers. The owner wants it to go through HSM: the collector picks the file up and uploads it, it becomes a new value of a File sensor, users are notified, and a user or an AI agent downloads it.

A File sensor cannot carry such a file. A file value is one JSON document with a byte as a number; both collectors read the whole file into memory and cap it at 10 MiB by default; a session frame carries about 1 MiB of file; the server stores the value as one row of the sensor-values database, keeps the last one in memory and serves a download from an array. An API token gets a file's name and size and never its content. The facts with their places in the code are in `docs/initiatives/large-file-values.md`.

Table sensors (ADR-0021) built a second WebSocket of the collector session for heavy data: binary frames, one acknowledgement each, one in flight, a server-wide memory budget with one reservation per access key.

Two stores for the content were measured the same way (`docs/initiatives/large-file-values.md`): a Linux container, files of 8 GiB of incompressible bytes, pieces of 1, 4 and 16 MiB, each piece made durable before it is acknowledged; single runs unless stated.

- **Disk writes.** LevelDB, one key per piece in a database of its own, writes every byte twice, to its log and to a table; a file takes it once. LevelDB did not rewrite anything afterwards: no compaction while it took the pieces of one file at a time.
- **Write time for 8 GiB** at pieces of 1 / 4 / 16 MiB: 146 / 73 / 55 s into LevelDB, 93 / 65 / 12 s into a file.
- **On disk.** LevelDB holds a file of 8 GiB as 1,024 tables of 8 MiB; the wrapper cannot set the table size.
- **Reading a whole file back**, cold: 109–975 MB/s from LevelDB (the same read varies about twofold between runs), 1,162–3,346 MB/s from a file.
- **Memory of a reader.** LevelDB maps its tables: the resident memory of the process climbed to 7.7–8.5 GB while it read one file of 8 GiB. These are mapped file pages: they count in the container's memory, and the system can take them back. A reader of a file stayed at 29–46 MB.
- **A byte range of 1 MiB.** LevelDB reads the whole piece: 4–48 ms cold against 0.6–2.3 ms from a file.
- **Deleting one file of three.** In LevelDB deleting the keys frees nothing; the space comes back after a compaction of the range that the server asks for, 1.4–6.5 s. A file is gone in 13–85 ms.
- **A kill of the writer.** Both kept every acknowledged piece. The flush was one on the virtual disk of Docker Desktop, so the write times are a lower bound and a power loss was not tested.
- **Extrapolated to 64 GiB, not measured:** about 128 GiB written to the disk and 8,192 tables per file in LevelDB.
- **Not measured at all:** the check of a whole file after its upload, the build of the archive and a re-encryption, which are the larger part of the work on a file of 64 GB. Step 3 measures them before its design is fixed.

Constraints:

- The public collector API, the DTOs of `HSMSensorDataObjects` and the session protocol are compatibility-sensitive: additions only.
- The collector targets net6.0 and net472. The native collector depends on curl and nlohmann-json, has no streaming upload, no compression and a text-only session.
- A collector on a session sends nothing over HTTP (ADR-0019 and the owner's rule of 2026-11-10).
- A dump holds the memory of a process: keys, passwords, client data.
- Sensor history is not part of the server's backups.

## Decision

1. **A large file value is a value of a File sensor whose content is not part of the value.** The value holds the description of the file (name, size, hash, state, the modification time of the source file); the content lies in the large file store. The time of the value is the time the server accepted it, unique within the sensor: a value's key is the sensor and the time, so the time of the source file cannot be it. The mechanism is general, for any large file. A dump is a use of it, and what is specific to dumps is a setting of the sensor. Ordinary file values and `SendFile` do not change.
2. **The collector inside the application sends.** The application gives it a watched folder (a folder, a name mask, a sensor path) or one file. The HSM agent may switch the same function on later through its configuration.
3. **A file in a watched folder is taken when its size and modification time have not changed for a set time,** 60 seconds by default, and, on Windows, it opens for reading with no writer; files found at start by the same rule, oldest first. The collector keeps the file open for the whole upload and compares its size, time and identity with the beginning before it ends the upload and before it deletes the file; a file that changed is sent again. A file whose writer died for good is sent as it is: the collector does not know the format.
4. **One file is one value.** Nothing travels with it.
5. **The collector compresses on the fly, in pieces.** The source file is cut into pieces of a fixed size; each piece is compressed by itself with zstd level 3 from a library. There is no temporary archive on the application's disk, and the collector holds one piece in memory. A piece number names a place in the file.
6. **Pieces travel on a file connection:** a WebSocket of the collector session of its own, with its own subprotocol, tied to the live session as the table connection is, open only while a file is uploaded, with a memory budget of its own. A piece is a binary frame, acknowledged, one in flight; an acknowledgement means the piece is on the disk. The memory of file frames is bounded by the number of uploads in progress on the server, not by one reservation per access key. It is not the table connection: a piece never waits ahead of a block, nor a block ahead of a piece. There is no HTTP route for uploading in the first version; large files need the session transport.
7. **An upload begins with a question and ends with a check.** The collector names the file (sensor, name, size, modification time) and the server answers with the piece to send next, zero for a new file, or refuses before a byte is sent: the file is over the limit, the sensor cannot take it, or the disk has less free space than the configured minimum, the reservations of the uploads in progress and the announced size. The announced size is reserved until the file is packed or abandoned. A piece must decompress to exactly the piece size, within a bounded buffer and window; the number of pieces follows from the size. A write that fails is answered as a fault, to be sent again. At the end the collector sends the hash of the whole file; the server checks what it stored, records the file as verified on the disk and answers. Only then does the value exist.
8. **An upload that is cut continues from the server's piece number,** after a drop of the connection and after a restart of either side. A half-uploaded file is kept 7 days from its last piece, then removed and reported. It is not a value and cannot be downloaded. A verified file is never removed for age before it is packed: after the collector deleted the source it is the only copy.
9. **The upload is gentle to the application:** one upload at a time, one background thread of lowered priority, and a speed limit by default, 10 MB/s, which the application may change. The limit is kept between pieces, so a piece always meets the server's deadline for a frame.
10. **The collector deletes the source file after the server confirmed the whole file,** and only the file it sent, known by its identity. A per-folder setting keeps it instead.
11. **The limit on a file is a server setting;** the design carries files of up to 64 GB.
12. **The content lives in files on the server's disk,** in a folder of its own that may be another volume: a part while the file is uploaded and checked, then one archive. An upload is uploading, verified or packed, and the state is on the disk; an archive is written under a temporary name and renamed when it is complete; a build that fails keeps the part and is tried again. Files are named by ids the server makes. The store removes the files of values that no longer exist in a pass of its own, because the cleanup of sensor history deletes key ranges and does not look at values. It is not a LevelDB database.
13. **The only form a file leaves the server in is a zip with AES-256 encryption and Zip64 under the product's dump password,** for every role and every token. The server builds the zip once, after the check of point 7, and keeps it as the stored file. A change of the password re-encrypts the kept archives in the background, by the version of the password each archive records; a rewritten archive is a new file. The password is set and seen by a Manager of the product and by an admin on a page of the web UI; it is never returned by an API and its value is never written to the journal. HSM offers a generated password; one typed by a person has a minimum length. While a product has no password, its files are accepted, kept verified and not given out.
14. **Everyone who may see the product may download.** An API token downloads what its owner may download; there is no new token flag. An access key never downloads a large file. An unknown file and a file of a product the caller may not see get the same `404`.
15. **HSM decides and Caddy gives out.** A download request comes to HSM, which checks the caller, records the download and hands the file to the bundled Caddy to serve, with byte ranges; the bytes do not pass through HSM. Caddy sees only the folder of finished archives, read-only, and serves from it only on HSM's instruction; there are no signed links. Downloads are recorded in a log of their own, not in the journal of configuration changes, and a download that cannot be recorded is refused. Without Caddy, HSM serves the same file itself.
16. **Retention is the sensor's history setting, by age only.** There is no "last N", no volume quota and no mark that protects a file. When the disk is short the server refuses new files (point 7) and deletes nothing early; the refusal is counted in a self-monitoring sensor with an alert.
17. **A sensor set up for dumps has the alert "new data has been received" by default.** The message is sent once, when the file is stored, and carries the file name, the size and a link to the sensor page, through new template variables.
18. **Users see large values on the sensor page:** a list with name, size, time, state and the download; no preview.
19. **Both collectors get the feature, in parallel,** in one epic; the server part comes before both. The Aggregator runs on the native collector and TTS on the managed one, and both leave dumps. The managed collector already has its second connection and zstd from table sensors; the native collector waits for them (#1848), so the managed one is likely to reach production first. (First decided as native first; changed on 2026-11-25 when the owner named the second consumer.) The epic has an integration branch of its own, cut from the branch of table sensors.

## Consequences

- A dump reaches the developers without a person, also after a crash, as long as the application comes back. An application that stays down sends nothing until it runs again or an agent takes the folder over.
- The server's copy becomes the only one once the collector has deleted the source, and it is not in the backups.
- Protection of the content is the password. Download is open to everyone who sees the product; a shared password cannot be taken back from one person, and the download log shows who downloaded, not who opened. The format derives its key with 1,000 rounds of PBKDF2, so an archive that left the server is as strong as its password and can be attacked offline for as long as it exists. The name of the file inside, its sizes and its time are readable without the password. The server knows the password and holds a file without it while the file is uploaded and checked.
- An AI agent needs the password from its operator; HSM does not give it out through the API. The password lies in the server's configuration database as it is, and in that database's backups.
- HSM has a second kind of store. The value and its file can disagree after a crash or a manual change of the folder; the server compares them at start and reports a difference, it does not repair one silently. The folder needs its own free-space check and its own retention pass.
- The bundled Caddy gets read access to the folder and a rule of its own. An installation with its own reverse proxy repeats that rule or lets HSM serve the files.
- A download costs the server the check and the record, and a cut download continues. A change of the password costs a rewrite of every kept archive, and the server keeps a replaced password until its last archive is rewritten. What the build of an archive of 64 GB costs is not measured.
- A session may hold three sockets: the session, the table connection and the file connection. "At most twice `MaxSessions` sockets" of ADR-0021 and the wire contract becomes three times.
- "One store, LevelDB" of ADR-0021 was decided for table rows and stands for them. This ADR adds a store of another kind for another kind of data and supersedes nothing.
- A File sensor with large values cannot be moved to another product in the first version: its archives lie under the first product's password.
- A zip with AES opens in 7-Zip and WinRAR and, as a rule, not in the archiver built into Windows. The zip built into .NET cannot encrypt; the server needs a library for it.
- The native collector gains zstd, binary WebSocket frames and its first code that reads a file as a stream. The first two are shared with table sensors (#1848) and built once.
- In a crash loop every crash leaves a dump. The server refuses when its disk is short, and the files pile up on the application's disk; that disk is the application's concern.
- A collector on the HTTP transport cannot send a large file.
- The piece protocol is portable behaviour of the collector: it ships with conformance scenarios and verbs in both drivers.

## Alternatives Considered

- **Pieces in LevelDB**, one key per piece in a database of its own: one kind of store, as for table blocks. It works and loses nothing in a crash, but it writes every byte twice, turns one file into a thousand tables per 8 GiB, grows the mapped memory of the process by the size of whatever is read, and returns space only after a compaction the server has to ask for; and nothing in front of HSM can serve a download from it, so every byte of every download would pass through HSM. A large file is written once, read whole or by range and deleted whole, which a file does by itself and a web server serves without the application.
- **SeaweedFS:** rejected by the owner earlier; ten large files need neither many small objects nor several servers, and HSM would stand in front of it for access anyway.
- **The table connection for pieces:** it sends one frame at a time and gives one large-frame reservation to an access key, so a piece on a slow link would hold back the tick blocks of the same collector and the large frames of every collector with the same key.
- **HTTPS upload:** the usual way to upload a file and usable from a script, but the one place where a collector on a session would use HTTP. A route can be added later over the same store.
- **A zip made by the collector, with the password in the application's configuration:** the server and everything on the way would hold only ciphertext. But a zip entry is one continuous encrypted stream, so the upload either needs the whole archive as a temporary file on the application's disk or starts again after every cut; the password would lie on every host, a change would mean visiting them all, and a leaked password would open every stored dump for good.
- **The zip built for each download:** nothing is rewritten when the password changes, but every download costs the server the whole build again and a cut download starts again, and Caddy cannot serve it.
- **The original file for Managers and admins, the zip for the rest:** a resumable download without the password for those who manage it. The owner chose one form for everyone.
- **Download for Managers and admins only, without a password:** the first answer; replaced, because the Manager role is little used and a password lets the file be given to everyone who sees the product.
- **Caddy serving the folder with no check by HSM:** the simplest, but anyone with a link downloads without signing in and nothing is recorded.
- **Keeping the last N files, a volume quota, deleting the oldest when the disk is short:** the owner chose age only and a refusal.
- **The HSM agent as the sender:** independent of the application's life, but an agent on every host, the folders of every application in its configuration, and the dump outside the application's place in the tree. Left for later, over the same collector function.
- **The classic zip password:** breakable when part of the content is known, and the header of a dump is known.
- **The modification time of the source file as the time of the value:** it reads naturally, but two files with one time would overwrite each other under the key of a value, an old file would take the path of late values, and a file older than the history period would be removed right after its upload.
- **One large-frame reservation per access key, as on the table connection:** collectors that share a key would queue behind each other's files; the number of uploads in progress bounds the memory instead.

## References

- The facts, the owner's answers and the work breakdown: `docs/initiatives/large-file-values.md`
- ADR-0017 (collector session), ADR-0019 (session as the default transport), ADR-0021 (table sensors: the second connection, "one store, LevelDB")
- Epic #1882, steps #1883–#1889
- Epic #1842, step 6 (#1848): binary frames and zstd in the native collector
