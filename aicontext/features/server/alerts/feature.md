# Feature: Alerts

> Owner: server | Last reviewed: 2026-06-24 | Canonical: yes
> Scope: Server-side alert ownership, creation paths, and the boundary between global (template) and per-sensor alerts.

---

## Overview

HSM alerts are server-side rules that evaluate sensor state and produce notifications or icon changes. There are two ways an alert ends up attached to a sensor:

1. **Global alerts (AlertTemplate)** — wildcard-path + folder-scoped templates that auto-apply policies to every matching sensor. This is the canonical mechanism for non-leaf alerting. Managed from `Views/AlertTemplates/Index.cshtml` and `AlertTemplatesController`.
2. **Per-sensor editor** — the `_Alerts.cshtml` partial rendered inside `_MetaInfo.cshtml` for a selected sensor. Managed through `HomeController.UpdateSensorInfo` and the `AddDataPolicy` / `AddAlertCondition` / `AddAlertAction` partial builders.

Per-node (Folder/Product) alert creation is **removed** as of issue #1142. Templates are the only supported path for alerting scenarios that previously used per-node alerts. The per-sensor editor remains in place because sensors own their runtime state and templates materialize onto sensors.

As of issue #1159 the editor exposes a single "Add" entry point: Inactivity Period is selectable as a condition property (`AlertProperty.TimeToLive`) inside the regular alert editor and the Alert Templates editor. The dedicated "Add Inactivity Period" link and TTL section are removed. Storage semantics are unchanged — the form collection JS routes each row to `DataAlerts[255]` when its main condition is TTL, and the controller's existing split persists it as `TTLPolicy`.

## Invariants

- The `_Alerts.cshtml` editor partial renders only when the selected node is a `SensorInfoViewModel`. `FolderInfoViewModel` and `ProductInfoViewModel` no longer render it.
- `HomeController.AddDataPolicy` and `HomeController.AddAlertAction` return `_emptyResult` for any entity id that is not in `_treeViewModel.Sensors`. Stale JS or direct POSTs against a product/folder id cannot create per-node policies.
- `HomeController.UpdateProductInfo` no longer parses `DataAlerts`; only TTL, description, history, self-destroy, and default-chats propagate from the form.
- The multi-edit TTL modal (`_MultiEditModal` / `HomeController.EditAlerts`) remains the supported way to bulk-set TTL across selected items.
- `AlertsController` (export/import) is preserved as-is.
- `ProductEntity.Policies` (the non-TTL persisted policy-id list on a product) is **dead at runtime**: `ProductModel(ProductEntity)` only loads `entity.TTLPolicies`, never `entity.Policies`. The field exists for backward-compatible deserialization only; a startup migration prunes it.
- A single "Add" entry point is exposed in both `_Alerts.cshtml` and `_AlertTemplate.cshtml`. "Inactivity Period" is one of the options in the regular condition property dropdown (added to every condition view model's `Properties` list). When a new alert is appended from `AddDataPolicy`, the property select is client-side flipped to `TimeToLive` and `.trigger('change')` runs the promote handler in `_ConditionBlock.cshtml` — so the default newly-added alert is TTL, with the interval picker shown and the action schedule restricted to repeat mode. The user can switch the property back to a regular value to demote the row.
- Each `dataAlertRow` carries a `data-alert-type` attribute (`255` for TTL, sensor type byte otherwise). The form collection JS (`_AlertsFormCollection.cshtml` partial, inlined into `_MetaInfo.cshtml` and `AlertTemplate.cshtml`) reads this attribute per row and routes field names to `DataAlerts[255][i]...` or `DataAlerts[sensorType][i]...` accordingly, with per-type counters keeping indices contiguous for the model binder. The attribute is read via jQuery `.attr()`, never `.data()` — `_ConditionBlock.cshtml` updates the attribute via `.attr()` on promote/demote, and jQuery's `.data()` cache is not invalidated by `.attr()`, so a `.data()` read after a `.attr()` write returns the stale pre-toggle value and routes the row to the wrong dictionary key.
- TTL alerts are single-condition: when the main condition's property is `AlertProperty.TimeToLive`, the `_ConditionBlock.cshtml` change handler removes any non-main conditions and hides the "add condition" button.
- The TTL demote path in `_ConditionBlock.cshtml` is a no-op inside an Any-template TTL container (`containerType == ttlKey`). Any templates only allow TTL alerts, so there is no concrete sensor type to demote to — the dropdown reverts to `TimeToLive` and the operation-refetch ajax is skipped, keeping the row routed as `TTLPolicy`. Without this guard the demote would set `data-alert-type` back to the container's key (which is also `ttlKey` for Any) but flip the visual state to regular, producing a row with a non-TTL property persisted as a malformed `TTLPolicy`.
- `ConditionViewModel.Property` defaults to `PropertiesItems.First()`. `AlertProperty.TimeToLive` is the last entry in every condition view model's `Properties` list (asserted by `ConditionViewModelPropertiesTests.DefaultProperty_IsNotTimeToLive`), so a freshly-created alert never defaults to TTL — the user must explicitly pick Inactivity Period.

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
| `src/server/HSMServer/Views/Home/Alerts/_AlertsFormCollection.cshtml` | Shared form-collection JS for `DataAlerts[...]` routing; inlined into `_MetaInfo.cshtml` and `AlertTemplate.cshtml`. |
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

Condition view model guards live in `src/tests/HSMServer.Core.Tests/ConditionViewModelTests/ConditionViewModelPropertiesTests.cs`:

- `PropertiesItems_IncludesTimeToLive` — every condition type offers Inactivity Period in the dropdown.
- `DefaultProperty_IsNotTimeToLive` — a freshly-created alert never defaults to TTL; the user must explicitly pick Inactivity Period. Guards against a `Properties` list reorder silently routing every new row to `TTLPolicy`.

## Notes

- Issue #1141 (parent epic) established that node-level alerts are replaced by global alerts.
- Issue #1142 removed the per-node alert editor UI and endpoint support, and added the storage cleanup migration.
- Issue #1159 consolidated Inactivity Period into the regular alert editor as a condition property; the dedicated `addTtlAlert` link and `dataAlertsList_@ttlType` section were removed from both `_Alerts.cshtml` and `_AlertTemplate.cshtml`. Storage semantics (TTLPolicy vs regular Policy) are unchanged — only the UI entry point and form routing changed.
- Collector-side alert behavior is explicitly out of scope per epic #1141.

## Known Issues / Limitations

- `ProductInfoViewModel.DataAlerts` is still populated but never posted from the form; cleanup deferred to avoid rippling into import/export.
- Switching a template's sensor type after adding alerts empties the unified alerts container (existing behavior for regular alerts; as of #1159 this also clears any TTL rows mixed in). Type changes are rare in practice; preserving TTL across type changes would require splitting the container.
- Demoting a TTL alert (loaded from storage) to a regular alert via the property dropdown does not restore the schedule's "starting at" / "instant send" fields. Those fields are gated server-side by `Model.IsTtl` in `_ActionBlock.cshtml` and never render for TTL rows, so the client cannot show them after demote. Regular-to-TTL-to-regular works correctly because the fields exist in the DOM and are only hidden+disabled by the `.ttlAction` class on `.fullAction`. Workaround: save and reopen the editor.
