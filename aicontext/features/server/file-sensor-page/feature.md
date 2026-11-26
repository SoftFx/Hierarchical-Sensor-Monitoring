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
- A large value never reaches the text preview or the content download of an ordinary file: both answer 404 for it.
- The download log of a file is shown to a Manager of the product and an admin only.

## Primary Workflows

1. The page loads the tab: `GetFileInfo` describes the last value. An ordinary csv/txt small enough is previewed; a large last value shows no preview and no buttons above the list.
2. `GetRecentFilesView` returns the first page: the uploads of the sensor in progress, then up to 20 values (an ordinary last value is left out: it is shown above). "Older files" asks `GetFilesPage?olderThan=<ticks>` and replaces itself with the next page.
3. A row of a large value shows its state; its download button links the archive (`/LargeFiles/Download/{fileId}/{archiveId}`, step 3), which checks access again and records the download.

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
| a large value whose file the store has no record of | file is missing (no button) |

There is no "removed by retention" state: retention removes a value's row by its key range and the store's pass then removes the file, so a large value leaves the list with its history, as any value does. The password notice appears only when a secure large file is listed. The texts of why a file cannot be downloaded are step 3's (`LargeFileDto.notReadyReason`). Where the store has no application or host, the row shows `—`.

## API / Public Contracts

| Route | Answer |
|---|---|
| `GET /Home/GetFileInfo?Selected=<id>` | `FileInfoDto`, or 404 |
| `GET /Home/GetFile?Selected=<id>&dateTime=<ticks>` | the content of an ordinary file value (the last one for 0); 404 for a large value, an unknown time, an unknown or invisible sensor |
| `POST /Home/GetFileStream?Selected=<id>&dateTime=<ticks>` | the same as a stream, for the preview |
| `GET /Home/GetRecentFilesView?fileId=<id>` | the first page (partial `_FileAccordions`) |
| `GET /Home/GetFilesPage?fileId=<id>&olderThan=<ticks>` | the page of values older than the time |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Model/FileSensors/FileSensorPageService.cs` | the description, the point read, the pages, the state of a large row (`Decide`) |
| `src/server/HSMServer/Model/FileSensors/FileSensorPageModels.cs` | `FileInfoDto`, the row, upload and page view models |
| `src/server/HSMServer/Model/FileSensors/LargeFilePageSource.cs` | `ILargeFilePageSource`: what the page reads from the store, the builder, the passwords and the download log |
| `src/server/HSMServer/Controllers/HomeController.cs` region "File" | the thin actions and their access check |
| `src/server/HSMServer/Views/Home/_FileAccordions.cshtml` | one page: the password notice, the uploads, the rows |
| `src/server/HSMServer/Views/Home/Sensor/History/_SensorFileContent.cshtml` | the last value and the handlers of the list |
| `src/server/HSMServer.Core/Extensions/FileValueNameExtensions.cs` | the name rule (`GetDisplayName`) |

## Dependencies

- Large file store (#1883): uploads of a sensor, the record of a file.
- Archive builder, dump passwords, download and download log (#1885).
- Alerts: the same name rule prints `$fileName` (`alerts/feature.md`).
- Self-monitoring of large files: the gauges and the refusal alert are in `storage/large-files/feature.md`, "Self-monitoring".

## Tests

- `src/tests/HSMServer.Core.Tests/Controllers/HomeControllerFileTests.cs`: the description without bytes, the same 404 for an unknown and an invisible sensor in every action, a file found by its time past the first 20, pages past 20 without the last value, the name rule, a client's extension kept as data, the state of a stored file for every builder state with and without a password, the missing file, the upload percent and "being checked", uploads on the first page only with the application, the host or `—`, the password notice and its link for a Viewer, a Manager and an admin.
- Playwright: `src/tests/Autotests` (the page with large values).

## Known Issues / Limitations

- A concurrent read of the same File sensor's history answers an empty page (the per-sensor file-history lock of the cache); reloading the tab reads it again.
