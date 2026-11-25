# Feature: Dashboard panel subscriptions

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: how a dashboard panel gets its sources from a subscription (a path template), by a scan and by itself when a sensor appears or moves, and when the dashboard is stored.

---

## Overview

A dashboard panel plots **sources**. A source is one sensor with one plotted property (`PanelDatasource`). A user adds a source by hand, or lets a **subscription** add it: a path template over the sensor's full path, with an optional folder filter, a plotted property and a label template (`PanelSubscription`).

A subscription adds sources in two ways:

- **Scan and apply.** The user starts a scan, sees which sensors match, and applies it. Apply adds a source for every matched sensor the panel accepts and turns the subscription on (`IsSubscribed`).
- **By itself, while it is on.** A sensor that is created, or moved (#1719), under a path the template covers is added without a user action.

This document covers the subscriptions only. Panel layout, chart data and the dashboard UI are not described here.

## Invariants

- **Match.** A subscription covers a sensor when all of these hold: the plotted property exists for the sensor's type, an EMA property is asked only of a sensor that has EMA statistics, the path template matches the whole `FullPath` (the semantics are those of alert templates, see `alerts/feature.md`), and the folder filter covers the folder of the sensor's root product.
- **Folder filter.** No filter (`Folders` is null) covers every product. In a filter, `Guid.Empty` stands for "the product is in no folder". A product in no folder is covered only by a filter that contains `Guid.Empty`; a filter of real folders does not cover it, and the check must not throw for it.
- **Acceptance by the panel.** A panel takes a source when the sensor type can be plotted, the type equals the panel's type, the unit matches the panel's unit (a missing unit on either side matches any), and the panel has fewer than 100 sources. The first source sets the panel's type; the first source that has a unit sets the panel's unit. Removing the last source clears both.
- **A rejected candidate leaves nothing behind.** A subscription builds its candidate source before the panel decides, and a candidate whose plotted property differs from the source's default already listens to the sensor's values. `Panel.TrySaveNewSource` disposes every source it does not add, so no handler stays on the sensor. This holds for both ways of adding.
- **An automatic add is stored at once.** The dashboard is one entity in the store and is written when it raises `UpdatedEvent` (`DashboardManager`). A source added for a new or a moved sensor raises it: once per sensor and panel, also when several subscriptions of the panel add a source each. A sensor no subscription covers costs no write. Without this the source would be gone after a restart, and it would not come back: the sensor is no longer new, so no subscription is asked about it again.
- **The handlers must not throw.** `Dashboard` handles `ChangeSensorEvent` and `SensorMovedEvent` on the thread of the cache, inside the creation or the move. An exception that leaves the handler of a new sensor makes the cache roll the sensor back.
- A moved sensor that already has a source on the panel is not added again.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Scan a subscription and apply the result | operator |
| 2 | A new sensor is added to the panels whose subscriptions cover it | server (`ChangeSensorEvent`, `ActionType.Add`) |
| 3 | A moved sensor is added to the panels whose subscriptions cover its new path | server (`SensorMovedEvent`) |
| 4 | A removed sensor loses its sources | server (`ChangeSensorEvent`, `ActionType.Delete`) |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Dashboards/Dashboard.cs` | Subscribes to the cache's events and offers the sensor to every panel |
| `src/server/HSMServer/Dashboards/Panels/Panel.cs` | `AddSensor` / `AddMovedSensor` / `TryApplyScanResults`; `TrySaveNewSource` is the single place a source is accepted or rejected |
| `src/server/HSMServer/Dashboards/Panels/Modules/PanelSubscription.cs` | Match rules, folder filter, candidate source |
| `src/server/HSMServer/Dashboards/Panels/PanelSensorScanTask.cs` | The scan |
| `src/server/HSMServer/Dashboards/DashboardManager.cs` | Writes a dashboard to the store on its `UpdatedEvent` |

## Storage / Persistence

A dashboard is stored whole, with its panels, sources and subscriptions, in the `ServerDashboards` collection. A failed database write is logged by the collection and does not throw.

For a new sensor the write runs on the cache's thread under the tree-structure lock, next to the write of the sensor's own row: the whole dashboard is serialized and written once per panel that gained a source. A collector that registers many sensors a subscription covers causes as many dashboard writes.

## Tests

- `src/tests/HSMServer.Core.Tests/Dashboards/PanelSubscriptionSourcesTests.cs`: automatic adds over a real cache and a real `DashboardManager` (stored and read back by a second manager, one write per sensor), the folder filter for a product in no folder, rejected candidates for a new sensor and for a scan.
- `src/tests/HSMServer.Core.Tests/Dashboards/PanelSubscriptionTests.cs`: match rules of a single subscription (malformed template, folder filter).

## Known Issues / Limitations

- A sensor covered by two subscriptions of one panel gets two sources, also when both plot the same property.
- A panel does not record which subscription added a source. A source stays on the panel when its sensor moves out of the template, and when the subscription is removed.
- Acceptance is a check followed by a separate insert, and the automatic adds are not serialized per panel. Two events handled at the same time can add two sources for one sensor, exceed the limit of 100, or put two types on an empty panel.
- A subscription loaded from the store adds what arrives after the start, not what is already there: a sensor created while the subscription was off is added only by a new scan.
