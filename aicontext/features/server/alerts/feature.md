# Feature: Alerts

> Owner: server | Last reviewed: 2026-06-22 | Canonical: yes
> Scope: Server-side alert ownership, creation paths, and the boundary between global (template) and per-sensor alerts.

---

## Overview

HSM alerts are server-side rules that evaluate sensor state and produce notifications or icon changes. There are two ways an alert ends up attached to a sensor:

1. **Global alerts (AlertTemplate)** — wildcard-path + folder-scoped templates that auto-apply policies to every matching sensor. This is the canonical mechanism for non-leaf alerting. Managed from `Views/AlertTemplates/Index.cshtml` and `AlertTemplatesController`.
2. **Per-sensor editor** — the `_Alerts.cshtml` partial rendered inside `_MetaInfo.cshtml` for a selected sensor. Managed through `HomeController.UpdateSensorInfo` and the `AddDataPolicy` / `AddAlertCondition` / `AddAlertAction` partial builders.

Per-node (Folder/Product) alert creation is **removed** as of issue #1142. Templates are the only supported path for alerting scenarios that previously used per-node alerts. The per-sensor editor remains in place because sensors own their runtime state and templates materialize onto sensors.

## Invariants

- The `_Alerts.cshtml` editor partial renders only when the selected node is a `SensorInfoViewModel`. `FolderInfoViewModel` and `ProductInfoViewModel` no longer render it.
- `HomeController.AddDataPolicy` and `HomeController.AddAlertAction` return `_emptyResult` for any entity id that is not in `_treeViewModel.Sensors`. Stale JS or direct POSTs against a product/folder id cannot create per-node policies.
- `HomeController.UpdateProductInfo` no longer parses `DataAlerts`; only TTL, description, history, self-destroy, and default-chats propagate from the form.
- The multi-edit TTL modal (`_MultiEditModal` / `HomeController.EditAlerts`) remains the supported way to bulk-set TTL across selected items.
- `AlertsController` (export/import) is preserved as-is.
- `ProductEntity.Policies` (the non-TTL persisted policy-id list on a product) is **dead at runtime**: `ProductModel(ProductEntity)` only loads `entity.TTLPolicies`, never `entity.Policies`. The field exists for backward-compatible deserialization only; a startup migration prunes it.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Define a global alert via AlertTemplate (wildcard path + sensor type + folder) | Operator |
| 2 | Apply template policies to matching sensors (auto on create/update) | Server (`TreeValuesCache.AddAlertTemplateAsync`) |
| 3 | Edit TTL on a single sensor via per-sensor editor | Operator |
| 4 | Bulk-set TTL across selected items via multi-edit modal | Operator |
| 5 | Import/export sensor alerts via `AlertsController` | Operator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `AlertUpdateRequest` | `src/api/HSMSensorDataObjects/SensorRequests/AddOrUpdateSensor/AlertUpdateRequest.cs` | Wire DTO; unchanged by this feature. |
| `AddDataPolicy(type, entityId, folderId?)` | `HomeController.cs:742` | Sensor-only as of #1142. |
| `AddAlertAction(entityId, isMain, isTtl, folderId?)` | `HomeController.cs:764` | Sensor-only as of #1142. |
| `UpdateProductInfo(ProductInfoViewModel)` | `HomeController.cs:921` | Drops alert parsing as of #1142. |
| `EditAlerts(EditAlertsViewModel)` | `HomeController.cs` | Multi-edit TTL; unchanged. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Views/Home/_MetaInfo.cshtml` | Hosts the `_Alerts.cshtml` partial; gated to `SensorInfoViewModel`. |
| `src/server/HSMServer/Views/Home/Alerts/_Alerts.cshtml` | Per-sensor alert editor. |
| `src/server/HSMServer/Views/Tree/_MultiEditModal.cshtml` | Multi-edit TTL modal. |
| `src/server/HSMServer/Controllers/HomeController.cs` | Alert mutation endpoints + `UpdateProductInfo`. |
| `src/server/HSMServer/Controllers/AlertTemplatesController.cs` | Global alert template CRUD. |
| `src/server/HSMServer/Controllers/AlertsController.cs` | Alert import/export only. |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.cs` | Template application (`AddAlertTemplateAsync`); product-owned policy cleanup at startup. |
| `src/server/HSMServer.Core/Model/AlertTemplateModel.cs` | Template model: path wildcard, folder, sensor type, policies. |

## Data Flow

```
Operator -> AlertTemplates UI -> AddAlertTemplateAsync
  -> persist template
  -> for each matching sensor in folder (GetSensorsByWildcard)
       -> queue ApplyTemplateRequest
       -> ApplyTemplateToSensor upserts/deletes sensor policies
       -> policies tagged with TemplateId + TemplateAlertId
New sensor arrives -> if matches existing template -> template policies applied on insert
Operator selects sensor -> per-sensor _Alerts.cshtml editor -> UpdateSensorInfo -> sensor.Policies updated
```

## Storage / Persistence

- `PolicyEntity` rows live in a single LevelDB table keyed by `byte[] Id` (Guid). The `"NewPolicyIds"` index lists all known ids.
- `BaseNodeEntity.Policies` (a `List<string>` of stringified Guids) exists on every Product and Sensor entity. At runtime, only `SensorEntity.Policies` is loaded into the in-memory `SensorPolicyCollection`. `ProductEntity.Policies` is **never read** into `ProductModel` — only `TTLPolicies` is.
- Template-derived policies carry non-null `TemplateId` / `TemplateAlertId`; user-added policies have `TemplateId == null`.
- `TreeValuesCache.CleanupProductOwnedPolicies` runs once at startup (`Initialize()`, after `ApplyProducts`) and deletes any `PolicyEntity` referenced by `ProductEntity.Policies` whose `TemplateId == null`, then prunes the dangling references from the list. The migration is idempotent: a second run finds an empty list and writes nothing.

## UI / Operator Visibility

- Per-sensor alert editor visible on sensor pages only.
- TTL on Folder/Product visible under "Inactivity Period" in `_GeneralInfo.cshtml` (was previously also duplicated inside `_Alerts.cshtml` for those node types — that duplicate is no longer rendered).
- Tree-row alert icons (`_AlertIconsList.cshtml`) still reflect template-derived policies.
- Alert templates managed from a dedicated top-level page.

## Dependencies

- Depends on: storage layer (PolicyEntity, BaseNodeEntity), AlertSchedule feature (referenced from per-alert).
- Used by: notification delivery, icon rendering, TTL evaluation.

## Tests

Coverage for the product-owned policy cleanup lives in `src/tests/HSMServer.Core.Tests/TreeValuesCacheTests/ProductOwnedPolicyCleanupTests.cs`:

- `Cleanup_RemovesUserAddedPolicy_FromProductEntityPoliciesList`
- `Cleanup_PreservesTemplateDerivedPolicy_ReferencedByProduct`
- `Cleanup_PreservesTtlPolicies_OnProduct`
- `Cleanup_PreservesSensorPolicies`
- `Cleanup_UpdatesProductEntityPoliciesList_WithSurvivingIds`

## Notes

- Issue #1141 (parent epic) established that node-level alerts are replaced by global alerts.
- Issue #1142 removed the per-node alert editor UI and endpoint support, and added the storage cleanup migration.
- Collector-side alert behavior is explicitly out of scope per epic #1141.

## Known Issues / Limitations

- `_Alerts.cshtml` still contains an unreachable `FolderInfoViewModel` branch. Removing it is a cleanup for a future PR.
- `ProductInfoViewModel.DataAlerts` is still populated but never posted from the form; cleanup deferred to avoid rippling into import/export.
