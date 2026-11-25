# Feature: Web UI access to the tree by id, and text shown in pages

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: which sensors, nodes and folders a signed-in web user can address by id in the tree page and the history routes, which of them the user may change, what a self-registered user holds, and how text that comes from collectors and users is put into pages.

---

## Overview

Three rules of the cookie-authenticated web UI live here.

1. **Access by id.** The tree page (`HomeController`), the history routes (`SensorHistoryController`), the per-node journal feed (`JournalController.GetPage`) and the alerts export and import (`AlertsController`) take sensor, node and folder ids from the client. The sensor tree (`TreeViewModel`) and the folder storage are process-wide singletons that hold every product, so an id found in them says nothing about the caller. These controllers do not hold the tree: they hold `UserTreeAccess`, whose lookups answer "not found" for an entity the user cannot see.
2. **Change by id.** Seeing an entity is not the right to change it. An action that changes what it addresses uses the `Managed` lookups of `UserTreeAccess`, which answer "not found" for an entity the user does not manage.
3. **Text in pages.** Sensor paths, node names, file names and extensions, file content, comments and string values come from collectors (whoever holds an access key of the product). Folder, key, chat, dashboard and user names come from users. All of it is text and is shown as text.

## Invariants

### Access by id

- A sensor or node is **visible** to a user when the user is an admin or holds any role (Viewer or Manager) on its **root product**. A folder is visible when the user is an admin, holds a role on the folder, or sees at least one of its products. This is the rule the tree itself is built by (`TreeViewModel.GetUserProducts`, `VisibleTreeViewModel`).
- A folder role does not imply a role on every product in the folder (a per-product role removal narrows it). A folder therefore shows a user only the products they see: in its children list, in its panel statistics and in its journal. A folder's own settings are inherited by every product in it, so changing them (`UpdateFolderInfo`) needs the folder Manager role, as the bulk edit already does.
- An id the user cannot see is answered **exactly like an unknown id**, and nothing changes: no data, no name in an error text, nothing connected to the user's state, nothing opened in the user's tree, no write.
- **Per-user state is checked again by its readers.** `SelectNode` connects the selected entity to state kept on the `User` object: the children pages (`SelectedNode`), the history table (`History.Table`), the journal (`Journal`). That state outlives the request and a role change, so every reader checks it again, per request: `GetGrid`/`GetList` (a folder's product list is taken anew), `TableHistory` and the table pages, `ExportHistory`, the recent files of a file sensor, `JournalController.GetPage` (of the loaded records only those of sensors and nodes visible now are paged and counted; a folder's own records, its settings, are the folder Manager's to read, as on the global Journal page).
- The history table in the user's state belongs to one sensor: the last one selected, in any browser tab. A table, export or recent-files request names its sensor; after the lookup the state is connected to that sensor, so the rows served are the rows of the sensor asked for. The "new values" counter is reported only for the sensor it counts.
- Database compaction and export (`Compact`, `Export`) and unsealing act on the whole database and are admin-only.
- The key list of one product (`AccessKeysController.AccessKeysForProduct`, the "Access keys" entry of the tree) is served for a product the user sees, by the same rule (`UserTreeAccess.IsVisibleTo`); an unknown id and a product the user has no role on are answered 404 alike.
- A new action in these controllers resolves ids through `UserTreeAccess` and nothing else. Do not inject `TreeViewModel` into a controller that takes ids from the client.

### Change by id

- A sensor or node is **managed** by an admin and by a Manager of its **root product**. Roles are held per root product: a role entry on a nested node's own id counts for nothing. A folder is managed by an admin and by a Manager of the folder.
- The root product of a node is read from the tree, where a node that sits in a folder is a root. A folder therefore holds root products only: the folder page admits nothing else, and `ProductController.MoveProduct` answers 400 for a node below the root.
- The folder Manager role does not imply the Manager role on every product in the folder (a user who already held a role on a product keeps it when the product enters the folder, and a product role can be changed afterwards). An action on a folder therefore reaches the products of the folder that the user manages (`GetManagedProducts`): the Grafana switch, the alerts export.
- An id the user sees but does not manage is answered **exactly like an unknown id** by the `Managed` lookups (`TryGetManagedSensor`, `TryGetManagedNode`, `TryGetManagedFolder`, `GetManagedSensorIds`), and nothing changes.
- The tree page actions that change something use them: mute and unmute and the mute dialog (`MuteSensors`, `SetMutedStateToSensorFromModal`, `RemoveMutedStateToSensor`), the Grafana switch (`EnableGrafana`, `DisableGrafana`), the settings forms (`UpdateSensorInfo`, `UpdateProductInfo`, `UpdateFolderInfo`), the display unit of a sensor (`UpdateSensorDisplayUnit`), and "Edit last value" with its dialog (`GetSensorEditModal`, `UpdateSensorStatus`). The page shows these controls by the same rule (`isManager` in the tree metadata, `hasUserRights` in `_MetaInfo.cshtml`, the status pencil in `_SensorGeneralInfo.cshtml`). `ClearHistoryNode` and `UpdateSensorTableSettings` have no control on the page at present and follow the same rule.
- A folder's own settings (`UpdateFolderInfo`) are the folder Manager's and are inherited by every product of the folder that takes them from its parent, whether the folder Manager manages that product or not.
- Mute and unmute of a selection act on the items the user manages and pass over the others without a word, as for an unknown id; the mute dialog names only the items it will mute.
- Two bulk actions are offered on any selection and answer per item: `RemoveNode` and `EditAlerts` resolve each id by visibility and report "no rights" for an item the user sees but does not manage. An item the user cannot see is not mentioned.
- Alerts export and import (`AlertsController`): `ExportAlerts` needs a managed node, `ExportFolderAlerts` a managed folder; both answer an empty result otherwise. `ImportAlerts` looks its sensors up only in a node the user manages (the paths of a file are relative to that node; a root product's sensors are found by their bare path); a product named in the file is looked up over the whole server, whatever node or folder the import was started on, and one the user does not manage is reported like a name no product has ("No product you manage has this name"), so the answer says nothing more about it. A file that names no products needs a node the user manages; on anything else (a folder, an unknown or unmanaged id) the answer says that nothing was placed. The write itself goes through `PolicyAdministrationService` with `CookieUserAccess`, which checks the Manager role on each sensor's root product again.
- The Users tab of the folder edit page (`FoldersController.ResetUsers`) lists the folder's users with their roles and the users of the server who are not admins and can be added: it is served to a Manager of the folder and to an admin, 404 otherwise.

### Self-registration

- The registration form (`AccountController.Registration`, `Registrate`; anonymous, available only while SSO is off) creates a user who is not an admin and holds no product or folder role. `RegistrationViewModel` binds the name and the two password fields and nothing else. Roles are granted afterwards by a Manager of the product or folder, or by an admin.
- The session cookie names a user by login. `Registrate` signs the request in only after `IUserManager.AddUser` reported that it created the user; when the name was taken in between, the form is shown again with "Username must be unique.".

### Text in pages

- A helper that returns ready HTML encodes the names it is given (`HtmlHelperExtensions.MultiItemsHeader`, the header of the mute dialog).
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
| `UserTreeAccess` | `src/server/HSMServer/Model/TreeViewModels/UserTreeAccess.cs` | Visibility: `TryGetSensor`/`TryGetNode`/`TryGetFolder(user, id, out ...)`, `IsVisible`, static `IsVisibleTo`, `GetVisibleProducts`. Manager role: `TryGetManagedSensor`/`TryGetManagedNode`/`TryGetManagedFolder`, `CanManage`, `GetManagedProducts`, `GetManagedSensorIds`. Singleton in DI |
| `escapeHtml`, `sanitizeHtml` | `src/server/HSMServer/wwwroot/src/js/site.js` | global helpers of the main bundle |

No route, API DTO or storage format changes. The registration form's view model (`RegistrationViewModel`) carries the name and the two passwords only.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Model/TreeViewModels/UserTreeAccess.cs` | The visibility rule, the Manager rule and the id lookups |
| `src/server/HSMServer/Controllers/HomeController.cs` | Tree page actions; hold `UserTreeAccess`, not the tree |
| `src/server/HSMServer/Controllers/AlertsController.cs` | Alerts export and import of a node or folder; holds `UserTreeAccess`, not the tree |
| `src/server/HSMServer/Controllers/AccessKeysController.cs` | `AccessKeysForProduct`: the key list of a product the user sees |
| `src/server/HSMServer/Controllers/FoldersController.cs` | `ResetUsers`: the Users tab list, for the folder's Managers |
| `src/server/HSMServer/Controllers/AccountController.cs` | Self-registration: a user with no role |
| `src/server/HSMServer/Controllers/SensorHistoryController.cs` | History routes; `SelectedTable` hands the table out only while its sensor is visible |
| `src/server/HSMServer/Controllers/JournalController.cs` | Per-node journal feed: re-checks the connected entity, encodes plain cells |
| `src/server/HSMServer/Model/TreeViewModels/SelectedNodeViewModel.cs` | Children pages of the selected node or folder (folder: visible products only) |
| `src/server/HSMServer/Model/History/Personal/Journal/SelectedJournalViewModel.cs` | Journal of the selection (folder: visible products only); `ConnectedId` |
| `src/server/HSMServer/Model/History/Personal/Journal/JournalRecordViewModel.cs` | Builds the Record cell; encodes recorded text |
| `src/server/HSMServer/wwwroot/src/js/site.js`, `confirmation.js`, `file.js`, `tree.js` | `escapeHtml`/`sanitizeHtml`, the dialog and toast helpers, file previews, tree dialogs |

## Storage / Persistence

None.

## UI / Operator Visibility

A refused id looks like a missing one: the node panel shows "No Data", tables and charts are empty, a file download and the key list answer 404, an alerts export answers an empty page. A change the user has no Manager role for is not offered by the page and does nothing when requested.

## Dependencies

- Depends on: product and folder roles (`User.IsProductAvailable`, `User.IsFolderAvailable`), `MarkdownHelper`, `HtmlSanitizerService`, DOMPurify.
- Used by: the tree page, sensor history, the per-node journal tab, the alerts export and import, the key list of a product.

## Tests

- `HomeControllerAccessTests`, `SensorHistoryControllerAccessTests` (`src/tests/HSMServer.Core.Tests/Controllers`): every id-addressed action asked by a member of the product (Viewer), its Manager, a user of another product and an admin; the actions that change something are refused to the Viewer with the answer of an unknown id; the per-user state after a role is taken away; a folder with products the user does not see or does not manage, and one where a role is taken from one of its products; product and folder settings by role; a nested node judged by its root product; requests that name another sensor than the selected one. Shared tree: `WebAccessFixture`.
- `AlertsControllerAccessTests`, `AlertsControllerImportTests`: export and import by role for a node and a folder, a file that names products the user does not manage, and that the import writes. `AccessKeysControllerAccessTests`, `FoldersControllerAccessTests`: the key list and the Users tab list by role. `AccountControllerRegistrationTests`: a posted form that carries more than the form's fields still creates a user with no role; a user that was not created is not signed in. `MultiItemsHeaderEncodingTests` (`.../Views`): the header helper.
- `EncodedOutputViewTests` (`.../Views`): the fixed views rendered through the Razor engine (`ViewRenderer`) with names made of markup; the tree metadata with a quoted name.
- `JournalRecordEncodingTests` (`.../Journal`): the Record cell.
- `home/collector_text_is_shown_as_text.spec.ts` (`src/tests/Autotests`): a sensor registered through the Sensor API with markup in its path, comment and files, walked through the tree, the sensor page, the removal dialog and the file previews.

## Known Issues / Limitations

- The sensor move is checked inside its service only (`SensorMoveAdministrationService` with `CookieSensorMoveAccess`): the controller passes the posted ids on. The policy lists of the sensor form are checked twice: by the controller's lookup and inside `PolicyAdministrationService` (`CookieUserAccess`).
- The mute dialog accepts a folder, but muting a folder does nothing: `SetMutedStateToSensorFromModal` handles sensors and nodes only.
- A folder's Grafana mark and menu entry are computed over the products of the folder the user sees, while the switch reaches the ones the user manages. A folder Manager who only views one of its products, with Grafana off there, is offered "Enable Grafana" on the folder even when it is on for every product they manage; the products themselves offer the right entry.
- A write that fans out over a subtree or a folder checks the role once, when the request arrives; a sensor moved to another product while the request is being applied is still written.
- An alerts import replaces the alert list of each sensor it names with the imported alerts (the semantics of the sensor form's save). The file keeps neither the AND/OR combination of an alert's conditions nor template-owned alerts.
- A folder's journal is loaded when the folder is selected, for the products visible then. Records of a product that stops being visible are left out from the next page on; a product that becomes visible later shows its records after the folder is selected again.
- The per-node journal pages the records of the sensors and nodes that are in the tree now. When a sensor or a node below the selected one is removed while its journal is open, its records (the removal record among them) leave the open table with it; before, they stayed until the next selection. The global Journal page still lists them.
- Comments are stored sanitized (HTML with the text already encoded). Where one is written through a text-only path (the recent files list, a journal record of a manual status change), `<` and `&` typed in the comment show as entities.
- Dashboards, the alert template and alert schedule pages, and the write actions of the product, folder and access key administration pages have their own controllers and are outside this feature.
