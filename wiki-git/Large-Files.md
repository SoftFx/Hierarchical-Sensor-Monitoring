# Large Files

> Large file values need HSMServer 3.46.0 or later; earlier servers have none.

This page is for a user of the HSM site: what a large file value is, what the sensor page shows of it, how to download it and open it, the dump password of a product, the alert, how long files are kept, and the REST API and MCP. How an application sends large files is on [DataCollector Large Files](DataCollector-Large-Files); the server's settings are on [Server Configuration](Server-Configuration#large-files). All kinds of sensors are compared on [Sensor Types](Sensor-types).

---

## What a large file value is

A File sensor holds files. An **ordinary file value** carries its bytes in the value itself, so it is small: on the collector session (the default transport) a file of about 1 MiB at most travels in one message, and the site shows a preview of a `csv` or `txt` file. A **large file value** is a value of the same File sensor whose content is kept on the server's disk beside the database: the application's collector uploads it in compressed pieces on a connection of its own, and it can be as large as the server allows (64 GiB by default). Crash dumps are the reason it exists.

What you notice on the site:

| | Ordinary file value | Large file value |
|---|---|---|
| On the sensor page | a preview (`csv` under 5 MiB, `txt` under 1 MiB) and **Download** | no preview; a state, and **Download** once its archive is ready |
| What you download | the file itself | a **zip** with the file inside, encrypted for a secure file |
| Its time | the time the application gave the value | the time the server accepted the file; the time of the source file is shown beside it |
| Where it comes from | — | the application and the host that sent it |

A value becomes large in two ways: the application sends it with the large file API (a watched folder of dumps, or one file handed over), or it sends an ordinary file larger than about 1 MiB on the collector session, which then takes the same path by itself (managed collector 4.2.0, native collector 0.16.0). Over HTTP an ordinary file stays ordinary, up to the collector's and the server's limits. One File sensor may hold ordinary and large values side by side.

**Secure and ordinary files.** The application decides, per file, when it sends it; the server never changes it:

- a **secure** file (the default of the large file API, meant for dumps) is given out only as a zip encrypted with AES-256 under the **dump password** of its product;
- an **ordinary** (not secure) file, and every file that came through an ordinary send, is given out as the same zip **without a password**.

The page shows the kind of a large value, `secure` or `not secure`, whenever it can read the file's state.

---

## The sensor page

Select the File sensor in the tree and open the **Files** tab. Under the last value, the list shows the values of the sensor, newest first, 20 at a time; **Older files** shows the next 20, with no limit on how far back.

**The last value.** An ordinary last value is shown on its own above the list with its preview, as before. A large last value says "A large file: its state and its download are in the list below."

**Uploads in progress** come first, before they are values. Each shows the file's name, its kind, its state, its size, when the upload started, a progress bar, and "File time: … · Application: … · Host: …". When a sensor has more than a page of them, the rest are counted ("and N more uploads in progress").

**A large value** shows its name, its kind, its state, its size and the time it was accepted. Opened, it shows:

- **File time**: the modification time of the source file;
- **Accepted**: the time the server accepted it (the value's time);
- **Application** and **Host**: the collector's application name and the address the server recorded for it (`—` when not known);
- why it cannot be downloaded yet, when it cannot;
- **Download**, once its archive is ready;
- **Who downloaded it**, for a Manager of the product and an admin (below).

While the list shows an upload in `uploading` or `being checked`, or a file `being packed`, the page reads the list again every 10 seconds, while the browser tab is visible. It pauses while a row is open and stops after **Older files**. Other states change rarely and are not read again: reload the tab to see them change.

### The states

| State on the page | Means | Download |
|---|---|---|
| `uploading, N%` | the collector is sending the pieces | — |
| `being checked` | every piece is in; the server checks the whole file against what the collector sent | — |
| `waiting for the collector, N%` | no piece came for longer than the server's lease (3 minutes by default): the collector stopped, lost its connection, or its computer is off. It shows the time of the last piece and the date the server removes the part "unless the collector continues". The collector continues from where it stopped when it comes back. | — |
| `waiting for the collector to finish` | every piece is in, but no check runs: the collector stopped before asking for it, or a check failed and the collector sends again | — |
| `waiting for the product's password` | a secure file is stored and checked; its product has no dump password yet | no |
| `being packed` | the server builds the zip | no |
| `being packed (waiting for disk space)` | the server has no room for the zip now; it tries again by itself | no |
| `packing failed` | building the zip failed; the server tries again by itself and keeps the file | no |
| `ready` | the zip is ready | **yes** |
| `ready (being re-encrypted under the new password)` | the product's password changed; the old zip stays downloadable until the new one is ready | **yes** |
| `file is missing` | the value is there, its file is not | no |
| `state cannot be read now` | the server cannot read its large files at the moment; ordinary values are shown as usual | no |

The first four are uploads (not values yet), the rest are values. A file becomes a value only once the server has checked it: a half-uploaded file, or one whose check failed, is never a value.

**A product without a dump password.** When the list holds a secure file of such a product, a notice above it says: "Secure large files of this product are kept and cannot be downloaded: the product has no dump password yet. A Manager of the product or an admin sets it in the product settings." A Manager of the product and an admin also get the link **Set the dump password**.

### Download

**Download** in the row of a ready file gives the zip, named after the file with `.zip` added (`app-crash.dmp.zip`). The download:

- is allowed to **everyone who may see the product**, a Viewer included, from the site (signed in) or over the [REST API](#rest-api-and-mcp) with an API token. An access key of a collector never downloads.
- is checked by the server every time, and recorded in the [download log](#the-download-log) before it starts;
- supports byte ranges: a download that broke off can be continued by a download manager or `curl -C - -o <file>`;
- names the version of the dump password a secure zip opens with (the header `X-Hsm-Password-Version`), and gives the SHA-256 of the zip (the header `Repr-Digest`).

### Opening the zip

The zip holds one file, under the name the application sent (made safe as a file name). A **secure** zip asks for the product's dump password; it uses AES-256 encryption, which **7-Zip** and **WinRAR** open. The archiver built into Windows usually cannot open it. An **ordinary** zip opens without a password, in any archiver.

What 7-Zip shows for a secure zip (`7z l -slt`): `Encrypted = +`, `Method = AES-256 Deflate`, `Characteristics = Zip64 WzAES : Encrypt Descriptor UTF8`. A wrong password gives "Wrong password".

The password protects the content. The zip's list of files (the name, the sizes, the time) is not encrypted: keep secrets out of file names.

### The history as a file

The Sensor API's history file of a File sensor (`POST /api/sensors/historyFile`, with an access key that may read) writes a large value as a row with an empty content, its name and extension, its status and comment, and a last column `Large file`: `Large file <file id>; <size> bytes; secure|not secure; <state>` (the state: `verified`, `packed`, `removed` or `unavailable`). The column is there only when the values hold a large one.

---

## The dump password of a product

The dump password belongs to a product (a root product; its nested nodes use it). It applies to **secure** files only.

**Who.** A **Manager of the product** and an **admin** set it and see it, in the product's settings page (which only they open), section **Dump password**; the section says "Set" or "Not set". Everyone else learns that a password is missing from the notice on the **Files** tab. No API, MCP tool, journal record or log line carries the password itself.

**Setting it.** Click **Generate** (24 letters and digits, made by the server), copy it, click **Save**. Or type one: at least 16 and at most 256 printable ASCII characters. A password can be replaced, not removed. The page answers, for example, "Saved: dump password version 1. The kept secure archives of the product are re-encrypted with it in the background."

**Hand it out** to the people who open the dumps, the way you hand out other secrets. HSM does not send it anywhere.

**While a product has no password**, its secure files are stored and checked, and wait in `waiting for the product's password`. Once a password is saved, packing starts at once; files are packed one at a time for the whole server, so a large dump takes as long as compressing it (our 300 MB took about half a minute).

**Changing it** makes a new **version**:

- The secure zips the server keeps are re-encrypted with the new password in the background, one at a time. Meanwhile a file shows `ready (being re-encrypted under the new password)` and stays downloadable under the old password.
- A re-encrypted file gets a new zip, so a download link to the old zip stops working (it answers "not found"): take the link from the page again.
- **A copy downloaded earlier is not changed**: it still opens with the password it was made with. The download names that version (`X-Hsm-Password-Version`).
- The settings page lists the kept versions to a Manager and an admin: version, set at, set by, and the state (`current`; `replaced, archives are being re-encrypted`; `replaced, kept until <date>`). **Show** shows their values. A replaced version is kept for 30 days (by default, `LargeFileArchives.PasswordRetentionDays`) after the last zip under it was re-encrypted, so a copy downloaded under it can still be opened.
- Ordinary (not secure) files are not touched by a password change.

---

## The alert

A File sensor that an application registered **for large files** gets, when the server creates it, one alert:

> If **New data has been received** then send notification `[$product]$path new file $fileName ($fileSize) $link $fileNote` to the parent chat(s).

So every stored file sends one notification, where the product's other alerts go (as for any alert: when the product has a chat for it, the sensor is not muted, and its schedule allows it). It fires when the file has been checked and is a value, never when an upload begins and never for a file whose check failed. It is an ordinary alert: you may edit it, mute the sensor, or remove it; the server does not add it again. A File sensor created another way (by an ordinary value, or by an older collector) does not get it; add one yourself.

The variables of File sensor alerts:

| Variable | Prints |
|---|---|
| `$fileName` | the file's name, as the page shows it (cut to 200 characters) |
| `$fileSize` | its size, as the page shows it (`286.10 MB`) |
| `$link` | the address of the sensor page, `https://<server>/Home/<sensor id>` |
| `$fileNote` | `waits for the product's dump password` for a secure file of a product without a password; empty otherwise |

They print the same for an ordinary file value (`$fileNote` is then empty); on other kinds of sensors they print nothing. The address in `$link` is built from the server's `HSM_DOMAIN` (the bundled Docker Compose setup passes it to the server), which has no port: on a server reached on another port, whoever opens the link adds the port. Without `HSM_DOMAIN` (for example with `docker-compose.direct.yml`) the address, its scheme and port included, comes from `Agent.ExternalConnectionUrl`; without either, `$link` prints the path alone.

The sensor's settings show the alert with a preview of its message. What we saw for a dump of 300,000,000 bytes, before and after the product had a password:

```
[LargeFileDocs]crashes/dumps new file app-crash.dmp (286.10 MB) https://localhost/Home/70b399f3-… waits for the product's dump password
[LargeFileDocs]crashes/dumps new file app-crash.dmp (286.10 MB) https://localhost/Home/70b399f3-…
```

---

## How long files are kept

- **A stored file** is a value: it is kept as long as the sensor's history (**Keep sensor history**) and goes with it, by retention or by **Clear history**. Removing the sensor or the product removes its files. There is no other expiry.
- **An unfinished upload** is removed 7 days after its last piece (by default, `LargeFiles.AbandonedUploadDays`); one that never received a piece goes when its lease ends (3 minutes by default). Its collector begins it again from the start if it comes back later.
- **A replaced dump password** is kept 30 days (by default) after the last zip under it was re-encrypted, and as long as a kept zip still uses it.

**A full server disk.** The server keeps free space on its disk (10 GiB by default) and reserves room for every upload it begins (about twice the file's size). When there is no room, it refuses a new upload and the application's collector keeps the file and tries again later; nothing is lost. The server's sensor **Large files → Refused for space** turns Error and sends its alert. A file already stored whose zip does not fit waits in `being packed (waiting for disk space)`.

---

## What a sensor with large files cannot do

**It cannot be moved to another product, or merged with another sensor.** The move answers:

> Sensor '…' (id …) holds large files: it cannot be moved to another product. The files leave with the sensor's history period or with clear-history.

A move or a rename inside the same product is allowed, unless it lands on another sensor's path (a merge). A File sensor without large files moves as before, except while the server cannot read its large files.

---

## Who may do what

| | Viewer of the product | Manager of the product | Admin |
|---|---|---|---|
| See the list, the states, the alert | yes | yes | yes |
| Download a file | yes | yes | yes |
| Learn that a dump password is missing (the notice on the Files tab) | yes | yes | yes |
| Open the product's settings and see whether a dump password is set | — | yes | yes |
| Set, generate and see the dump password and its versions | — | yes | yes |
| See who downloaded a file | — | yes | yes |

---

## The download log

Every download the server allows is recorded before it starts: the time, the user (for an API token, its owner and the token's name), the file, the version of the password, the channel (web or API) and the client's address. Requests of one user or token for one zip within 10 minutes of each other (by default) make one record, so a download in ranges is one record.

**Who reads it.** A Manager of the product and an admin: **Who downloaded it** in the row of a file shows the 20 most recent records, for example:

```
11/26/2026 20:21:22 · default (token large files docs) · API
11/26/2026 20:18:50 · default · web
```

Records are kept 365 days (by default). A record says that a download was allowed, not that every byte arrived. If the server cannot write a record, it refuses the download ("The download cannot be recorded right now, so it is refused. Try again later.") and its sensor **Large files → Download log** turns Error.

---

## REST API and MCP

With an API token (your profile page, section **API tokens**; a read-only token is enough for the REST API):

- `GET /api/v1/sensors/{sensorId}` and `GET /api/v1/sensors/{sensorId}/history`: a file value carries a `largeFile` block (`fileId`, `secure`, `state`, `notReadyReason`, and `archive` with its `downloadUrl` once ready; `state` is `removed` when the server no longer holds the file and `unavailable` when it cannot read it now); an ordinary one has `"largeFile": null`.
- `GET /api/v1/sensors/{sensorId}/large-files/{fileId}`: the description of one file: name, extension, size, `sourceModifiedUtc`, `secure`, `state` (`verified`: kept, not downloadable yet; `packed`: downloadable), `buildState`, `notReadyReason`, and `archive` (`id`, `size`, `sha256`, `passwordVersion`, `passwordSetUtc`, `downloadUrl`). `passwordVersion` and `passwordSetUtc` are null for an ordinary file (`passwordSetUtc` also when that version is no longer kept).
- `GET` (or `HEAD`) the `downloadUrl`, `/api/v1/large-files/{fileId}/archives/{archiveId}`, with the same token: the zip, with ranges and resumption.

```bash
curl -k -H "Authorization: Bearer TOKEN" "https://hsm.example.com/api/v1/sensors/<sensor id>/large-files/<file id>"
curl -k -C - -o app-crash.dmp.zip -H "Authorization: Bearer TOKEN" "<downloadUrl>"
```

Answers: 401 without a token or with an access key; the same 404 for a file that does not exist, for one you may not see, and for a zip replaced by a new password; 503 when the download cannot be recorded ("The download cannot be recorded right now, so it is refused. Try again later."); 409 with the reason while it cannot be downloaded:

| Reason |
|---|
| The file is kept but cannot be downloaded: its product has no dump password yet. A Manager of the product sets it in the product settings. |
| The archive of the file is being packed. Try again later. |
| The archive of the file waits for free disk space on the server; the server tries again by itself. Try again later. |
| Packing the archive of the file failed; the server tries again by itself and the file is kept. Try again later. |

**MCP.** The tool `get_large_file(sensorId, fileId)` answers the same description as the REST route. The file's bytes never go through MCP: an agent downloads `downloadUrl` with the same token and opens a secure zip with the dump password its operator gives it. MCP needs a token that is not read-only.

Through the [MCP](MCP) server of HSM an AI agent thus finds a dump, downloads it and, with the password from its operator, opens it.

---

## The server's own sensors

The product **HSM Server Monitoring** has the node **Large files** once the server has taken its first upload:

| Sensor | Shows |
|---|---|
| `Uploads in progress` | uploads not finished yet, now |
| `Files stored` | stored files of every product and both kinds, now |
| `Bytes stored` | MB on the disk now: the parts of uploads and of stored files, and the zips |
| `Files waiting for a password` | secure files of products without a dump password, now |
| `Refused for space` | uploads refused for want of disk space since the server started; Error, with an alert, when it grows |
| `Archive build result`, `Archive builds failed` | the last zip build and the failed ones since the start; the result has an alert |
| `Download log` | Error, with an alert, while downloads cannot be recorded |
| `Parts removed`, `Files removed`, `Leftovers removed`, `Start disagreements`, `Rows written again`, `Rows that disagree`, `Files of sensors not loaded`, `Write failures` | the server's housekeeping of the files; see [Server Configuration](Server-Configuration#what-the-server-reports) |

---

## Try it

From an empty server to a dump on the sensor page, its alert, the dump password, the download and the opened zip. You need Docker Desktop, the .NET 8 SDK, `curl`, 7-Zip and a clone of the repository (branch `master`, 3.46.0 or later).

**1. Run a server and a sender.** Follow steps 1 to 3 of [DataCollector Large Files: Try it](DataCollector-Large-Files#try-it): a server behind the bundled Caddy at `https://localhost:18443`, a product `LargeFileDocs` and its key, and the C# program that watches the folder `dumps` and sends a 2.7 MB report with `SendFile`. Expect its line `The file value of reports/daily (2718735 bytes) is stored on the server as a large file value.`

**2. The ordinary file that went large.** On the site select `LargeFileDocs/reports/daily`, tab **Files**. Expect the row `report.csv`, `not secure`, `ready`, `Size: 2.59 MB`, and opened: File time, Accepted, `Application: large-files-try-it`, `Host: <your address>`, **Download** and **Who downloaded it**. There is no preview.

**3. A dump and its upload.** Put a file of a few hundred megabytes named `app-crash.dmp` into the program's `dumps` folder (a move makes it appear whole); our run used 300,000,000 bytes of random data. About 10 seconds later (the program's settle time) `LargeFileDocs/crashes/dumps` appears. On its **Files** tab, expect the notice "Secure large files of this product are kept and cannot be downloaded: the product has no dump password yet. …" with **Set the dump password**, and above the list the upload: `app-crash.dmp`, `secure`, `uploading, N%`, `Size: 286.10 MB`, `Started: …`, the progress bar, and `File time: … · Application: large-files-try-it · Host: …`. The page counts up by itself every 10 seconds; at the collector's default 10 MB/s our 300 MB took about 30 seconds. Then `being checked` for a moment, and the upload becomes a value: `waiting for the product's password`, opened with "The file is kept but cannot be downloaded: its product has no dump password yet. A Manager of the product sets it in the product settings." and no **Download**. The program prints `… is stored on the server and deleted here.`, and the file is gone from `dumps`.

**4. The alert.** Open the sensor's settings (the info of `crashes/dumps`). Expect one alert: "If **New data has been received** then send notification `[LargeFileDocs]crashes/dumps new file app-crash.dmp (286.10 MB) https://localhost/Home/<sensor id> waits for the product's dump password` to parent chat(s) (From parent)". The help of the comment lists `$fileName`, `$fileSize`, `$link` and `$fileNote`. To get the notification itself, give the product a Telegram chat ([Telegram Setup](Telegram-Setup)) before step 3: the message is the preview's text. Our run had no chat and checked the preview only.

**5. The password.** Open the product's settings, section **Dump password**: "Not set". Click **Generate**, then **Save**: "Saved: dump password version 1. …". Back on the **Files** tab the row turns `being packed`, then `ready` (half a minute for our 300 MB), with **Download**; the notice is gone, and the alert's preview no longer ends in "waits for the product's dump password".

**6. Download and open.** Click **Download**: `app-crash.dmp.zip`. Open it in 7-Zip with the password from step 5. Expect `app-crash.dmp`, 300,000,000 bytes, `AES-256 Deflate`, and the same SHA-256 as the file you put into the folder (`Get-FileHash`). A wrong password gives "Wrong password". In the row, **Who downloaded it** shows `<time> · default · web`.

**7. A new password.** Generate and save another password, and reload the **Files** tab: the row shows `ready (being re-encrypted under the new password)`, and after another reload `ready`; the settings list version 2 as `current` and version 1 as `replaced, kept until <date>`. The zip you downloaded in step 6 still opens with the first password, not with the second; the old download link answers "not found", and **Download** now gives a zip that opens with the second.

**8. Over the API and MCP.** With an API token: `GET /api/v1/sensors/<sensor id>/large-files/<file id>` answers `"secure": true, "state": "packed"`, `"passwordVersion": 2` and the `downloadUrl`; downloading it with the token adds `<time> · default (token <name>) · API` to **Who downloaded it**. MCP's `get_large_file` with a token that is not read-only answers the same description.

**9. What it cannot do.** A move of `crashes/dumps` to another product (a dry run is enough) answers `notSupported`: "Sensor 'LargeFileDocs/crashes/dumps' (id …) holds large files: it cannot be moved to another product. The files leave with the sensor's history period or with clear-history."

**10. The server's sensors.** Within 5 minutes **HSM Server Monitoring → Large files** shows `Files stored` 2, `Uploads in progress` 0, `Files waiting for a password` 0, `Bytes stored` about 287, and the counters at 0.

Failures: a row that stays `waiting for the collector, N%` means the program stopped before the end (start it again: the upload continues); a `secure` file that stays `waiting for the product's password` needs step 5; **Download** answering "The file was not found." means the link is old (a new password made a new zip): reload the list.
