# ADR-0001: Remove user-facing node-level alert creation on non-leaf nodes

**Status:** Accepted
**Date:** 2026-06-22
**Supersedes:** —

---

## Context

HSM historically allowed operators to attach alert rules directly to non-leaf tree nodes (Folders and Products) via the `_Alerts.cshtml` partial inside the right-hand `_MetaInfo` panel. The same `_Alerts.cshtml` editor was also rendered for sensors.

In parallel, the `AlertTemplate` system already provided a "global alert" mechanism: a template carries a wildcard path, folder scope, and sensor type filter, and the server auto-applies its policies to every matching sensor (and re-applies on future sensor creation). Templates are managed from a dedicated top-level page (`AlertTemplatesController` / `Views/AlertTemplates/`).

Epic #1141 decided that maintaining two parallel surfaces — per-node alert editors and global templates — creates duplicate configuration surfaces and makes alert behavior harder to reason about. Issue #1142 is the implementation.

Investigation during planning found two things that reduced scope:

1. `ProductUpdate` has no `Policies` field (only `TTLPolicies`). `HomeController.UpdateProductInfo` parsed `DataAlerts` from the form but only projected TTL into the update — typed data alerts added via the Product editor were silently dropped on submit. The per-node typed-alert UI was already dead.
2. `ProductModel(ProductEntity)` calls `Policies.BuildDefault(this, entity.TTLPolicies)` and never reads `entity.Policies`. The persisted `ProductEntity.Policies` list is dead at runtime; only `entity.TTLPolicies` is loaded. Legacy rows may still exist on disk.

## Decision

Remove the user-facing per-node alert editor from Folder and Product pages. Keep the per-sensor editor. Templates are the canonical path for non-leaf alerting scenarios.

Concretely:

- `_MetaInfo.cshtml` gates the `_Alerts.cshtml` partial render to `SensorInfoViewModel` only.
- `HomeController.AddDataPolicy` and `HomeController.AddAlertAction` reject any entity id that is not a sensor (`_treeViewModel.Sensors` lookup; return `_emptyResult`).
- `HomeController.UpdateProductInfo` drops its `DataAlerts` parsing block. TTL still propagates via the `TTL = newModel.ExpectedUpdateInterval.ToModel(...)` field, and TTL display on Folder/Product is preserved by `_GeneralInfo.cshtml` ("Inactivity Period").
- The multi-edit TTL modal (`_MultiEditModal` / `HomeController.EditAlerts`) and the import/export endpoints (`AlertsController`) remain unchanged.
- A one-time startup migration (`TreeValuesCache.CleanupProductOwnedPolicies`) deletes any `PolicyEntity` whose id appears in a `ProductEntity.Policies` list and whose `TemplateId == null`, then prunes the dangling references from the list. The migration is idempotent.

## Consequences

- Positive: One canonical way to define non-leaf alerts (templates). UX is simpler. Dead code paths removed (`TryGetSelectedNode`, `UpdateProductInfo` alert parsing block). Storage is cleaned up on upgrade.
- Negative: Operators who relied on clicking "Add" on a Product/Folder to author an alert must use the templates UI instead. Any per-node typed-data alert they had configured was already non-persistent, so no real data loss occurs for typed alerts. TTL on Folder/Product remains configurable via multi-edit and visible under "Inactivity Period".
- Compatibility: Collector wire format is unchanged. `ProductEntity.Policies` remains in the storage schema for backward-compatible deserialization but is no longer populated; the migration prunes existing rows.

## Alternatives Considered

- **Remove the entire `_Alerts.cshtml` for all node types including sensors**: Rejected. Sensors own their runtime policies (including template-materialized ones) and the per-sensor editor is still useful for direct authoring and TTL management.
- **Keep typed-data alerts, only remove TTL editor**: Rejected. The typed-data-alert UI on Products was already non-persistent (silent drop in `UpdateProductInfo`), so leaving the UI in place would have preserved a confusing dead control.
- **Auto-migrate per-node alerts to templates on upgrade**: Rejected. Typed data alerts on Products were never actually persisted, so there is nothing to migrate. TTL on Products is preserved (not migrated) because it remains a supported configuration via multi-edit.
- **Leave existing per-node storage rows in place and ignore at runtime**: Rejected in favor of explicit cleanup, so storage matches the new invariant and operators do not accumulate orphan rows.

## References

- Issue #1141 (parent epic): replace node-level alerts with global alerts.
- Issue #1142 (implementation): remove node-level alert creation/edit/delete flows on non-leaf nodes.
- `aicontext/features/server/alerts/feature.md`: canonical alert model documentation.
