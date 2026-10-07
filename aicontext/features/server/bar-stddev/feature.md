# Feature: Bar standard deviation (StdDev, σ)

> Owner: shared | Last reviewed: 2026-10-06 | Canonical: yes
> Scope: the per-bar population standard deviation — how the server receives, stores, compresses and exposes it, and how the sensor page and dashboards draw it (#1509).

---

## Overview

A bar carries `Min`, `Max`, `Mean`, `Count`, `FirstValue`, `LastValue` and, since #1509, `StdDev`: the
**population** standard deviation of the bar's samples, `sqrt(Σ(x − mean)² / Count)`. One spike stretches
the candlestick's whisker to the ceiling and hides the typical behaviour inside a 5-minute bar; a mean
line with a ±σ band shows it. **The native C++ collector computes it; the managed .NET collector does
not** (owner decision 2026-10-07, #1529, [ADR-0009](../../../../docs/decisions/0009-bar-stddev-native-only.md)),
so bars from managed hosts show no band. Collector side:
[`collector/sensors/feature.md`](../../collector/sensors/feature.md#bar-mechanics); wire:
[`api/wire-contract/feature.md`](../../api/wire-contract/feature.md#bar-stddev-1509). Reading the band:
for normally distributed samples mean ± 1σ holds about 68 % of the values; CPU load, temperature or write
speed inside a bar are not normal, so it is a rule of thumb — the hover text keeps Min/Max/Count so the
tails stay visible.

## Invariants

- **Unknown is not 0.** `StdDev` is `double?` everywhere on the server. `null` = unknown: a collector that
  does not send it (the managed collector, native < 0.11.0, any other sender), a row stored before the
  field existed, or a bar built from pre-aggregated partials (`AddPartial` carries no spread). Nothing
  draws, plots or reports an unknown σ as 0. An incoming σ that is negative, NaN or infinite is stored
  as unknown too (`BarStdDev.Normalize`, applied in both `ApiConverters` sites — `HSMServer.Core.ApiObjectsConverters` and `HSMServer.ApiObjectsConverters`).
- **Additive on the wire.** Wire and DTO only gain an optional field: older collectors keep working
  with a newer server, and older servers ignore the field from newer collectors. **Storage is not
  backward compatible** — see the next point.
- **Storage format — forward-only.** `BarBaseValue<T>.StdDev` is the LAST member of the
  MemoryPack-serialized bar records (`IntegerBarValue`/`DoubleBarValue`). These records use MemoryPack's
  default (not version-tolerant) layout: members in declaration order behind a member count. An old row
  has one member fewer and reads with `StdDev = null` (pinned by
  `MemoryPackFormatterTests.Bars_stored_before_stddev_read_with_it_unknown`, bytes captured from the
  formatter before the field existed), but a reader whose schema has FEWER members than the row throws.
  So a server older than #1509 cannot read any bar row written after the upgrade — **rolling the server
  back means restoring a database backup taken before the upgrade** (or losing the bar rows written
  since). Inserting or reordering members would break old rows too; only appending is safe.
- **Exact compression.** When the sensor page compresses history into coarser bars, σ of the merged bar
  is computed from the parts' (Count, Mean, σ) alone with the parallel-variance formula — no raw
  samples:

  ```
  N  = Σ nᵢ                       μ = Σ nᵢ·μᵢ / N
  M2 = Σ nᵢ·(σᵢ² + (μᵢ − μ)²)     σ = sqrt(M2 / N)
  ```

  Timeout rows (a copy of the last bar with `IsTimeout`, `GetTimeoutValue`) and parts with
  `Count ≤ 0` carry no new samples and are skipped; one counted part keeps its σ unchanged; any counted
  part with an unknown σ makes the merged σ unknown. The parts' Mean and σ are the collector's rounded
  values, so the result is exact for the stored parts (`BarStdDev.Combine`) — with one caveat: an int
  bar's Mean is rounded to an integer, so for int bars with a small spread the between-bar term carries
  up to 0.5 of rounding per part and the merged σ can come out high (samples {0,1} and {1,2} store
  means 0 and 2 and merge to 1.12 instead of 0.71).
- **Partial bars.** Same-`OpenTime` partial posts replace each other in `BarValuesStorage` (the
  collector's running σ rides on each post like Mean), so no σ merge happens on ingestion;
  `BarBaseValue<T>.TrySetValue` and `NotCompressedValue<T>` copy σ like the other fields.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | A collector posts a bar with `StdDev`; the server stores it (`ApiConverters.Convert`) | collector |
| 2 | Sensor page: history compressed to the visible point budget combines σ exactly (`BarHistoryProcessor`) | operator |
| 3 | Sensor page: the bar picker switches **Candlestick** / **Mean ± σ** | operator |
| 4 | Dashboard: a bar source plots the `StdDev (σ)` property | operator |
| 5 | Sensor API `/api/sensors/history` (JSON), `historyFile` (CSV), Grafana JSON datasource read σ | integrator |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `IntBarSensorValue.StdDev` / `DoubleBarSensorValue.StdDev` (`double?`) | `src/api/HSMSensorDataObjects/SensorValueRequests/{Int,Double}BarSensorValue.cs` | wire field, right after `Type`; DTO 3.2.0; not on `BarSensorValueBase<T>`, so the managed collector's bars never carry it |
| `BarBaseValue<T>.StdDev` (`double?`) | `src/server/HSMCommon/SensorValues/BarBaseValue.cs` | stored; last MemoryPack member |
| `BarSensorHistory.StdDev` (`string`, null = unknown) | `src/server/HSMServer.Core/Model/HistoryValues/BarSensorHistory.cs` | Sensor API JSON history, formatted like `Mean` |
| CSV column `StdDev` (the LAST column of the bar export, so existing columns keep their positions) | `src/server/HSMServer/ApiObjectsConverters/ApiCsvConverters.cs` | empty cell when unknown |
| Grafana table column `StdDev` (the LAST column, after `Comment`, so existing columns keep their positions) | `.../GrafanaDatasources/JsonSource/JsonHistoryResponse/BarHistoryTableResponse.cs` | `null` when unknown |
| `PlottedProperty.StdDev = 57` | `src/server/HSMServer/Dashboards/Panels/Modules/BasePlotPanelModule.cs` | persisted panel property value; never renumber |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMCommon/SensorValues/BarStdDev.cs` | exact parallel-variance combination |
| `src/server/HSMServer/Model/History/Personal/HistoryProcessor/Processor/BarHistoryProcessor.cs` | sensor-page compression (collects each part's Count/Mean/σ) |
| `src/server/HSMServer/Datasources/Lines/BarSensorLineDatasources.cs` | dashboard `StdDev` line source (skips unknown σ) |
| `src/server/HSMServer/wwwroot/src/js/plots.js` (`BarPLot`, `BarView`) | candlestick / mean ± σ traces |
| `src/server/HSMServer/wwwroot/src/js/plotting.js` (`setBarView`, `getBarView`) | view switch, persisted per graph in `localStorage` |
| `src/Directory.Build.targets` | makes the server build against the in-repo DTO (see Notes) |

## Data Flow

Collector → `/api/sensors/*` (DTO `StdDev`) → `ApiConverters.Convert` → `BarBaseValue<T>.StdDev` →
LevelDB (MemoryPack) → history reads → sensor page (`BarHistoryProcessor` compression → `BarPLot`),
dashboards (`BarBaseStdDevLineDatasource`), Sensor API / CSV / Grafana outputs.

## Storage / Persistence

Bar rows in the sensor-values LevelDB store, MemoryPack format, `StdDev` appended as the last member. No
migration: old rows read with `StdDev = null`. **Forward-only:** every bar row written by this server
version or later carries one more member, which a pre-#1509 server rejects when it reads the row
(MemoryPack's default layout throws on a member count larger than its schema). A rollback to an older
server therefore needs the database backup taken before the upgrade; there is no down-migration.
A version-tolerant layout was deliberately not introduced (it would rewrite every stored row).

## UI / Operator Visibility

- **Sensor page.** The bar picker (⋮ next to the chart) gains a view switch: **Candlestick** (default:
  body FirstValue→LastValue, whiskers Min/Max — drawn exactly as before #1509; the existing **Mean**
  checkbox still adds a mean line) and **Mean ± σ** (a mean line with a filled band from mean − σ to
  mean + σ). The choice is remembered per sensor graph
  (`localStorage` key `barView_graph_<id>`). The **Bar** checkbox shows/hides the whole view. Bars with an
  unknown σ break the band (one filled polygon per run of known σ) — no band is drawn there, the mean line
  continues. Hover text: min, mean, max, σ (only when known), count, open/close time.
- **Dashboards.** `StdDev (σ)` is selectable for bar sources next to Min/Mean/Max/Count; bars with an
  unknown σ and timeout rows are left out of the line. When the panel downsamples several bars into one visible point, σ
  is averaged like Mean (a display approximation — a one-number line point does not keep Count/Mean);
  the sensor page compresses exactly.
- The dashboard "Bar" property (candlestick) is unchanged: no mean line, no view switch.

## Dependencies

- Depends on: the native collector's bar accumulation (0.11.0; the managed collector sends no σ, #1529), `HSMSensorDataObjects` 3.2.0.
- Used by: sensor page chart, dashboards, Sensor API history, Grafana datasource.

## Tests

- `src/tests/HSMServer.Core.Tests/Model/BarStdDevTests.cs` — exact combination (textbook series split in
  two bars → 2), count weighting, unknown propagation, zero-count skip, single-part pass-through, the
  double and int history processors, Sensor API history output.
- `src/tests/HSMDatabase.LevelDB.Tests/SensorValuesDBTests/MemoryPackFormatterTests.cs` — pre-#1509 rows
  read with σ unknown; σ round-trips.
- `ApiSensorValuesToServerValuesConverterTests` — the DTO → stored value conversion carries σ.

## Notes

- **In-repo DTO for the server build.** Since collector 3.5.0 the `HSMDataCollector` NuGet package
  bundles its own `HSMSensorDataObjects.dll`, and the server consumes that package while also referencing
  the DTO project. With equal versions the package's copy won, so a DTO change stayed invisible to the
  server until the next collector release; with different versions the compiler fails (CS1704).
  `src/Directory.Build.targets` drops the package's copy whenever the in-repo DTO project is among the
  resolved references; the package's collector code then binds to the in-repo DTO at run time — safe
  because DTO changes are additive.
- The server's own self-monitoring bars come from the managed `HSMDataCollector` package, so they never
  carry σ (#1529) and show no band.

## Known Issues / Limitations

- Dashboard downsampling averages σ (see above) instead of pooling it.
- Compressed σ of int bars inherits the rounding of their integer Mean (see Invariants).
- No alerts on σ (`AlertProperty` has no StdDev member) and no σ column in the sensor page's history
  table; both would be follow-ups.
