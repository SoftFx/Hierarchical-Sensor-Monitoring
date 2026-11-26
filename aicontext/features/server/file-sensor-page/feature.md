# Feature: File sensor page

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: what the web UI shows of a File sensor's values, ordinary and large: the list page by page, the description of the last value, the states of a large file, its download and its download log, and the file actions behind them.

---

## Overview

The "Files" tab of a File sensor shows its last value on its own and, under it, the list of its values, newest first, page by page with no limit on how far back. An ordinary file value keeps its preview (csv up to 5 MiB, txt up to 1 MiB) and its download. A large file value (ADR-0022, epic #1882) is listed with its state, the time of the source file and the time it was accepted, the application and the host it came from, and its download once its archive is ready; it never has a preview. Uploads that are not values yet are listed first, with their percentage (a page of them; the rest are counted). A product without a dump password says what is missing and who can set it.

A large file is of one of two kinds, marked by the application at the send (the owner's amendment of 2026-11-25): **secure** (a dump: its archive under the product's dump password) or **not secure** (the same zip without a password, downloadable by everyone who sees the product whether or not the product has a password). The page shows the kind of every large value; only a secure file waits for the password. A File sensor may hold ordinary and large values side by side: the content of an ordinary `SendFile` above about 1 MiB travels on the file connection and is stored as a not secure large file, so a sensor may hold hundreds of small large values; the list is paged and reads the store once per row of the page.

Issue #1886 (step 4 of epic #1882). The store is step 1 (#1883), the archive, the password and the download step 3 (#1885).

## Invariants

- The page and every file action work from the description of a value, never its bytes: `GetFileInfo` answers `FileInfoDto` (time, receiving time, name, extension, size, `isLarge`), for ordinary and large values alike.
- A file is found by the time of its row (`dateTime` = ticks of the value's time, a point read), not among the first 20 rows of the history: every ordinary file value of the list can be downloaded.
- The list reads the sensor's history directly (`FileSensorPageService`), not through the user's History tab state, which belongs to whichever sensor the user selected last.
- Every file action (`GetFile`, `GetFileStream`, `GetFileInfo`, `GetRecentFilesView`, `GetFilesPage`) answers an unknown sensor and a sensor of a product the user may not see the same way (404); it relies on the web UI access-check work.
- Nothing a client sent (a file name, an extension, a comment, an application, a host) goes into a script or an event attribute: rows carry it as HTML-encoded text and `data-*` attributes, element ids are made of the value's time only, and the handlers in `_SensorFileContent.cshtml` read the attributes.
- A large value never reaches the text preview or the content download of an ordinary file, whatever its extension and size (a routed `SendFile` of a small txt included): the row offers no Preview and only the archive's download, and `GetFile` / `GetFileStream` answer 404 for it.
- The download log of a file is shown to a Manager of the product and an admin only, and read only when they ask for it in the row (`GetLargeFileDownloads`), never with a page: the log is not read on every view.

## Primary Workflows

1. The page loads the tab: `GetFileInfo` describes the last value. An ordinary csv/txt small enough is previewed; a large last value shows no preview and no buttons above the list.
2. `GetRecentFilesView` returns the first page: the uploads of the sensor in progress, then up to 20 values (an ordinary last value is left out: it is shown above). "Older files" asks `GetFilesPage?olderThan=<ticks>` and replaces itself with the next page.
3. While the list shows an upload or a large file not yet ready, the tab reads the first page again every 10 seconds (the percentage, "being checked", "being packed" turning "ready"), unless the user went to older pages or opened a row.
4. A row of a large value shows its state; its download button links the archive (`/LargeFiles/Download/{fileId}/{archiveId}`, step 3), which checks access again and records the download.

## States of a large file on the page

| Where it comes from | State on the page |
|---|---|
| store record `Uploading`, pieces missing | uploading, N% |
| store record `Uploading`, every piece in or the end check running | being checked |
| `verified`, secure, the product has no dump password (asked before the builder's state) | waiting for the product's password |
| `verified`, builder `none`, `queued`, `building` (none: after a restart, before the builder lists the file) | being packed |
| `verified`, builder `waitingForRoom` | being packed (waiting for disk space) |
| `verified`, builder `failed` | packing failed |
| `verified`, secure, builder `waitingForPassword` | waiting for the product's password (a not secure file is "being packed") |
| `packed` | ready (with "being re-encrypted under the new password" while a re-encryption runs; the old archive stays downloadable) |
| a large value whose file the store has no record of, or whose record belongs to another sensor | file is missing (no button) |
| the store cannot be read now | state cannot be read now (no button, no kind); the uploads are not listed; ordinary values are shown as usual |

There is no "removed by retention" state: retention removes a value's row by its key range and the store's pass then removes the file, so a large value leaves the list with its history, as any value does. The password notice appears only when a secure large file is listed. The texts of why a file cannot be downloaded are step 3's (`LargeFileDto.notReadyReason`). Where the store has no application or host, the row shows `—`.

## History export

The CSV export of the History tab (`SensorHistoryController.ExportHistory`) and of the Sensor API history write a large value as a row with an empty content column, its name (without the extension) and its extension in their columns, its status and comment, and a last column `Large file` that holds `Large file <file id>; <size> bytes; secure|not secure; <state>` (the kind and the state read from the store; without them when the store cannot be read). The column is there only when the exported values hold a large one, so an ordinary File sensor's export keeps its columns.

## API / Public Contracts

| Route | Answer |
|---|---|
| `GET /Home/GetFileInfo?Selected=<id>` | `FileInfoDto`, or 404 |
| `GET /Home/GetFile?Selected=<id>&dateTime=<ticks>` | the content of an ordinary file value (the last one for 0); 404 for a large value, an unknown time, an unknown or invisible sensor |
| `POST /Home/GetFileStream?Selected=<id>&dateTime=<ticks>` | the same as a stream, for the preview |
| `GET /Home/GetRecentFilesView?fileId=<id>` | the first page (partial `_FileAccordions`) |
| `GET /Home/GetFilesPage?fileId=<id>&olderThan=<ticks>` | the page of values older than the time |
| `GET /Home/GetLargeFileDownloads?fileId=<sensor id>&file=<file id>` | who downloaded the file and when, the 20 most recent (partial `_LargeFileDownloads`); 404 for a Viewer, a file of another sensor, an unknown file or sensor |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Model/FileSensors/FileSensorPageService.cs` | the description, the point read, the pages, the state of a large row (`Decide`) |
| `src/server/HSMServer/Model/FileSensors/FileSensorPageModels.cs` | `FileInfoDto`, the row, upload and page view models |
| `src/server/HSMServer/Model/FileSensors/ILargeFilePageSource.cs` | `ILargeFilePageSource`: what the page reads about large files, and its facts |
| `src/server/HSMServer/Model/FileSensors/LargeFilePageSource.cs` | its implementation: the store's uploads and origin (#1883), `LargeFileDownloadService.DescribeFile`, `IDumpPasswordService.HasPassword`, `IDownloadLog.ReadFile` (#1885) |
| `src/server/HSMServer/Controllers/HomeController.cs` region "File" | the thin actions and their access check |
| `src/server/HSMServer/Views/Home/_FileAccordions.cshtml` | one page: the password notice, the uploads, the rows |
| `src/server/HSMServer/Views/Home/_LargeFileDownloads.cshtml` | who downloaded one file |
| `src/server/HSMServer/Views/Home/Sensor/History/_SensorFileContent.cshtml` | the last value and the handlers of the list |
| `src/server/HSMServer.Core/Extensions/FileValueNameExtensions.cs` | the name rule (`GetDisplayName`) |

## Dependencies

- Large file store (#1883): uploads of a sensor, the record of a file.
- Archive builder, dump passwords, download and download log (#1885).
- Alerts: the same name rule prints `$fileName` (`alerts/feature.md`).
- Self-monitoring of large files: the gauges and the refusal alert are in `storage/large-files/feature.md`, "Self-monitoring".

## Tests

- `src/tests/HSMServer.Core.Tests/Controllers/HomeControllerFileTests.cs`: the description without bytes, the same 404 for an unknown and an invisible sensor in every action, a file found by its time past the first 20, pages past 20 without the last value, the name rule, a client's extension kept as data, the state of a stored file for every builder state with and without a password, the missing file, the upload percent and "being checked", uploads on the first page only with the application, the host or `—`, the password notice and its link for a Viewer, a Manager and an admin.
- `src/tests/HSMServer.Core.Tests/Controllers/FileAccordionsRenderTests.cs`: the partials rendered by the server's compiled views on a test host: every state of a large file and both kinds, the download only when ready, no preview for a large value, the live mark, an upload's percent and origin, the password notice with and without the settings link, a client's name, extension, application and host only as encoded text and data attributes (no `onclick`, ids of digits), the downloads of a file encoded.
- `src/tests/HSMServer.Core.Tests/LargeFiles/LargeFilePageSourceTests.cs`: the real store's uploads with their progress, kind and origin; over the download step: a packed file ready with its web link, a secure verified file waiting in the download step's words, a not secure one never waiting, a file of a removed sensor missing, the downloads naming the user or the token.
- `src/tests/HSMServer.Core.Tests/LargeFiles/FileSensorPageDatabaseTests.cs`: on the real database, a value at the first tick of a storage week found by its time and listed.
- Playwright: `src/tests/Autotests/home/file_sensor_page.spec.ts` covers ordinary file values (pages past 20, the 25th downloaded by its time, a hostile extension). Large values are not seeded there yet: a real upload needs the file connection (#1884), which is not in the integration branch at this step; the state matrix is held by the render tests above.

## Known Issues / Limitations

- The history of one File sensor is read by one reader at a time (the per-sensor file-history lock of the cache): a page, a download or a preview asked while another read of the same sensor runs answers an empty page or 404; asking again reads it. The live refresh keeps the list it has when its read met another one.
