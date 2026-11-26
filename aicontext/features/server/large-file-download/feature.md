# Feature: Large file download (archive, download log, hand-over to Caddy)

> Owner: server | Last reviewed: 2026-11-26 | Canonical: yes
> Scope: how a large file value leaves the server (#1885, ADR-0022 points 13–15): the zip the server builds once per file (for a secure file, encrypted, once per dump password version; for a file that is not secure, without encryption), the background builder that packs and re-encrypts, the download for the web UI and the management API with its binding order of checks, the hand-over of the bytes to the bundled Caddy, the download log, and the self-monitoring of both. The dump password itself is `../dump-password/feature.md`; the store that holds parts and archives is #1883.

---

## Overview

A large file value (glossary) reaches the server as a part of zstd pieces and is checked against the SHA-256 of the original bytes by the large file store (#1883). This feature turns a verified file into the only form in which it ever leaves the server: a zip with one entry (Zip64, Deflate). A file is **secure** or **not secure** (glossary "Secure file"): the application decides at the send, the store keeps the mark in the file's record (a record without it is secure) and it never changes. A secure file's zip is encrypted with AES-256 under the dump password of the file's root product; a file that is not secure gets the same zip without encryption and without a password. The archive is built once, kept as the stored file and given out as a plain file with byte ranges. When the product's password changes, the kept secure archives are re-encrypted in the background, each under a new archive id; an archive that is not secure is never rewritten.

Everyone who may see the product may download either kind, from the web UI with the sign-in cookie or from `/api/v1` with an API token acting as its owner. An access key never downloads. Every allowed GET is written to the download log first; a download that cannot be recorded is refused. Behind the bundled Caddy, HSM decides and Caddy moves the bytes; without it, HSM serves the file itself.

**The store behind the seam.** The archive side talks to the store only through `ILargeFileArchiveStore`. On the server that seam is `LargeFileStoreArchiveAdapter` over the large file store of #1883 (`IDatabaseCore.LargeFiles`, `../../storage/large-files/feature.md`); the tests of the archive side alone use fakes over a temporary folder, and the end-to-end tests run the adapter over the real store. The adapter keeps nothing of its own: every call reads the store. It maps the store's record (`Secure` → `IsSecure`, `Hash` → `ContentSha256`, `ArchiveHash` → `ArchiveSha256`, the three states, a password version of 0 read as "no version"), forwards the store's verified event until it is disposed, reads the part length on the disk from the part the store opens, and maps the begin, commit and abandon of a build with their outcomes. `ArchivesFolder` is `<store folder>/archives`, the subtree the bundled Caddy mounts.

## Invariants

- **One outward form.** A file leaves the server only as a zip with one entry, Zip64 on, for every role and every token: for a secure file WinZip AES-256 (AE-2) under the dump password of its root product, for a file that is not secure without encryption. The bytes never go through MCP, and no API or tool answer carries a password.
- **The kind is the application's, once.** Secure or not secure is decided at the send and kept in the store's record; nothing on the server changes it. A record without the mark is secure. A file that is not secure never waits for a password, is never re-encrypted, carries no password version (the store commits it with version 0, read as "no version") and keeps no replaced password version alive. A secure file is never read back from an unencrypted archive, nor one that is not secure from an encrypted one (`IArchiveWriter.OpenEntry` refuses both).
- **Built once, checked before it counts.** An archive is written to a temporary file on the archives' volume, flushed to disk, opened again with the password it was written under and read to its end; the decompressed size and SHA-256 must equal those of the original bytes. Only then is it committed through the store. A broken archive never replaces a part or an older archive.
- **One builder for the whole server.** `ArchiveBuilder` is a singleton and runs one build at a time (a semaphore around the pass; the background loop never overlaps its runs). Its status lives in memory and only for files with work pending or a failure reported; the work to do is listed from the store on every pass, so a restart loses nothing.
- **A password version is taken once per build** (secure files). A build reads (password, version) at its start and commits under that version, whatever changes meanwhile; a later pass re-encrypts it if the product's current version is newer.
- **Re-encryption makes a new archive.** A re-encrypted archive has a new id; the link to the old one answers 404 once the new one is committed. A transfer that already opened the old file reads it to its end; a download continued later never receives bytes of two archives.
- **The order of checks is binding** (see the table below), the same for both kinds. Everything that may not disclose a file answers the same 404 of its surface, never a 403.
- **No error answer carries the hand-over header.** `X-Hsm-Archive` is set only on an allowed download, after the download record was written, as the last header of the answer.
- **The download log records that a download was allowed.** Nothing records that it completed. A GET that cannot be recorded is refused with 503; a HEAD is not recorded.
- **No password in any text.** Statuses, events, log lines, exception texts and self-monitoring comments of the builder name the file, the product and the version only; exceptions of the zip library are reduced to their type name before they are shown. Pinned by tests.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Pack a verified secure file under the product's current password | background builder |
| 2 | Keep a verified secure file waiting while its product has no password; pack it once one is set | background builder |
| 3 | Pack a verified file that is not secure at once, without encryption, whether or not the product has a password | background builder |
| 4 | Re-encrypt every secure archive of a product after its password changed, one at a time | background builder |
| 5 | Download an archive in the web UI (`GET /LargeFiles/Download/{fileId}/{archiveId}`) | signed-in user |
| 6 | Describe a file and get its download link (`GET /api/v1/sensors/{sensorId}/large-files/{fileId}`, MCP `get_large_file`) | API client, AI agent |
| 7 | Download an archive through the API (`GET`/`HEAD /api/v1/large-files/{fileId}/archives/{archiveId}`), with ranges and resumption | API client, AI agent |
| 8 | Remove download records older than the retention, once a day | background service |

## The archive

- **Library.** SharpZipLib 1.4.2 (MIT), pinned exactly (`[1.4.2]` in `HSMServer.Core.csproj`). It writes WinZip AES-256 AE-2 (HMAC-SHA1, CRC zeroed), Zip64 and a data descriptor to a forward-only stream, and takes AES and PBKDF2 from `System.Security.Cryptography`. It is used in one class only, `SharpZipArchiveWriter`, behind the narrow `IArchiveWriter` (write one entry with a password or with none; open the only entry with a password, or with none), so it can be replaced without touching the builder. The zip built into .NET cannot encrypt. Of the libraries compared, SharpCompress writes no encrypted zip, DotNetZip is deprecated, and the ProDotNetZip fork packed 27 MB/s against SharpZipLib's 149 MB/s in one run on 256 MiB of random bytes without compression.
- **Shape.** One entry; `UseZip64.On` always; `AESKeySize = 256` for a secure file, no encryption (no AES extra field, no password) for one that is not secure; an empty password is refused, null means "no encryption". Opening with a password refuses an entry that is not AES-256; opening without one refuses an encrypted entry. The output is not seekable, so the library writes a data descriptor and the bytes written are the bytes of the archive. The builder hashes them as they pass (SHA-256 and length of the archive), so the archive is never read again for its hash.
- **Compression.** Deflate at `LargeFileArchives.DeflateLevel`, 1 by default. Level 1 against 6 on dump-like data: an archive about 4% larger, a build about 1.7 times faster (measurements below). 0 stores the content without compression (a secure file is still encrypted); 9 is the smallest and slowest. The same level applies to both kinds.
- **Entry name** (`ArchiveEntryNames.Sanitize`): the original name the collector sent, reduced to a bare file name that every archiver extracts into the folder the user chose. Only the part after the last `/` or `\` is kept; a drive prefix such as `C:` is dropped; control and format characters (bidi overrides, zero-width marks) are removed; the characters Windows refuses in a name (`<>:"|?*`) become `_`; leading spaces and trailing dots and spaces are trimmed, so `..` is not a name; a Windows device name (`CON`, `NUL`, `COM1`…) gets a leading `_`; the extension the collector sent (up to 32 characters, no spaces) is appended when the name has none; the result is at most 200 characters, cut in the stem without splitting a surrogate pair; an empty result is `file`. The name is stored as UTF-8 with the zip's UTF-8 flag (general purpose bit 11, SharpZipLib's default for a new entry), so a Cyrillic name reads the same in every archiver.
- **Entry time:** the modification time of the source file, written as the zip's DOS time without a zone (an archiver shows the UTC time), clamped to 1980–2107.
- **Source.** A first pack reads the verified part (zstd frames, one per piece, decoded as one stream with ZstdSharp.Port 0.8.8). A re-encryption reads the current archive, opened with the password of the version it records. In both cases the content must be exactly the size of the original file.

## Building and re-encrypting

`LargeFileArchiveBackgroundService` runs `IArchiveBuilder.RunDueAsync` every minute, and at once after the store reports a verified file or a dump password changes; signals that arrive during a run coalesce into one more run. Each pass:

1. Lists the verified and the packed files of the store. A verified file that is not secure is due for its **first pack** at once, whatever the password. A verified secure file of a product without a password becomes **waiting for password** and stays verified; of a product with a password, it is due for its **first pack**. A packed secure file whose password version is older than the product's current one is due for **re-encryption**; a packed file that is not secure is not looked at. A packed file whose version is not kept (it cannot be opened) or is newer than the current one is **failed** with an error naming the version (`the password version N of this archive is not kept`), without a next attempt: the failure is reported once per file and version per process and the builder does not retry it; the archive stays downloadable as it is. Files whose next attempt lies in the future are skipped. A pass builds from this one listing (one listing of the verified and one of the packed files, not one per build): the store is listed again only when a file was verified, a password changed or a commit was superseded during the pass, once the listed work is done or after `BuildsPerListing` (64) builds, whichever comes first, so a file verified during the pass is built in the same pass. A file is never built twice for the same state in one pass. The statuses of files that are packed under the current state or that the store no longer knows are dropped at each listing, so the status map holds only files with work pending or a failure reported. Ordinary sends above about 1 MiB make many small files: the listings of a pass grow with the number of new files, not with the number of builds times the number of files.
2. Builds the due files one after another:
   - reads the record again; a file removed or in another state meanwhile is dropped;
   - a secure file takes the current (password, version); a file that is not secure takes none (and a re-encryption of one is dropped); the record must carry the SHA-256 of the original bytes;
   - first pack: the part's length on the disk must equal the length the record covers, else the build fails; re-encryption: the version the archive records must still be kept, else the build fails and the old archive stays;
   - marks the versions it reads and writes as in flight, so they are not retired while it runs;
   - `BeginArchive`: the store answers with a new archive id and a temporary path, with the room reserved, or **no room** (the file waits for room, with the store's retry time or the backoff), or not found / wrong state (the file is dropped without a retry);
   - writes the archive to the temporary path, flushes it to the disk, reads it back and compares the size and the SHA-256 with the original. A full volume during the build (an `IOException` with `ERROR_DISK_FULL`/`ERROR_HANDLE_DISK_FULL` on Windows or `ENOSPC`, HResult 28, on Linux; space taken outside HSM after the store's reservation) abandons the temporary archive and makes the file **wait for room** with the backoff, as a no room at the begin does, not fail;
   - `CommitArchive(target, version, length, sha256)` (version 0 for a file that is not secure: the store keeps no version): the store renames the archive into place durably, writes the record as packed and removes the part (first pack) or the replaced archive (re-encryption). **Superseded** (another archive was committed first) changes nothing and the pass lists the store again to look at the file; **removed** (the file went away during the build) drops it.
3. Retires the password versions that no secure archive carries and no build in flight uses (from one listing of the packed files at the end of the pass), then removes the retired versions whose retention ended (`../dump-password/feature.md`). Products that have a password but no file seen since the start are included.

**A build that fails** abandons its temporary file (the part, or the old archive, is kept), is reported, and is tried again after 1, 5, 30 and 60 minutes, then hourly. A file removed during a build that fails is dropped without a retry. A pass cancelled by the server's shutdown abandons its build, counts no failure and starts again on the next start. **Waiting for room** is a state of its own, not a failure, though it is reported through the same self-monitoring sensor and alert.

Build states, as the description of a file shows them (`buildState`): `none`, `waitingForPassword`, `queued`, `building`, `waitingForRoom`, `failed`. Counters since the start (`ArchiveBuildStatistics`: built, of them built secure and built not secure, re-encrypted, failed, waited for room, versions retired) are kept in memory. `waitingForPassword` is a state of secure files only.

## The download

### Routes

| Surface | Route | Authentication |
|---|---|---|
| Web | `GET /LargeFiles/Download/{fileId}/{archiveId}` (`LargeFilesController`) | the sign-in cookie, like every page; a bearer token does not open it |
| API | `GET` and `HEAD /api/v1/large-files/{fileId}/archives/{archiveId}` (`LargeFilesApiController.DownloadArchive`) | `HsmApiToken` bearer, `ManagementPolicy`, `[ManagementApi]` area; a read-only token downloads |
| API | `GET /api/v1/sensors/{sensorId}/large-files/{fileId}` (`LargeFilesApiController.GetLargeFile`) | the same; the description with the link (below) |

A link names both the file and the archive, so a link to a replaced archive answers 404. There are no signed links: a link works only with the caller's own sign-in or token.

### The order of checks

`LargeFileDownloadService.Decide`, the same for both surfaces and for both kinds:

1. The store knows the file — else 404.
2. The file's sensor exists and its root product is still the product the file was uploaded to — else 404 (a removed sensor, or a sensor moved to another product).
3. The caller sees that product: a web user through `User.IsProductAvailable(root)`; a token through `ApiTokenAuthorizationService.AuthorizeRead` on the sensor resource — else 404.
4. The state: packed goes on; verified answers 409 with the reason; uploading answers 404 (it is not a value yet).
5. The archive id in the link is the file's current archive, and the record names its length, hash and, for a secure file, its password version — else 404.
6. A GET is written to the download log — else 503. A HEAD is not recorded.
7. The answer: the hand-over to Caddy, or HSM serves the file. When HSM serves, the archive is opened first and the record is read again after the open; an archive replaced in between answers 404, never the new archive's bytes under the old link. An archive the store cannot open (an I/O error, access denied, any other fault of the store) answers the same 404 and is logged with the exception's type and message.

### Statuses

| Case | Web | API |
|---|---|---|
| Allowed | 200, or 206 for a range (from HSM or from Caddy) | the same |
| Not signed in / no token | 302 to the sign-in page | 401 `unauthorized` |
| An access key (`Key` header) | — | 401 |
| Unknown file, a product the caller may not see, a file in upload, a removed or moved sensor, an archive id that is not current | 404, plain text `The file was not found.`, the same for every cause | 404 `not_found`, the area's single not-found body |
| Visible but not packed yet | 409, plain text reason | 409 `conflict`, the reason as `message` |
| The download log cannot be written | 503, plain text | 503 `service_unavailable` |

The 409 reasons: the product has no dump password yet (a Manager sets it in the product settings; a secure file only: a file that is not secure never answers it); the archive is being packed; the archive waits for free disk space; packing failed and is tried again. A packed file being re-encrypted stays downloadable under its current archive until the new one is committed.

### Headers

On an archive answer:

- `Content-Disposition: attachment; filename="<ASCII fallback>"; filename*=UTF-8''<name>`, built with `ContentDispositionHeaderValue`. The name is the entry name (`ArchiveEntryNames.Sanitize`, above) with `.zip` appended, so control and format characters such as bidi overrides are dropped from it exactly as from the entry; the plain `filename` keeps printable ASCII only, without `"`, `\` and `%`.
- `Repr-Digest: sha-256=:<base64>:` — the SHA-256 of the archive bytes (RFC 9530). The description gives the same hash as lowercase hex.
- `X-Hsm-Password-Version: <N>` — the version of the product's dump password a secure archive opens with. **Absent** (not empty) on the answer for an archive that is not secure: it opens without a password.
- `Content-Type: application/zip`.
- When HSM serves: `Accept-Ranges`, `Content-Range` on 206, `ETag: "<archiveId:N>"`, `Last-Modified` (the archive file's time), `If-Range` resumption, and `Cache-Control: private, no-store`. The management API controller is also marked `NoStore`.
- When Caddy serves: Caddy's `file_server` gives the ranges and its own `ETag` and `Last-Modified`; of HSM's answer it copies `Content-Disposition`, `Repr-Digest`, `X-Hsm-Password-Version` and `Cache-Control` (`copy_response_headers` copies the headers that are there, so a not-secure answer reaches the client without `X-Hsm-Password-Version`; the Caddyfile is the same for both kinds).

### The hand-over to the bundled Caddy

The rule lives in the `hsm_upstream` snippet of `caddy/Caddyfile`, so both sites carry it (image `hsmonitoring/hsm-caddy:2.11.4-9`):

- Caddy sets `X-Hsm-Serve-Archives: 1` on every request it forwards (`header_up`, which replaces a value sent by the client).
- HSM honours the marker only on a connection whose own peer address is a configured trusted proxy. `ConnectionPeerMiddleware`, registered right before `UseForwardedHeaders` with the same options, records the peer before the forwarded headers replace it, and judges it against the same `KnownProxies`/`KnownNetworks` (`attached-networks` included). A client's own `X-Original-For` therefore changes nothing. Nothing recorded (no `Kestrel.TrustedProxies`) means not trusted. Otherwise HSM serves the file itself, even when the header is present.
- On an allowed download through the proxy HSM answers with an instruction instead of the bytes: status **503**, a short text body, the archive headers above, and, last, `X-Hsm-Archive: <productId:N>/<archiveId:N>.zip` (a path built from two GUIDs). The path is the store's own path of the archive relative to `ArchivesFolder`, so the hand-over and the store cannot drift apart, and it is accepted only when it equals `LargeArchivePaths.RelativeArchivePath(productId, archiveId)` of the record: nothing a client sent ever reaches it. A store that places the archive elsewhere (or cannot say where) makes the download answer the same 404 as an unknown file, logged as an error. Caddy's `handle_response` consumes that answer and serves `/srv/hsm-archives/<X-Hsm-Archive>` with `file_server`. The 503 and the instruction header never reach the client; a client that receives the instruction anyway sees an error, never an empty file that looks complete.
- Caddy mounts only the `archives/` subtree of the large files folder, read-only, and has no route of its own to it. A file the instruction names that is not there answers 404 with no body.
- Without the bundled Caddy (`docker-compose.direct.yml`, a plain `docker run`, a proxy that does not set the marker) HSM serves the same file itself, with ranges.

The deployment side (mounts from `HSM_LARGE_FILES`, another reverse proxy, `caddy/test.sh`) is in `aicontext/architecture/docker.md`, section "Large file downloads: HSM decides, Caddy gives out".

A cross-site page can start a GET download in a signed-in user's name, as with any download link: it cannot read the bytes, and the download log gets a record.

## The download log

- **Store.** A LevelDB database of its own, `Databases/DownloadLog` (`DownloadLogCollection`), opened on its first use. Key: UTC ticks (8 bytes, big-endian) then the record id (16 bytes), so a time range is a key range; value: the record as JSON. It is not the journal, and it is not part of the configuration database's backups.
- **A record:** id, time, product, sensor, file id, archive id, password version (0 for a file that is not secure), the file name of the download, the channel (`web` or `api`), the user (id and name; for a token, its owner), the token (id and name) and the client IP.
- **Coalescing.** The requests of one caller for one archive make one record while each arrives within `DownloadLogWindowMinutes` (10) of the one before it: a sliding window that every request renews, so a download in ranges or one continued after a break is one record. The caller is the user, or the token (a token is a caller apart from its owner). The windows live in memory, at most `DownloadLogMaxCallers` (10,000) of them: when the map is full the ended windows go first, then the oldest. After a restart the next request makes a record again. The write runs under a lock, so two requests of one caller at once make one record.
- **A write that fails** is counted, logged and thrown: the download answers 503 and no window is opened, so the next request tries to write again. The self-monitoring sensor below turns Error.
- **Reading.** `IDownloadLog.Read(productId, from, to, limit)` returns a product's records newest first; `IDownloadLog.ReadFile(productId, fileId, limit)` returns one file's records (every archive of it) newest first over the whole retention, the limit counted after the file filter (the store's read takes an optional file id). In this step they are Core queries with their tests; the reading surface is the sensor page of #1886, for Managers of the product and admins.
- **Retention.** `DownloadLogRetentionDays` (365). The background service removes older records once a day, the first time on its first pass after the start.

## The access-key history read

An access key never downloads, but it reads the history of a File sensor (`POST /api/sensors/history`, `POST /api/sensors/historyFile`, `SensorsController`). A large file value there gives its description, never its content: in `history` an additive `LargeFile` member (`FileId`, `Name`, `Extension`, `Size`, `Secure`, `State`) beside the usual members, with an empty `Value`; in `historyFile` an empty content cell and a last `Large file` column (`Large file <fileId>; <size> bytes; secure|not secure; <state>`), present only when the values hold a large one. `Secure` and `State` come from the store's record: `verified`, `packed`, `removed` (the store no longer holds the file; `Secure` null) or `unavailable` (the store could not be read; the history is still answered). An ordinary file value keeps its shape and its content. The contract: `aicontext/features/api/overview.md`.

## Self-monitoring

Two Bool sensors in the node `Large files` of the server's self-monitoring product, beside the large file store's counters (the same class, `LargeFileSensors`, created by `DataCollectorWrapper`); each is created at its first report, so a server that never built an archive has neither. Unlike the other self-monitoring sensors, both carry a default alert: on a change to Error, a notification and the Error icon.

| Sensor | Type | Posted | Error means | Default alert text |
|---|---|---|---|---|
| `Large files/Archive build result` | Bool | once per build attempt (first pack or re-encryption) | the build failed, or the disk had no room for it; the comment names the file and the reason; the file is kept and tried again (an archive whose password version is not usable: reported once, not retried) | `[$product] Large file archive build failed: $comment` |
| `Large files/Download log` | Bool | when its state changes | a download record could not be written: every download answers 503 meanwhile | `[$product] Large file download log cannot be written, every download is refused: $comment` |

Both have no TTL and are available to Grafana. A failing post never breaks a build or a download.

## Configuration

Section `LargeFileArchives` of the server settings, read once at the start; a value out of range stops the start with a message that names the key.

| Key | Default | Range | Effect |
|---|---|---|---|
| `DeflateLevel` | 1 | 0–9 | Deflate level of new archives; 0 = no compression |
| `PasswordRetentionDays` | 30 | 0–3650 | How long a replaced dump password stays readable after its last archive was re-encrypted |
| `DownloadLogWindowMinutes` | 10 | 1–1440 | The sliding window of one download record |
| `DownloadLogRetentionDays` | 365 | 1–3650 | Age after which download records are removed |
| `DownloadLogMaxCallers` | 10,000 | 1–10,000,000 | The windows the download log keeps in memory |

Fixed in code: the builder's run period (1 minute), the backoff (1, 5, 30, 60 minutes, then hourly), the daily cleanup of the download log. Deployment: `HSM_LARGE_FILES` (the host folder of large files, `./LargeFiles` by default) and `Kestrel.TrustedProxies` (`aicontext/architecture/docker.md`).

## What an archive costs (measured 2026-11-25)

Files of 8 GiB made of zstd pieces of 4 MiB, packed with SharpZipLib 1.4.2 in a Linux container (Docker Desktop on WSL2, Intel Core Ultra 7 270K), one thread, single runs. "Dump-like": pages of zeros, pointers, text and random bytes that zstd level 3 compresses 3.43 times. "Random": incompressible.

| 8 GiB | Wall time | CPU | Peak RSS | Written to the disk |
|---|---|---|---|---|
| dump-like, build, Deflate 6 | 221 s | 237 s | 67 MB | 2.74 GiB (the archive) |
| dump-like, build, Deflate 1 | 131 s | 139 s | 66 MB | 2.86 GiB |
| dump-like, build, no compression (AES only) | 111 s | 107 s | 68 MB | 8.0 GiB |
| dump-like, read-back check | 63–73 s | 67–77 s | 57 MB | 0 |
| dump-like, re-encryption, Deflate 6 | 265 s | 285 s | 63 MB | 2.74 GiB |
| dump-like, re-encryption, Deflate 1 | 191 s | 204 s | 66 MB | 2.86 GiB |
| random, build, Deflate 1 | 285 s | 296 s | 67 MB | 8.0 GiB (archive +0.03%) |
| random, read-back check | 128–142 s | 136–138 s | 38 MB | 0 |
| random, re-encryption, Deflate 1 | 504 s | 498 s | 64 MB | 8.0 GiB |

A first pack in the code is a build followed by a read-back check; a re-encryption is followed by one too. One core does the work and the memory does not grow with the size of the file. Without compression the build still runs at about 77 MB/s, so the encryption and the library's managed Deflater set the pace (an inference from these runs, not a profile).

**Extrapolated linearly to 64 GB, not measured:** dump-like — build about 16 minutes at Deflate 1, check about 9, re-encryption about 24; random — build about 35 minutes, check about 16, re-encryption about 63. On the disk: the archive is about the size of the compressed file (about 22 GB for dump-like data), plus the part until the commit removes it.

**Caveats.** A fast desktop CPU: a production server may be two to three times slower. The 8 GiB lay in the page cache; 64 GB will not. The dump-like data is synthetic. A real dump is measured in the rollout step (#1889), which also confirms the default Deflate level.

**Interoperability.** 7-Zip 25.01 on Linux tested an 8 GiB archive of this builder: with the password, "Everything is Ok" and the extracted content's SHA-256 equal to the sent file; with a wrong password, "Wrong password"; with none, it asks for one. `7z l -slt` lists `AES-256 Deflate` and `Zip64 WzAES`.

**Not secure, larger than 4 GiB (measured 2026-11-26, same machine and container).** 5 GiB of random bytes, written without a password through the builder's settings (Zip64 on, Deflate 1, forward-only output): the build took 188 s (one core, peak RSS 62 MB), the archive is 5,370,341,519 bytes, and the read-back check of the content's SHA-256 passed. 7-Zip 25.01 on Linux: `7z l -slt` lists `Encrypted = -`, `Method = Deflate`, `Characteristics = Zip64 : Descriptor UTF8`; `7z t` without a password answers "Everything is Ok"; `7z x -so` gives content whose SHA-256 equals the sent one. Not part of the test suite (its size); the suite checks the same with a 2 MiB archive.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `GET /LargeFiles/Download/{fileId}/{archiveId}` | `Controllers/LargeFilesController.cs` | Web download, cookie-authenticated; statuses above |
| `GET`/`HEAD /api/v1/large-files/{fileId}/archives/{archiveId}` | `Controllers/LargeFilesApiController.cs` | API download; see `../management-api/feature.md` |
| `GET /api/v1/sensors/{sensorId}/large-files/{fileId}` | `Controllers/LargeFilesApiController.cs` | `LargeFileDto`: `fileId, sensorId, name, extension, size, sourceModifiedUtc, secure, state ("verified"/"packed"), buildState, notReadyReason, archive {id, size, sha256 (hex), passwordVersion, passwordSetUtc, downloadUrl}`; `passwordVersion` and `passwordSetUtc` are null for a file that is not secure; no password |
| `LargeFileDownloadService.DescribeFile(fileId, baseUrl)` | `Model/ManagementApi/LargeFiles/LargeFileDownloadService.cs` | The same `LargeFileDto` without any caller check, for a surface that already decided who sees the file (the sensor page, #1886); null when the store does not know the file, it is still uploading, or its sensor is gone or in another product. The caller checks the answer's `SensorId` against the sensor it shows and links a signed-in user to `WebDownloadPath` (`DownloadUrl` is the token route). The four not-ready reasons are public constants |
| MCP `get_large_file(sensorId, fileId)` | `Mcp/LargeFilesMcpTools.cs` | The REST description's twin; see `../mcp/feature.md` |
| `X-Hsm-Serve-Archives` (request), `X-Hsm-Archive` (response) | `Model/ManagementApi/LargeFiles/LargeFileDownloadResult.cs`, `caddy/Caddyfile` | The hand-over between HSM and the bundled Caddy |
| `ILargeFileArchiveStore` | `HSMServer.Core/LargeFiles/Archives/ILargeFileArchiveStore.cs` | The seam to the store of #1883: `FileVerified`, `TryGetFile`, `GetFiles`, `OpenPart`, `GetPartLengthOnDisk`, `OpenArchive`, `ArchivesFolder`, `ArchivePath`, `BeginArchive`/`CommitArchive`/`AbandonArchive`; archive path `<productId:N>/<archiveId:N>.zip` under `ArchivesFolder`. Implemented by `LargeFileStoreArchiveAdapter` |
| `LargeFileDownloadService.DescribeValue(sensorId, fileId, baseUrl)` | `Model/ManagementApi/LargeFiles/LargeFileDownloadService.cs` | The `largeFile` block of a File sensor value in the sensor reads of `/api/v1` and MCP (`../management-api/feature.md`): `DescribeFile` reduced to the file id, `secure`, `state`, `notReadyReason` and `archive`; `removed` when the store no longer holds the file as a value of that sensor, `unavailable` when the store cannot be read |
| `IArchiveWriter` | `HSMServer.Core/LargeFiles/Archives/IArchiveWriter.cs` | The only place the zip library is reached |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/LargeFiles/Archives/ArchiveBuilder.cs` | The builder: passes, first pack, re-encryption, read-back, backoff, password retirement |
| `src/server/HSMServer.Core/LargeFiles/Archives/SharpZipArchiveWriter.cs` | `IArchiveWriter` over SharpZipLib: one AES-256 entry, Zip64, Deflate |
| `src/server/HSMServer.Core/LargeFiles/Archives/ArchiveEntryNames.cs` | The entry name rules |
| `src/server/HSMServer.Core/LargeFiles/Archives/ILargeFileArchiveStore.cs` | The seam to the store, its records and outcomes |
| `src/server/HSMServer.Core/LargeFiles/Archives/LargeFileStoreArchiveAdapter.cs` | The seam over the large file store of #1883 |
| `src/server/HSMServer.Core/LargeFiles/Downloads/DownloadLog.cs` | Coalescing windows over the download log store |
| `src/database/HSMDatabase/DatabaseWorkCore/DownloadLogCollection.cs` | The `DownloadLog` LevelDB database |
| `src/server/HSMServer/Model/ManagementApi/LargeFiles/LargeFileDownloadService.cs` | The decisions: order of checks, 409 reasons, the record, the description |
| `src/server/HSMServer/Model/ManagementApi/LargeFiles/LargeFileDownloadResult.cs` | The answer: the file with ranges, the hand-over, the error answers of each surface |
| `src/server/HSMServer/Model/ManagementApi/LargeFiles/LargeFileNames.cs` | The download file name and `Content-Disposition` |
| `src/server/HSMServer/Controllers/LargeFilesController.cs`, `LargeFilesApiController.cs` | The web and API routes |
| `src/server/HSMServer/BackgroundServices/LargeFileArchiveBackgroundService.cs` | The loop: builder runs, signals, the daily cleanup of the download log |
| `src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/LargeFiles/LargeFileSensors.cs` | The two self-monitoring sensors and their default alerts |
| `src/server/HSMServer/ServerConfiguration/Sections/LargeFileArchivesConfig.cs` | The settings section |
| `src/server/HSMServer/Extensions/LargeFileArchivesServiceCollectionExtensions.cs` | The registration of the archive side in one place |
| `caddy/Caddyfile` | The hand-over rule in `hsm_upstream` |

## Storage / Persistence

- Archives and parts: the large file store (#1883), outside every LevelDB database. This feature writes an archive only through `BeginArchive`/`CommitArchive`.
- The download log: `Databases/DownloadLog`, key and value above, retention by age.
- Build statuses and download windows: memory only, rebuilt from the store and from new requests after a restart.

## UI / Operator Visibility

- The web download route; the list of large values on the sensor page is #1886.
- The 409 reasons and `buildState` in the description tell why a file cannot be downloaded yet.
- The two self-monitoring sensors with their alerts; the server log names files, products and versions.
- The dump password section of the product settings page (`../dump-password/feature.md`).

## Dependencies

- Depends on: the large file store (#1883) through `ILargeFileArchiveStore` (`LargeFileStoreArchiveAdapter`); the dump password; the API token authorization (`api-tokens/`); the web UI access checks (`User.IsProductAvailable`); the forwarded-headers setup (`Kestrel.TrustedProxies`); the bundled Caddy image.
- Used by: the sensor page (#1886), the management API and MCP surfaces, AI agents.

## Tests

`src/tests/HSMServer.Core.Tests/LargeFiles/`:

- `Archives/ArchiveBuilderTests.cs` — first pack opens with the password and removes the part; a file that is not secure is packed at once without a password (also for a product without one), opens only without a password, is never rewritten by a password change, and is ignored by the version retirement and the "version not kept" report; 2,000 tiny files that are not secure are packed in one pass from one listing (the fake store counts `GetFiles`) and leave no status behind; a file verified during a pass is built in the same pass; the status of a file the store no longer knows is dropped; waiting for a password, then building; a file verified before a restart is found without its event; no room is a wait, not a failure, also when the volume fills while the temporary archive is written (the three out-of-space codes), while another I/O fault there is a failure; a part of another length is never built; the backoff list then hourly; a damaged part and a writer fault keep the part and abandon the temporary file; a file removed during the build is dropped without a retry; begin not found / wrong state; commit superseded during a first pack and during a re-encryption; re-encryption opens only with the new password and removes the old archive; a lost old version fails clearly and keeps the old archive; a packed archive whose version is not kept, or is newer than the current one, fails once and is never retried by itself; a password change during a build commits under the version taken, then re-encrypts; a version is retired only after its last archive is rewritten, and once; concurrent passes never run two builds; cancellation abandons without a failure; no password in statuses, events or logs.
- `Archives/LargeFileStoreArchiveAdapterTests.cs` — over the real store in a temporary folder: a verified file (secure and not) is described from the record; an upload is listed as uploading; the verified event is forwarded and stops after Dispose; the part decodes to the content and its length on the disk is the record's; a commit with version 0 has no password version and its archive is served from its path; the hand-over's relative path is the store's archive path under `ArchivesFolder`; a re-encryption expects the current archive and a second commit is superseded; a commit after a removal is removed; an abandon deletes the temporary file; a begin of an unknown file is not found, of an upload wrong state.
- `Downloads/LargeFileEndToEndTests.cs` — nothing faked between the store and the client: a file verified through the store's upload path (zstd pieces), packed by the real builder and writer, downloaded through the real service and API controller in a TestServer: a secure archive opens with its password only and the download log holds its record; one that is not secure is packed without a product password and opens without one (recorded with version 0); a range and a resume give the whole archive (one record); a re-encryption makes a new archive, the old link answers 404 and the new one opens with the new password only; the hand-over names the file the store keeps; 7-Zip tests and extracts the downloaded archive where installed.
- `Downloads/LargeFileValueBlockTests.cs` — the `largeFile` block of `/api/v1` sensor reads: a packed value carries its archive and a working link and no password; a not-secure value has no password version; a verified one its reason; one the store no longer holds, or a file of another sensor, is `removed`; a store that cannot be read is `unavailable`; an ordinary file value has none.
- `LargeFileHistoryReadTests.cs` — an access key reads the history of a File sensor through the Sensor API: `history` describes a large value (secure and not) without its content and keeps an ordinary value's content and shape; the state comes from the store (packed, removed); `historyFile` leaves the content cell empty and appends the `Large file` column only when the values hold a large one.
- `Archives/SharpZipArchiveWriterTests.cs` — round trip of bytes, name, size and time at several levels and on seekable and forward-only outputs; without a password: an unencrypted round trip that a password does not open, an encrypted entry refused without a password, the Zip64 field and no AES field on a tiny file, an empty password refused; an empty entry; a wrong password is a format error without either password in its text; Zip64 and AES extra fields with AES-256 on a tiny file; damaged data; an archive of two entries refused; not a zip; cancellation; a Cyrillic entry name round-trips and carries the UTF-8 flag.
- `Archives/ArchiveEntryNamesTests.cs` — the name rules, the length cut keeping the extension, surrogate pairs.
- `Archives/SevenZipInteropTests.cs` — an archive of the builder passes `7z t` with the password, lists `AES-256` and the entry name, fails with a wrong one; an archive that is not secure passes `7z t` without a password, lists Zip64 and no AES, and `7z x -so` gives the sent bytes; a Cyrillic entry name is listed by `7z l -slt` exactly as sent; runs where 7-Zip is installed (`7z` on `PATH`, or `Program Files\7-Zip` on Windows) and is skipped otherwise.
- `Downloads/DownloadLogTests.cs` — the sliding window, separate callers and archives, a token apart from its owner, the record's fields, a failed write throws and the next request writes again, bounded windows, concurrent requests make one record, reads by product and range newest first, reads of one file newest first (in memory and over LevelDB), removal of older records, options, the database opening on first use, a round trip through LevelDB across a reopen.
- `Downloads/LargeFileWebDownloadTests.cs` — every hidden case answers the same 404; 409 with the reason; not signed in redirects to the sign-in page; a bearer token does not open the web route.
- `Downloads/LargeFileApiDownloadTests.cs` — the same 404 for an owner who cannot see and an unknown id; an archive that is not secure downloads with or without a product password, without `X-Hsm-Password-Version`, recorded with version 0, and its hand-over carries no version; a file that is not secure and not packed yet answers 409 "being packed", never "no password"; both kinds hide behind the same 404; a secure record without a version is not served; 401 without a token and with an access key; 409; GET recorded, HEAD not; 503 when the log fails, without the file; 206 and resumption with the ETag; a new password in the middle of a download makes the next range of the old archive 404; an archive replaced after the decision is not served; an archive the store cannot open (access denied or another fault) answers the same 404 as an unknown id; the marker from a trusted proxy hands the file to Caddy (also over an IPv4-mapped IPv6 peer); the marker from any other peer, from no recorded peer, or with a client's own `X-Original-For` gets the file itself; a store that places the archive elsewhere than the two-GUID path, or cannot say where, is neither handed over nor served and answers the same 404; no error answer carries the instruction.
- `Downloads/LargeFileDescriptionTests.cs` — a packed and a verified file described without a password; `secure` in the description, null `passwordVersion` and `passwordSetUtc` for a file that is not secure; `DescribeFile` without a caller, null for unknown, uploading, orphaned and moved files; the same 404 for unknown, invisible and other-sensor files; the MCP twin answers the same and is read-only and idempotent.
- `Downloads/LargeFileArchiveBackgroundTests.cs` — signals coalesce and runs never overlap; a failing run is caught and the cleanup runs once a day; build outcomes reach the sensors; the default alerts; the settings defaults; `Content-Disposition` with the ASCII fallback and the full name; the download name is the entry name plus `.zip`, format characters dropped.
- `caddy/test.sh` — the hand-over through the real Caddy image (`aicontext/architecture/docker.md`).

## Known Issues / Limitations

- The archive opens in 7-Zip (verified, 8 GiB) and, by the format, in WinRAR; WinRAR was not available on the development machine and is not verified. The archiver built into Windows usually cannot open an AES zip.
- The password does not hide the entry name, the sizes or the time of the file inside the archive.
- The format derives its key with 1,000 rounds of PBKDF2: an archive that has left the server is as strong as its password and can be attacked offline for as long as it exists. A password shared with many people cannot be taken back from one of them.
- The download log records that a download was allowed; nothing records that all the bytes arrived.
- The costs above are 8 GiB measurements and a linear extrapolation; a real dump of production size is measured in #1889.
