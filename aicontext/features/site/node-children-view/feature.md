# Feature: Node children view (overlay chart + ranking)

> Owner: site | Last reviewed: 2026-07-08 | Canonical: yes
> Status: **Proposed — not yet implemented.** Tracked by SoftFx/Hierarchical-Sensor-Monitoring#1235 (v1).
> Scope: When an operator selects a tree node, show its child sensors *together* over a chosen time window — first as one overlaid multi-line chart (v1), later folded into a ranked "top consumers" summary (v2).

---

## Overview

Today, selecting a node renders each child sensor as its **own separate tile/plot**. To compare children — which process used the most CPU, which interface moved the most data, which disk was busiest over the last hour — the operator must open each sensor individually and eyeball its chart. There is no node-level combined view.

This feature adds a node-level view that fetches every comparable child sensor's history over one window and presents them together, with **zero configuration**: the operator clicks the node and picks a period; the view assembles itself from the node's descendants.

- **v1 — Chart:** one time-series chart, one line per child (raw values, no aggregation).
- **v2 — Ranking:** the same data folded into a sorted leaderboard (total / average / peak).
- **v3 — Heterogeneous nodes, scope, and per-type aggregation strategies.**

Dashboards already allow multi-line panels, but require adding each sensor by hand; this is the node-driven, no-setup version.

## Invariants

- Overlay/ranking happens only over **comparable** children — a shared sensor type **and** unit. Mixed-unit nodes (e.g. `Disks monitoring`: `%`, `MB/s`, `GB`) are split into one group per unit; children of different units are never plotted on a single Y axis or ranked against each other.
- A node with **fewer than 2 comparable children** shows no view and falls back to the normal node panel.
- The view is **read-only** — derived entirely from stored history; it never writes or mutates sensor state.
- The time window is operator-chosen (last hour / 3h / day / custom) and shared by every series. A child with **no data in the window is omitted, not zero-filled**.
- Aggregation (v2) is **per sensor type**: instant numeric contributes its value; bar sensors contribute `Mean` (line) / `Max` (peak); rate/counter → delta over window; enum/bool → availability.
- **Sparse series stay honest:** a child that reports intermittently (e.g. top-N CPU processes, which report only while in the top-10 at ≥1%) is drawn with gaps/markers, never interpolated across the gap.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Select node → pick period → view overlaid multi-line chart of children (v1) | operator |
| 2 | Toggle to Ranking → children sorted by total/avg/peak over the window (v2) | operator |
| 3 | Mixed-unit node → pick which unit group to view/rank (v3) | operator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| Node panel render | `HomeController.SelectNode` → `Views/Home/_NodeDataPanel.cshtml` | new Chart/Ranking tab added here |
| Child sensor ids | `TreeViewModel.GetAllNodeSensors(Guid)` (via `HomeController.GetNodeSensors`) | already returns all descendant sensor ids of a node |
| Per-sensor history | `SensorHistoryController.ChartHistory` (`GetSensorHistoryRequest`) | existing; Plotly chart data for one sensor |
| Multi-target history (prior art) | `grafana/JsonDatasource` `query` (`QueryHistoryRequest.Targets[]`) | precedent for one request → many series; the node-batch endpoint should follow this shape |

**New (v1):** a node-history endpoint taking a node id + window (+ optional unit-group) that returns the full series set in one call, so the client makes one request instead of N. Reuse `GetAllNodeSensors` for enumeration and the same history read `ChartHistory` uses.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Controllers/HomeController.cs` | `SelectNode` (node panel), `GetNodeSensors` → `GetAllNodeSensors` |
| `src/server/HSMServer/Controllers/SensorHistoryController.cs` | existing per-sensor history (`ChartHistory`/`TableHistory`); the node-batch endpoint lands here or in `HomeController` |
| `src/server/HSMServer/Model/TreeViewModels/TreeViewModel.cs` | `GetAllNodeSensors(Guid)` (descendant sensor ids); sensor type/unit used for grouping |
| `src/server/HSMServer/Views/Home/_NodeDataPanel.cshtml` | node panel; hosts the new Chart/Ranking tab |
| `src/server/HSMServer/Views/Home/Sensor/History/_SensorGraphTabContent.cshtml` | existing Plotly graph tab to model the multi-series chart on |
| `src/server/HSMServer/wwwroot` (Plotly.js 2.28.0) | client charting already in the bundle — reuse for the overlay |

## Data Flow

1. Operator selects node `N` and window `[from, to]`.
2. Server resolves children = `GetAllNodeSensors(N)` → leaf sensors; groups them by `(type, unit)`.
3. For each comparable group, read each sensor's history over `[from, to]` (same read path as `ChartHistory`).
4. **v1** returns the series (per sensor: points) → Plotly overlays them on one shared time axis.
5. **v2** folds each series to a scalar via a per-type strategy — instant numeric → total (area ≈ resource-time) / avg / peak; bar → `Mean`/`Max`; rate/counter → delta over window; enum/bool → % uptime / downtime / flaps — then sorts into a leaderboard.

## Storage / Persistence

None. Reads existing sensor history only.

## UI / Operator Visibility

This *is* the operator-visible surface: a new tab in the node data panel. Add a `screens/site/` spec when the screen behavior lands.

## Dependencies

- Depends on: sensor-tree (node selection), stored sensor history, the Plotly bundle.
- Used by: operators diagnosing "which child was hottest over this window".

## Tests

Create `tests.md` when v1 lands. Required coverage:

- happy path — homogeneous node (`Top CPU processes`, `Network`) → chart renders a line per child;
- boundary — 0 or 1 comparable child → no view;
- mixed-unit node → grouped by unit, never one shared axis;
- sparse series → gaps preserved, not interpolated;
- window with no data for a child → that series omitted (not zeroed).

## Notes

- The multi-line chart is the **substrate**; the ranking is the same data aggregated. Build v1 first, add v2 on the same fetch.
- Reuse Plotly (already bundled) and the existing history read; the only new server surface is the node-level batch that fans out over `GetAllNodeSensors`.

## Known Issues / Limitations

- **v1** (#1235): overlay multi-line chart for **same-unit nodes only**; no ranking, no mixed-unit grouping.
- **v2:** ranking/leaderboard tab on the same data.
- **v3:** mixed-unit grouping, scope toggle (direct children vs flattened subtree), full per-type aggregation strategies (rate/counter delta, enum/bool availability).
- Top-N-sampled folders (`Top CPU processes`) yield intermittent series by design — the UI must explain the gaps rather than hide them.
