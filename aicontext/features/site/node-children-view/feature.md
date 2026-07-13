# Feature: Node Children Chart (overlay comparable child sensors)

> Owner: site | Last reviewed: 2026-07-13 | Canonical: yes
> Scope: A read-only "Chart" tab on a tree node that overlays its comparable child sensors' history on one multi-line time chart over an operator-chosen window.

---

## Overview

Selecting a tree node normally shows each child sensor as its own tile. To answer "which
child was hottest over the last hour" (which process used the most CPU, which NIC moved the
most data), the operator had to open each child one by one.

The node **Chart** tab overlays every *comparable* child sensor as one line on a single time
chart. It is entirely derived from stored history: it never writes or changes sensor state.

**v1 (this feature) — grouped overlay with a group selector:**

- The server groups the node's chart-comparable descendant sensors by `(SensorType, effective unit)`.
  Each group of >= 2 sensors can be overlaid on its own chart.
- The Chart tab overlays the **largest** group by default. When a node has more than one comparable
  group, a **"Type" selector** (next to the Period selector) lets the operator switch which group
  `(type, unit)` is charted — one unit at a time, so the y-axis stays meaningful.
- One line per child in the selected group. Children with no data in the window are **omitted** (not
  zero-filled). Sparse/intermittent series (e.g. top-N processes reported only while active) are drawn
  with **gaps** — `connectgaps: false` — never interpolated across the gap.
- The tab is **not rendered** when the node has no group of >= 2 comparable children.

## Invariants

- **Read-only.** Derived from stored history via the existing history-read path; never writes,
  never mutates sensor/TTL state.
- **Comparable = numeric line-able + same type + same effective unit.** v1 comparable types:
  `Integer, Double, Rate, IntegerBar, DoubleBar`. Excluded: `Boolean, Enum, Version, String,
  TimeSpan, File` and service `status`/`alive` step sensors (their availability aggregation is v3).
- **Effective unit** = `DisplayUnit` for `Rate` sensors, otherwise `SelectedUnit` (`OriginalUnit`).
  Two unitless sensors of the same type still group together. `EffectiveUnitCode` (group key) and
  `EffectiveUnitLabel` (displayed unit) must branch on the **same** source: a Rate sensor without a
  `DisplayUnit` is unit-less (code `null`, label empty) — it never borrows `SelectedUnit` for the label,
  which would let two such sensors share the `null` key yet show conflicting units.
- **Bars are flattened to their `Mean`** so every series is a single scalar line; bar min/max/count
  overlays are out of scope for v1.
- **Tab visibility is a server render-time decision.** `SelectNode` sets
  `SelectedNodeViewModel.ShowChartTab = GetComparableChildGroups(nodeId).Count > 0`. Enabled for
  product/tree **nodes** only, not folders. The flag is **reset to `false` on every selection change**
  (`SelectedNodeViewModel.Subscribe`) because the view-model instance is reused — otherwise a node's
  `true` would leak onto the next selected folder and render a Chart tab that has no endpoint.
- **Empty series are omitted, not zero-filled.** A window with no data for a child drops that line.
  A child whose read **throws** is treated the same way — logged and dropped as a single series, so one
  malformed sensor can't fault the fan-out and 500 the whole overlay. **Non-finite points (`NaN`/
  `Infinity`) are skipped** in `TryGetScalar` (they'd serialize as JSON string literals Plotly can't
  plot); a child left with no finite points is omitted like an empty window.
- **At most `MaxSensorsPerChart` (20) sensors are overlaid — chosen from those that actually have data.**
  The endpoint reads every sensor in the group (bounded per sensor, plus a `NodeChartMaxSensorsScanned`
  = 500 scan ceiling), drops the ones with no data in the window, and only then caps the display to the
  **20 highest-peak** series. Capping by tree order *before* reading is wrong for intermittent top-N
  series (per-process CPU, etc.): the first ids by path are usually idle in any given window, so it
  collapsed the chart to a single line. The note reports "20 highest-peak of N with data (M in group)".
- Scope is the node's full descendant set (`GetAllNodeSensors`); the two real examples
  (`Top CPU processes`, per-interface `Network`) are homogeneous. Direct-children-vs-subtree toggle
  is v3.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Select a node -> open Chart tab -> pick a window (Last hour / 3h / day / Custom) -> one overlaid line per comparable child | operator |
| 2 | Change the window -> re-query + redraw; children absent in the new window drop out | operator |
| 3 | On a multi-group node, pick a different `(type, unit)` group from the **Type** selector -> re-query + redraw that group | operator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `POST SensorHistory/NodeChartHistory` | `Controllers/SensorHistoryController.cs` | Body `NodeChartRequest { NodeId, GroupKey?, From, To }`. Returns `{ error, unit, note, selectedKey, groups: [{ key, label, count }], series: [{ id, label, values: [{ time, value }] }] }`. |
| `NodeChartRequest` | `Model/History/NodeChartRequest.cs` | `NodeId` is the node's GUID string (encoded id == `ToString()`); optional `GroupKey` selects the group; `From`/`To` are UTC instants. |
| `NodeSensorGroup` | `Model/TreeViewModels/NodeSensorGroup.cs` | `(SensorType Type, int? UnitCode, string UnitLabel, List<Guid> SensorIds)`; `Key` = stable `"{typeInt}:{unitCode}"` used by the group selector. |
| `TreeViewModel.GetComparableChildGroups(Guid)` | `Model/TreeViewModels/TreeViewModel.cs` | Groups >= 2 comparable descendants by `(type, unit)`, largest first. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Model/TreeViewModels/TreeViewModel.cs` | `GetComparableChildGroups` + comparability/unit helpers. |
| `src/server/HSMServer/Model/TreeViewModels/NodeSensorGroup.cs` | Comparable-group record. |
| `src/server/HSMServer/Model/TreeViewModels/SelectedNodeViewModel.cs` | `ShowChartTab` flag. |
| `src/server/HSMServer/Controllers/HomeController.cs` | `SelectNode` sets `ShowChartTab` for nodes. |
| `src/server/HSMServer/Controllers/SensorHistoryController.cs` | `NodeChartHistory` endpoint + scalar projection (`BuildNodeChartSeries`, `TryGetScalar`, `GetNodeRelativeLabel`). |
| `src/server/HSMServer/Model/History/NodeChartRequest.cs` | Request DTO. |
| `src/server/HSMServer/Views/Home/_ChildrenPanel.cshtml` | Conditional Chart tab link + pane. |
| `src/server/HSMServer/Views/Home/_NodeChartTabContent.cshtml` | Window picker, notes, graph div, and the inline Plotly overlay renderer. |

## Data Flow

1. `SelectNode` connects the node and computes `ShowChartTab` from `GetComparableChildGroups`.
2. `_ChildrenPanel` renders the Chart tab only when `ShowChartTab`; the pane includes
   `_NodeChartTabContent`.
3. On tab click / window change, the inline script POSTs `{ nodeId, from, to }` to
   `NodeChartHistory`.
4. The endpoint takes the largest comparable group, reads each sensor's history in the window
   (`GetSensorValues`, latest N, bounded), converts to display values, projects each to a scalar
   (`Value`, or bar `Mean`), and returns one `series` entry per non-empty child.
5. The client builds one `scattergl` line per series (palette-cycled, `connectgaps: false`) and
   calls `Plotly.newPlot` with a shared legend. Global `Plotly`/`jQuery` are reused — no bundle
   rebuild.

## Storage / Persistence

None. Reuses the existing sensor-history read path (`ITreeValuesCache.GetSensorValuesPage`).

## UI / Operator Visibility

Node data panel -> **Chart** tab (only when the node has >= 2 comparable children). Window picker
(Last hour / Last 3 hours / Last day / Custom From-To). A static note explains omitted/gapped
series; a dynamic note appears for mixed-unit nodes.

## Dependencies

- Depends on: sensor-tree (`TreeViewModel`, `GetAllNodeSensors`), sensor history read path,
  bundled Plotly.js 2.28.0.
- Used by: operators triaging a node's children at a glance.

## Tests

Manual acceptance (issue #1235):

- `Top CPU processes` -> Chart -> Last hour -> one line per process.
- `Network` -> one line per interface (MB/s).
- Node with < 2 comparable children -> no Chart tab.
- Change window -> re-query + redraw; children with no data in-window are absent, not zeroed.

## Notes

- Bars render as a single Mean line in v1; per-property (min/max/count) overlays and rate/counter
  delta strategies are deferred.
- Bounds (all constants in `SensorHistoryController`): `NodeChartMaxPointsPerSensor` (2000) values per
  child; `NodeChartMaxSensorsScanned` (500) sensors read per request; `MaxSensorsPerChart` (20) lines
  drawn (the highest-peak of those with data). **All three surface a `note` when they clip** — the
  2000-point cap takes the most-recent values, so a dense series over a long window notes that earlier
  points may be omitted (no silent truncation).

## Known Issues / Limitations

- **v2 — Ranking:** a sorted "top consumers" leaderboard (total / average / peak) as a second tab
  on the same data.
- **v3:** direct-children-vs-subtree scope toggle, per-type aggregation strategies (rate/counter
  delta, enum/bool availability = % uptime / flaps). Mixed-unit nodes are already handled by the group
  selector (one unit charted at a time), so the earlier "largest group only + note" behavior is gone.
- Group selection is by `(type, unit)`; a lone comparable sensor of a different unit (a singleton
  group) still isn't chartable on its own — it needs a peer to form a group of >= 2.
