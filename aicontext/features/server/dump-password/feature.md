# Feature: Dump password

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: the password of a root product under which its secure large file values are given out (#1885, ADR-0022 point 13): who sets and sees it, how it is generated and checked, its versions and their retention, where it is stored, and what it does and does not protect. The archives built under it and the download are `../large-file-download/feature.md`.

---

## Overview

The password applies to **secure** files only (glossary "Secure file"): every secure large file value leaves the server as a zip encrypted with AES-256 under the dump password of the file's root product. A file that is not secure (the application decides at the send) leaves as the same zip without encryption, to everyone who may see the product, whether or not the product has a password; a password change does not rewrite it and password versions do not apply to it. The password is set and seen on the product's settings page by a Manager of that product and by an admin; no API, MCP tool, journal record or log line carries its value. HSM offers a generated password; one typed by a person has to pass rules of its own.

A change of the password makes a new version. The secure archives of the product are re-encrypted with it in the background; each archive records the version it opens with, and a download names it in `X-Hsm-Password-Version`. A replaced version stays readable to Managers and admins until 30 days after its last archive was re-encrypted, so a copy downloaded earlier can still be opened.

While a product has no password, its secure large files are accepted and kept verified, and cannot be downloaded; its files that are not secure are packed and downloadable as usual.

## Invariants

- **One password per root product.** A nested node resolves to its root; the service refuses a product that is not a root.
- **Set and seen by a Manager of the root product or an admin** (`User.IsManager(root)`, which is true for an admin). Everyone who may see the product sees only whether a password is set.
- **Never in an answer of `/api/v1` or MCP, never in a log line, a journal value or an exception text.** The value reaches a browser only in the JSON answer of the page's POST actions. Product models, view models, change sets and DTOs do not hold it: it is stored apart from the product entity.
- **Versions are atomic, unique and durable.** Every change runs under one lock: the next version is one past the higher of the highest kept version and the highest version a packed archive of the product carries (read from the archive store), so a version is never given twice even after the row was lost (a power loss, a restore of an older backup); the database row is written first and memory changes only after the write landed. Two Managers saving at once get two consecutive versions, both durable. A failed write changes nothing, journals nothing, raises nothing and fails the action.
- **A password can be replaced, never cleared.** An empty value is refused.
- **The current version is never removed.** A replaced version is retired when no archive carries it and no build reads or writes it, and removed `PasswordRetentionDays` (30) after that.
- **Removed with the product.** Deleting a root product removes its row.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | See whether the product has a dump password | anyone who may see the product |
| 2 | Generate a password, copy it, save it | Manager of the root product, admin |
| 3 | Type a password and save it | Manager of the root product, admin |
| 4 | Show the values of every kept version (to open an archive downloaded under an older one) | Manager of the root product, admin |
| 5 | Retire and remove replaced versions | archive builder (background) |

## The settings section

The product settings page (`EditProduct`) carries a **Dump password** section (`Views/Product/_DumpPassword.cshtml`) for everyone who may see the root product (`User.IsProductAvailable`):

- **Everyone:** "Set" or "Not set" (with "the product's secure large files are kept and cannot be downloaded" when not set), and a short text: the password applies to secure large files only; every secure large file is given out only as an AES-256 zip under this password; a large file that is not secure (the sending application decides) is given out as the same zip without encryption to everyone who may see the product, whether or not a password is set, and a new password does not change it; it opens in 7-Zip or WinRAR and usually not in the archiver built into Windows; the password does not hide the name of the file inside, its sizes or its time; a new password re-encrypts the kept secure archives in the background, and until then an archive opens with the version it was made with, which the download names.
- **A Manager of the root product or an admin, additionally:** the table of kept versions (version, set at, set by, state: current; replaced, archives being re-encrypted; replaced, kept until a date) without values; **Show**, which fetches the values; a field with **Generate** and **Save**.

The page model never holds a value. The three actions of `DumpPasswordController` are POSTs with a JSON body, cookie-authenticated, `[ValidateAntiForgeryToken]` (the token travels in the `RequestVerificationToken` header) and `Cache-Control: no-store`:

| Action | Body | Answer |
|---|---|---|
| `POST /DumpPassword/Show` | `{productId}` | `{versions: [{version, password, setUtc, setBy, isCurrent, retiredUtc, keptUntilUtc}]}`, newest first |
| `POST /DumpPassword/Generate` | `{productId}` | `{password}`; nothing is stored until it is saved |
| `POST /DumpPassword/Set` | `{productId, password, generated}` | `{version, message}` |

An unknown product, and a product the caller cannot see (`User.IsProductAvailable(root)` false), answer the same 404; a caller who sees the product but is not a Manager of the root product nor an admin, 403; a password that breaks a rule, 400 `{error}` with the rule. The server log records "set to version N by user" without the value.

## Generation and typed rules

- **Generated:** 24 characters from `A–Z a–z 0–9` (62 symbols), drawn with `RandomNumberGenerator` — about 143 bits. Letters and digits only: nothing an archiver encodes differently, nothing hard to type.
- **Typed:** at least 16 characters, at most 256, printable ASCII only (`0x20`–`0x7E`, the space included).
- `Set` treats a value as generated only when the page says so and the value looks generated (24 characters of the alphabet); anything else is checked as typed, so the flag cannot skip the rules.

## Versions and their retention

- A version holds the value, the time it was set, who set it (the initiator) and, once retired, the time it was retired.
- The current version is the highest. New secure archives are built under it; secure archives under older versions are re-encrypted one at a time by the archive builder, which takes the (password, version) pair once at the start of each build.
- At the end of each pass, the builder retires every replaced version that no secure archive carries and no build in flight uses (an archive that is not secure carries no version and keeps none alive). A retired version stays readable on the page with "kept until" for `LargeFileArchives.PasswordRetentionDays` (30 by default, 0–3650) and is then removed by the builder's pass. A version that can no longer be read cannot be used to re-encrypt, so a version an archive still carries is never retired.
- After a restart the builder also looks at products that have a password but no file seen since the start.

## Journal

A saved version writes one journal record after the durable write: kind `nodeSettings`, property `Dump password`, new value `changed (version N)`, no old value, the product's path, the user as initiator. A failed journal append is logged and does not undo or fail the change (the journal's durability rule). See `../journal/feature.md`.

## Storage / Persistence

- The configuration database (`EnvironmentData`), a collection of its own: key `DumpPasswords_<productId>`, value `ProductDumpPasswordsEntity { ProductId, Versions: [{ Version, Password, SetTicks, SetBy, RetiredTicks }] }` as JSON. The value is stored as it is: the server needs it to build an archive.
- Loaded into memory at the start; a row that cannot be parsed is skipped and logged by its key, which names the product only.
- Writes and removals throw on a storage failure (unlike product writes), so a change that did not land fails visibly. Both are synced LevelDB writes (`PutSynced` / `DeleteSynced`): a version answered as saved is on the disk before the answer.
- The entities' `ToString` prints the product and the version count, never a value.
- The configuration database's backups contain the rows, values included.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `POST /DumpPassword/Show`, `/Generate`, `/Set` | `src/server/HSMServer/Controllers/DumpPasswordController.cs` | Web UI only; antiforgery; no-store |
| `X-Hsm-Password-Version` | `Model/ManagementApi/LargeFiles/LargeFileDownloadResult.cs` | The version on every answer for a secure archive, absent for one that is not secure; the value is never sent |
| `secure`, `passwordVersion`, `passwordSetUtc` | `Model/ManagementApi/LargeFiles/LargeFileDtos.cs` | In the description of a file (REST and MCP); the version and its time are null for a file that is not secure; no value |

There is no API or MCP route to read or set a dump password. An AI agent gets it from its operator.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/LargeFiles/DumpPasswords/IDumpPasswordService.cs` | The service contract, the rules (`DumpPasswordRules`), the outcomes |
| `src/server/HSMServer.Core/LargeFiles/DumpPasswords/DumpPasswordService.cs` | Versions under one lock, durable writes, journal record, retirement and removal, product deletion |
| `src/server/HSMServer/Model/LargeFiles/DumpPasswordPageService.cs` | The page decisions: root resolution, roles, view models without values, the generated-flag check |
| `src/server/HSMServer/Controllers/DumpPasswordController.cs` | The three POST actions |
| `src/server/HSMServer/Views/Product/_DumpPassword.cshtml` | The settings section |
| `src/database/HSMDatabase.AccessManager/DatabaseEntities/ProductDumpPasswordsEntity.cs` | The stored row |
| `src/database/HSMDatabase.LevelDB/DatabaseImplementations/EnvironmentDatabaseWorker.cs` | The `DumpPasswords_` collection |

## Dependencies

- Depends on: the configuration database, the journal, the tree cache (root products, product deletion), the web UI roles.
- Used by: the archive builder and the download (`../large-file-download/feature.md`).

## Tests

- `src/tests/HSMServer.Core.Tests/LargeFiles/DumpPasswords/DumpPasswordServiceTests.cs` — a version is stored and becomes current; consecutive durable versions under concurrent saves; the versions read back from the database; a failed write changes nothing, journals nothing and raises nothing (also for a first password); a journal failure neither fails nor undoes the change; a throwing subscriber; unknown and non-root products refused without a write; the length bounds; the generator's length and alphabet; retirement set once and never on the current version; removal of expired retired versions only, never the current one; storage failures keep the versions and throw; removal with the product, also through the tree's delete event, without throwing into the cache; the journal record carries no password; the records print no password; a version is allocated past the highest one an archive carries, also after the row was lost, and a failure to list the archives fails the change without a write.
- `src/tests/HSMServer.Core.Tests/LargeFiles/Downloads/LargeFileArchivesRegistrationTests.cs` — the registered service reads the carried versions from the archive store, without a dependency cycle with the builder.
- `src/tests/HSMDatabase.LevelDB.Tests/EnvironmentDBTests/DumpPasswordsSyncedWriteTests.cs` — the row is put and removed with synced writes.
- `src/tests/HSMServer.Core.Tests/LargeFiles/DumpPasswords/DumpPasswordServiceLoggingTests.cs` — no log line and no exception text carries a password.
- `src/tests/HSMServer.Core.Tests/Controllers/DumpPasswordControllerTests.cs` — the actions are never cached; a Viewer cannot show, generate or set; an unknown product is 404, and a user with no role on the product gets the same 404; a Manager sees every kept value newest first with the replaced one marked; a nested node resolves to the root product; the generated flag is honoured only for a value that looks generated; a too-long value is refused before the service; a save is logged and journaled without the value; the page model never holds a value.

## Known Issues / Limitations

- **What the password does not hide:** the name of the file inside an archive, its sizes and its time are readable without it.
- **An archive that left the server is as strong as its password.** The format derives its key with 1,000 rounds of PBKDF2, and a downloaded archive can be attacked offline for as long as it exists; hence the generated password and the minimum length of a typed one.
- A password given to many people cannot be taken back from one of them; a new version protects only the archives re-encrypted under it.
- The server holds the password as it is, in its configuration database and that database's backups, and holds a file without the password while it is uploaded and checked. The password protects a downloaded copy, not the server.
