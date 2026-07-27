# Feature: Restore from backup

> Owner: storage | Last reviewed: 2026-07-27 | Canonical: yes
> Scope: Operator-driven restore of individual entities from a previously-taken LevelDB backup, starting with Alert Templates.

---

## Overview

The server takes periodic LevelDB backups of the environment database into `DatabasesBackups/` (see `BackupDatabaseService` and `Database.Backup`). Restore is the inverse flow: an admin picks a backup file, picks an entity type, ticks the specific entities to recover, and writes them back into the **live** environment database through the running cache.

This feature exists because incidents like #1312 (a server restart silently wiping all Alert Templates when the index contained duplicate GUIDs) had no recovery path short of hand-editing LevelDB.

Scope of v1: **Alert Templates only**. The entity-type selector in the wizard is the extension point for future types (chats, access keys, folders, etc.).

## Invariants

- Restore writes go through `ITreeValuesCache.AddAlertTemplateAsync` — never directly to LevelDB. This keeps the in-memory cache, the persistent DB, and any downstream sensor-policy application consistent with the normal add/edit path.
- The backup DB is opened **read-only** against an unpacked temp folder. `LevelDBDatabaseAdapter.ForReadOnly` sets `CreateIfMissing = false` so a wrong/missing path surfaces as an open error rather than silently producing an empty DB that "successfully" reads zero templates.
- A temp folder created for a restore session is always deleted: either by `BackupSession.Dispose()` when the wizard closes/expiry-timer fires, or by `RestoreTempCleanupService` on the next server startup. The LevelDB handle is always disposed **before** the folder is deleted (otherwise Windows refuses to delete the `LOCK` file).
- Restore is admin-only — inherits the class-level `[AuthorizeIsAdmin]` on `ConfigurationController`.
- Every restore call is audit-logged with the admin user name, source backup file, and per-item outcome (`inserted` / `overwritten` / `skipped` / `duplicated as <guid>` / `error: …`).

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Pick backup → pick entity type → tick items → resolve collisions → Restore | operator (admin) |
| 2 | Cleanup stale restore temp folders at server startup | server (`RestoreTempCleanupService`) |
| 3 | Expire idle restore sessions after 10 minutes of inactivity | server (`RestoreService` timer) |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `IRestoreService` | `src/server/HSMServer.Core/Restore/IRestoreService.cs` | Surface used by `ConfigurationController`. Singleton DI. |
| `ConfigurationController.ListBackups` | `GET /Configuration/ListBackups` | Returns `BackupFileInfo[]`. |
| `ConfigurationController.OpenBackup` | `POST /Configuration/OpenBackup?fileName=...` | Path-traversal guarded; returns `{ sessionId }`. |
| `ConfigurationController.ListAlertTemplates` | `GET /Configuration/ListAlertTemplates?session=<guid>` | Returns `BackupTemplateItem[]`. |
| `ConfigurationController.RestoreAlertTemplates` | `POST /Configuration/RestoreAlertTemplates` | Body `{ Session, Items: [{Id, Resolution}] }`. Returns `RestoreResult`. |
| `ConfigurationController.CloseRestoreSession` | `POST /Configuration/CloseRestoreSession?session=<guid>` | Releases the temp DB immediately. |
| `CollisionResolution` enum | `src/server/HSMServer.Core/Restore/RestoreModels.cs` | `Overwrite=0, Skip=1, Duplicate=2`. Numeric values are part of the JSON contract. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/Restore/IRestoreService.cs` | Public service surface. |
| `src/server/HSMServer.Core/Restore/RestoreService.cs` | Session dict, zip extraction, read-only open, restore dispatch. |
| `src/server/HSMServer.Core/Restore/BackupSession.cs` | `IDisposable` handle: holds worker + temp path; disposes worker before folder. |
| `src/server/HSMServer.Core/Restore/RestoreModels.cs` | DTOs and `CollisionResolution` enum. |
| `src/server/HSMServer/BackgroundServices/DatabaseServices/RestoreTempCleanupService.cs` | Startup sweep + periodic safety-net sweep of the temp root. |
| `src/server/HSMServer/Controllers/ConfigurationController.cs` | Wizard endpoints (admin-only). |
| `src/server/HSMServer/Views/Configuration/_RestoreWizard.cshtml` | 3-step modal UI. |
| `src/database/HSMDatabase.LevelDB/Database.cs` | `LevelDBDatabaseAdapter.ForReadOnly` — read-only ctor. |
| `src/database/HSMDatabase.LevelDB/DatabaseImplementations/EnvironmentDatabaseWorker.cs` | Added ctor taking a pre-built adapter. |

## Data Flow

```
admin opens wizard
  → ListBackups                                   (enumerate DatabasesBackups/EnvironmentData_*.zip)
  → OpenBackup(fileName)                          (extract zip → restore_<guid>/, open read-only, return sessionId)
  → ListAlertTemplates(sessionId)                 (read AlertTemplates index from read-only worker)
  → user ticks items + per-item CollisionResolution
  → RestoreAlertTemplates({Session, Items})
       for each item:
         Skip         → no-op, record outcome
         Overwrite    → new AlertTemplateModel(entity), AddAlertTemplateAsync(model)  (same Id → cache replaces)
         Duplicate    → model.Id = NewGuid, model.Name = $"{name} (restored …)", AddAlertTemplateAsync(model)
  → CloseRestoreSession(sessionId)                (dispose worker → delete temp folder)
```

If the wizard is abandoned or the server crashes, the session either expires after 10 minutes of inactivity (`RestoreService.SweepExpired`) or is cleaned on the next startup (`RestoreTempCleanupService`).

## Storage / Persistence

- Source backups: `DatabasesBackups/EnvironmentData_<timestamp>.zip` (read-only).
- Temp unpacked DBs: `DatabasesRestoreTemp/restore_<guid>/` — short-lived, deleted on session close or next startup.
- Live writes go through `IEnvironmentDatabase.AddAlertTemplate` via `TreeValuesCache.AddAlertTemplateAsync` — same key/path as the normal add path, no separate restore storage.

## UI / Operator Visibility

The wizard is reached from **Configuration → Backup tab → "Restore" button** (next to the existing "Backup" button). It opens a Bootstrap modal with a 3-step stepper:

1. Dropdown of `EnvironmentData_*.zip` files (name, size, last-write time).
2. Disabled radio locked to "Alert Template".
3. Searchable checklist of `{Id, Name}` with a per-row collision `<select>` (default **Duplicate** for safety). Final "Restore" button shows per-item outcomes in a result table.

`showToast` announces completion. The modal calls `CloseRestoreSession` on close so the temp DB is reclaimed immediately.

## Dependencies

- Depends on: `BackupDatabaseService` (creates the source zip), `ITreeValuesCache` (live write path), `LevelDBDatabaseAdapter` (read-only open), `Database.Backup` (zip layout — `ZipFile.CreateFromDirectory`, inverse is `ZipFile.ExtractToDirectory`).
- Used by: operator UI only. No internal consumers.

## Tests

Required coverage checklist (see plan in #1314):

- `LevelDBDatabaseAdapter_ForReadOnly_*`: open existing DB reads back values; missing path surfaces as open error (no silent creation).
- `RestoreService_*`: all three collision resolutions in one call; session dispose deletes temp folder.
- `RestoreTempCleanupService_*`: stale `restore_<guid>` dirs removed; locked dir skipped without crash.

## Notes

- **`AddAlertTemplateAsync` requires the template's `FolderId` to have at least one product on the live server** (see `TreeValuesCache.cs:1539`). If the folder was deleted after the backup was taken, the restore will return `error: No products found in the selected folder.` and the item will be reported as failed in the result table. The user can still recover by recreating the folder first, or by manually re-applying the template to a current folder.
- Sessions live in process memory only. A server restart loses all open sessions — the wizard will need to reopen the backup. Acceptable for an admin tool; the in-flight cost is one zip extract.
- The 10-minute idle expiry is conservative; the wizard's normal flow takes seconds. Tunable in `RestoreService.SessionIdleTimeout`.

## Known Issues / Limitations

- Only Alert Templates can be restored in v1. Other entity types are blocked by the UI (disabled radio) and not exposed by the service. The entity-type dropdown is the extension point.
- Only the environment database is supported. Restoring the dashboard / server-layout DB is out of scope.
- Cross-server restore (pointing at a backup folder on another host) is not supported — only local `DatabasesBackups/`.
- Restore is manual; there is no scheduled / automatic restore.
