# Feature: Web UI access to the tree by id, and text shown in pages

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: which sensors, nodes and folders a signed-in web user can address by id in the tree page and the history routes, and how text that comes from collectors and users is put into pages.

---

## Overview

Two rules of the cookie-authenticated web UI live here.

1. **Access by id.** The tree page (`HomeController`), the history routes (`SensorHistoryController`) and the per-node journal feed (`JournalController.GetPage`) take sensor, node and folder ids from the client. The sensor tree (`TreeViewModel`) and the folder storage are process-wide singletons that hold every product, so an id found in them says nothing about the caller. These controllers do not hold the tree: they hold `UserTreeAccess`, whose lookups answer "not found" for an entity the user cannot see.
2. **Text in pages.** Sensor paths, node names, file names and extensions, file content, comments and string values come from collectors (whoever holds an access key of the product). Folder, key, chat, dashboard and user names come from users. All of it is text and is shown as text.

## Invariants

### Access by id

- A sensor or node is **visible** to a user when the user is an admin or holds any role (Viewer or Manager) on its **root product**. A folder is visible when the user is an admin, holds a role on the folder, or sees at least one of its products. This is the rule the tree itself is built by (`TreeViewModel.GetUserProducts`, `VisibleTreeViewModel`).
- A folder role does not imply a role on every product in the folder (a per-product role removal narrows it). A folder therefore shows a user only the products they see: in its children list, in its panel statistics, in its journal, and when an action fans out over the folder (the Grafana switch). A folder's own settings are inherited by every product in it, so changing them (`UpdateFolderInfo`) needs the folder Manager role, as the bulk edit already does.
- An id the user cannot see is answered **exactly like an unknown id**, and nothing changes: no data, no name in an error text, nothing connected to the user's state, nothing opened in the user's tree, no write.
- `UserTreeAccess` answers visibility only. Whether a user may **change** what they see (Manager role) is checked by the action or the service it calls (`RemoveNode`, `EditAlerts`, the sensor move, the policy lists through `CookieUserAccess`).
- **Per-user state is checked again by its readers.** `SelectNode` connects the selected entity to state kept on the `User` object: the children pages (`SelectedNode`), the history table (`History.Table`), the journal (`Journal`). That state outlives the request and a role change, so every reader checks it again, per request: `GetGrid`/`GetList` (a folder's product list is taken anew), `TableHistory` and the table pages, `ExportHistory`, the recent files of a file sensor, `JournalController.GetPage` (of the loaded records only those of sensors and nodes visible now are paged and counted; a folder's own records, its settings, are the folder Manager's to read, as on the global Journal page).
- The history table in the user's state belongs to one sensor: the last one selected, in any browser tab. A table, export or recent-files request names its sensor; after the lookup the state is connected to that sensor, so the rows served are the rows of the sensor asked for. The "new values" counter is reported only for the sensor it counts.
- Database compaction and export (`Compact`, `Export`) and unsealing act on the whole database and are admin-only.
- A new action in these controllers resolves ids through `UserTreeAccess` and nothing else. Do not inject `TreeViewModel` into a controller that takes ids from the client.

### Text in pages

- Razor: `@value` (HTML-encoded) for every name, path and description. `Html.Raw` is only for HTML the server built itself from constants or from already encoded parts (icons, the alert action summary), for serialized JSON in a script block, and for the output of `MarkdownHelper.ToHtml`, which sanitizes.
- Comments and string values are sanitized at ingestion (`HtmlSanitizerService.SanitizeText`: only `<markdown>` and `<br>` survive) and again at render (`MarkdownHelper.ToHtml`). File names, extensions and file content are **not** sanitized anywhere: they are plain text and must be output as text.
- A name is never written into an inline event handler or a script string literal. Razor's HTML encoding does not protect there: the browser decodes the attribute before running it, and a template literal still evaluates `${...}`. Pass it as a `data-` attribute read with `dataset`, or as `@Json.Serialize(value)`.
- Script: text that joins an HTML string goes through `escapeHtml` (site.js). `showToast` and `showConfirmationModal` take ready HTML and pass it through `sanitizeHtml` (DOMPurify) as a second line; callers still escape the names and paths they put in. Values set into an element use `.text()`.
- HTML built in C# for a table that inserts cells as markup (the per-node journal table, DataTables) encodes each piece of recorded text where the cell is built (`JournalRecordViewModel`, `JournalController.GetPage`).
- File sensors: a preview requests the file as text (`dataType: "text"`), so the response is never taken for a script by its content type; the text preview sets text; the table (CSV) preview renders cells with DataTables' text renderer; "Preview" in a new tab shows any non-PDF file inside a sandboxed frame (`sandbox` without `allow-same-origin`), so a page from a file runs its own scripts in an origin of its own and cannot reach the site's cookies, storage or pages.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Select a sensor, node or folder in the tree; page through its children, history, files and journal | operator |
| 2 | Open a kept link or bookmark to a sensor after a role was taken away: the page shows "No Data" | operator |
| 3 | A collector registers a sensor whose path, comment or file holds markup: every page shows it as typed | collector integrator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `UserTreeAccess` | `src/server/HSMServer/Model/TreeViewModels/UserTreeAccess.cs` | `TryGetSensor`/`TryGetNode`/`TryGetFolder(user, id, out ...)`, `IsVisible`, `GetVisibleProducts`, `GetSensorIds`; singleton in DI |
| `escapeHtml`, `sanitizeHtml` | `src/server/HSMServer/wwwroot/src/js/site.js` | global helpers of the main bundle |

No route, DTO or storage format changes.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Model/TreeViewModels/UserTreeAccess.cs` | The visibility rule and the id lookups |
| `src/server/HSMServer/Controllers/HomeController.cs` | Tree page actions; hold `UserTreeAccess`, not the tree |
| `src/server/HSMServer/Controllers/SensorHistoryController.cs` | History routes; `SelectedTable` hands the table out only while its sensor is visible |
| `src/server/HSMServer/Controllers/JournalController.cs` | Per-node journal feed: re-checks the connected entity, encodes plain cells |
| `src/server/HSMServer/Model/TreeViewModels/SelectedNodeViewModel.cs` | Children pages of the selected node or folder (folder: visible products only) |
| `src/server/HSMServer/Model/History/Personal/Journal/SelectedJournalViewModel.cs` | Journal of the selection (folder: visible products only); `ConnectedId` |
| `src/server/HSMServer/Model/History/Personal/Journal/JournalRecordViewModel.cs` | Builds the Record cell; encodes recorded text |
| `src/server/HSMServer/wwwroot/src/js/site.js`, `confirmation.js`, `file.js`, `tree.js` | `escapeHtml`/`sanitizeHtml`, the dialog and toast helpers, file previews, tree dialogs |

## Storage / Persistence

None.

## UI / Operator Visibility

A refused id looks like a missing one: the node panel shows "No Data", tables and charts are empty, a download answers 404.

## Dependencies

- Depends on: product and folder roles (`User.IsProductAvailable`, `User.IsFolderAvailable`), `MarkdownHelper`, `HtmlSanitizerService`, DOMPurify.
- Used by: the tree page, sensor history, the per-node journal tab.

## Tests

- `HomeControllerAccessTests`, `SensorHistoryControllerAccessTests` (`src/tests/HSMServer.Core.Tests/Controllers`): every id-addressed action asked by a member of the product, a user of another product and an admin; the per-user state after a role is taken away; a folder with products the user does not see, and one where a role is taken from one of its products; folder settings by role; requests that name another sensor than the selected one. Shared tree: `WebAccessFixture`.
- `EncodedOutputViewTests` (`.../Views`): the fixed views rendered through the Razor engine (`ViewRenderer`) with names made of markup; the tree metadata with a quoted name.
- `JournalRecordEncodingTests` (`.../Journal`): the Record cell.
- `home/collector_text_is_shown_as_text.spec.ts` (`src/tests/Autotests`): a sensor registered through the Sensor API with markup in its path, comment and files, walked through the tree, the sensor page, the removal dialog and the file previews.

## Known Issues / Limitations

- Visibility is not the Manager role: `UserTreeAccess` never answers whether a user may change an entity. A write action carries its own role check.
- A folder's journal is loaded when the folder is selected, for the products visible then. Records of a product that stops being visible are left out from the next page on; a product that becomes visible later shows its records after the folder is selected again.
- Comments are stored sanitized (HTML with the text already encoded). Where one is written through a text-only path (the recent files list, a journal record of a manual status change), `<` and `&` typed in the comment show as entities.
- Dashboards, the alert template pages and the product, folder and access key administration pages have their own controllers and are outside this feature.
